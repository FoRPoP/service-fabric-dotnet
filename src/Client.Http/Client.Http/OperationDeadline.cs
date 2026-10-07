using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Microsoft.ServiceFabric.Client.Http;

sealed class OperationDeadline : IDisposable
{
    static readonly TimeSpan maximumTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1L);
    readonly object sync = new();
    readonly Stopwatch elapsed = Stopwatch.StartNew();
    readonly TimeSpan timeout;
    readonly CancellationTokenSource cancellation;
    readonly Timer timer;
    bool disposed;

    internal OperationDeadline(long serverTimeout, CancellationToken cancellationToken)
    {
        if (serverTimeout < 1 || serverTimeout > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(serverTimeout));

        timeout = TimeSpan.FromSeconds(serverTimeout);
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = cancellation.Token;
        timer = new Timer(CheckDeadline, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        CheckDeadline(null);
    }

    internal CancellationToken Token { get; }

    internal Uri ApplyTo(Uri uri)
    {
        Token.ThrowIfCancellationRequested();
        TimeSpan remaining = timeout - elapsed.Elapsed;
        if (remaining <= TimeSpan.Zero)
            throw new OperationCanceledException("The code package patch operation timed out.", Token);

        string value = Math.Ceiling(remaining.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        string[] parameters = uri.Query.TrimStart('?').Split('&');
        UriBuilder builder = new(uri)
        {
            Query = string.Join("&", parameters.Where(parameter => parameter.Length > 0 &&
                !parameter.StartsWith("timeout=", StringComparison.Ordinal)).Concat(new[] { "timeout=" + value }))
        };
        return builder.Uri;
    }

    void CheckDeadline(object state)
    {
        lock (sync)
        {
            if (disposed)
                return;

            TimeSpan remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                cancellation.Cancel();
            else
                timer.Change(remaining > maximumTimerDelay ? maximumTimerDelay : remaining, Timeout.InfiniteTimeSpan);
        }
    }

    void IDisposable.Dispose()
    {
        lock (sync)
        {
            disposed = true;
            timer.Dispose();
        }
        cancellation.Dispose();
    }
}