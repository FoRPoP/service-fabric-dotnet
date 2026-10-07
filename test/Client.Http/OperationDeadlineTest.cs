using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Client.Http;

public abstract class OperationDeadlineTest : IDisposable
{
    readonly OperationDeadline sut;
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    readonly long serverTimeout = fuzzy.Int64().Between(60, 600);
    readonly CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    OperationDeadlineTest() => sut = new OperationDeadline(serverTimeout, cancellation.Token);

    void IDisposable.Dispose()
    {
        ((IDisposable)sut).Dispose();
        cancellation.Dispose();
    }

    public sealed class Constructor : OperationDeadlineTest
    {
        [Theory, InlineData(0L), InlineData(-1L), InlineData(4294967296L)]
        public void RejectsTimeoutOutsidePublishedRange(long serverTimeout)
        {
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => new OperationDeadline(serverTimeout, cancellation.Token));
            Assert.Equal(nameof(serverTimeout), error.ParamName);
        }

        [Fact]
        public void SupportsTimeoutLongerThanSingleTimerRange()
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            using OperationDeadline deadline = new(uint.MaxValue, cancellation.Token);
            Uri actual = deadline.ApplyTo(new Uri("http://deadline.invalid/?timeout=60"));
            AssertBudget(actual, uint.MaxValue, elapsed.Elapsed);
        }
    }

    public sealed class ApplyTo : OperationDeadlineTest
    {
        readonly string nodes = fuzzy.String();

        [Fact]
        public void ReplacesTimeoutWithoutChangingEncodedFilters()
        {
            string query = "Nodes=" + Uri.EscapeDataString(nodes) + "&api-version=11.11";
            Uri uri = new("http://deadline.invalid/?timeout=999&" + query + "&timeout=123");

            Uri actual = sut.ApplyTo(uri);

            Assert.StartsWith("?" + query + "&timeout=", actual.Query);
            AssertBudget(actual, serverTimeout, elapsed.Elapsed);
        }

        [Fact]
        public void AddsTimeoutWhenQueryIsEmpty()
        {
            Uri actual = sut.ApplyTo(new Uri("http://deadline.invalid/"));
            Assert.StartsWith("?timeout=", actual.Query);
            AssertBudget(actual, serverTimeout, elapsed.Elapsed);
        }

        [Fact]
        public async Task ReducesTimeoutAsOperationConsumesBudget()
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);

            Uri actual = sut.ApplyTo(new Uri("http://deadline.invalid/"));

            long remaining = long.Parse(actual.Query.Split('=').Last());
            Assert.InRange(remaining, 1, serverTimeout - 1);
        }

        [Fact]
        public void RejectsRequestAfterCallerCancellation()
        {
            cancellation.Cancel();

            var error = Assert.Throws<OperationCanceledException>(() => sut.ApplyTo(new Uri("http://deadline.invalid/")));

            Assert.Equal(sut.Token, error.CancellationToken);
        }
    }

    public sealed class Dispose : OperationDeadlineTest
    {
        [Fact]
        public void DisconnectsCallerCancellation()
        {
            ((IDisposable)sut).Dispose();

            cancellation.Cancel();

            Assert.False(sut.Token.IsCancellationRequested);
        }
    }

    public sealed class Token : OperationDeadlineTest
    {
        [Fact]
        public void PropagatesCallerCancellation()
        {
            cancellation.Cancel();
            Assert.True(sut.Token.IsCancellationRequested);
        }

        [Fact]
        public async Task CancelsWhenOperationBudgetExpires()
        {
            using OperationDeadline deadline = new(1, cancellation.Token);
            TaskCompletionSource<bool> canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = deadline.Token.Register(() => canceled.TrySetResult(true));
            Task watchdog = Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Task completed = await Task.WhenAny(canceled.Task, watchdog);

            Assert.Same(canceled.Task, completed);
            Assert.True(deadline.Token.IsCancellationRequested);
        }
    }

    static void AssertBudget(Uri uri, long seconds, TimeSpan elapsed)
    {
        string timeout = Assert.Single(uri.Query.TrimStart('?').Split('&'), parameter => parameter.StartsWith("timeout=", StringComparison.Ordinal));
        long remaining = long.Parse(timeout.Substring("timeout=".Length));
        Assert.InRange(remaining, Math.Max(1, seconds - (long)Math.Ceiling(elapsed.TotalSeconds)), seconds);
    }
}