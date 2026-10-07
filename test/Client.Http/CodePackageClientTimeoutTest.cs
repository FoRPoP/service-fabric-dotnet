using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Common;
using Xunit;

namespace Microsoft.ServiceFabric.Client.Http;

public abstract partial class CodePackageClientTest
{
    public sealed class SetCodePackagePatchAsync_Timeout
    {
        readonly CodePackagePatchSetDescription description = new("fabric:/timeout-test", "Code", "1.0",
            new ImageStoreCodePackagePatchSource("Uploads/timeout-test"));

        [Fact]
        public async Task AllowsOperationToOutlastConnectionTimeoutWithoutChangingOtherCalls()
        {
            using HoldingHandler handler = new();
            using IServiceFabricClient client = await CreateClient(handler, TimeSpan.FromMilliseconds(30));
            Task<CodePackagePatchResult> pending = client.CodePackages.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken);
            await handler.Started.Task;

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.Nodes.GetNodeInfoListAsync(
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.False(pending.IsCompleted);
            handler.Release.TrySetResult(true);
            Assert.Empty((await pending).SuccessfulNodes);
        }

        [Fact]
        public async Task CancelsWhenOperationTimeoutExpires()
        {
            using HoldingHandler handler = new();
            using IServiceFabricClient client = await CreateClient(handler, TimeSpan.FromMinutes(10));

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CodePackages.SetCodePackagePatchAsync(
                description, 1, TestContext.Current.CancellationToken));

            Assert.True(handler.ObservedCancellation.IsCancellationRequested);
        }

        [Fact]
        public async Task PreservesCallerCancellation()
        {
            using HoldingHandler handler = new();
            using IServiceFabricClient client = await CreateClient(handler, TimeSpan.FromMinutes(10));
            using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            Task<CodePackagePatchResult> pending = client.CodePackages.SetCodePackagePatchAsync(description,
                cancellationToken: cancellation.Token);
            await handler.Started.Task;

            cancellation.Cancel();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.True(handler.ObservedCancellation.IsCancellationRequested);
        }

        [Fact]
        public async Task KeepsConcurrentPatchBudgetsIndependent()
        {
            using HoldingHandler handler = new();
            using IServiceFabricClient client = await CreateClient(handler, TimeSpan.FromMinutes(10));
            Task<CodePackagePatchResult> longer = client.CodePackages.SetCodePackagePatchAsync(description,
                60, TestContext.Current.CancellationToken);
            await handler.Started.Task;
            Task<CodePackagePatchResult> shorter = client.CodePackages.SetCodePackagePatchAsync(description,
                1, TestContext.Current.CancellationToken);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shorter);

            Assert.False(longer.IsCompleted);
            handler.Release.TrySetResult(true);
            Assert.Empty((await longer).SuccessfulNodes);
        }

        static Task<IServiceFabricClient> CreateClient(HoldingHandler handler, TimeSpan timeout) => new ServiceFabricClientBuilder()
            .UseEndpoints(new Uri("http://timeout-test.invalid"))
            .ConfigureClientSettings(settings => settings.ClientTimeout = timeout)
            .ConfigureHttpClientSettings(new HttpClientHandler(), handler)
            .BuildAsync(TestContext.Current.CancellationToken);

        sealed class HoldingHandler : DelegatingHandler
        {
            internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal CancellationToken ObservedCancellation;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri.AbsolutePath.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal))
                {
                    ObservedCancellation = cancellationToken;
                    TaskCompletionSource<bool> canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    using CancellationTokenRegistration registration = cancellationToken.Register(() => canceled.TrySetCanceled());
                    Started.TrySetResult(true);
                    await await Task.WhenAny(Release.Task, canceled.Task);
                }
                else
                    await Task.Delay(Timeout.Infinite, cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("{\"SuccessfulNodes\":[],\"FailedNodes\":[],\"UnresolvedNodes\":[]}")
                };
            }
        }
    }
}