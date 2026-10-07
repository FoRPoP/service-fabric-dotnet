using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fuzzy;
using Microsoft.ServiceFabric.Common;
using Microsoft.ServiceFabric.Common.Exceptions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.ServiceFabric.Client.Http;

public abstract partial class CodePackageClientTest : IDisposable
{
    readonly ICodePackageClient sut;
    readonly RecordingHandler handler = new();
    readonly IServiceFabricClient client;
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    CodePackageClientTest()
    {
        client = new ServiceFabricClientBuilder()
            .UseEndpoints(new Uri($"http://{fuzzy.String().LettersOrDigits()}.invalid"))
            .ConfigureHttpClientSettings(new HttpClientHandler(), handler)
            .BuildAsync().GetAwaiter().GetResult();
        sut = client.CodePackages;
    }

    void IDisposable.Dispose() => client.Dispose();

    public sealed class SetCodePackagePatchAsync : CodePackageClientTest
    {
        readonly CodePackagePatchSetDescription codePackagePatchSetDescription = new(
            $"fabric:/{fuzzy.String().LettersOrDigits()}", fuzzy.String(), fuzzy.String(),
            new ImageStoreCodePackagePatchSource(fuzzy.String()), contentName: fuzzy.String(),
            nodes: fuzzy.Array(fuzzy.String));

        [Fact]
        public async Task SendsVersionQualifiedRequestWithDefaultTimeout()
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            _ = await sut.SetCodePackagePatchAsync(codePackagePatchSetDescription,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal("/$/SetCodePackagePatch", handler.RequestUri.AbsolutePath);
            AssertTimeout(1800, elapsed.Elapsed);
            Assert.Equal(codePackagePatchSetDescription.ServiceName.ToString(), handler.Body["ServiceName"]);
            Assert.Equal(codePackagePatchSetDescription.CodePackageName, handler.Body["CodePackageName"]);
            Assert.Equal(codePackagePatchSetDescription.CodePackageVersion, handler.Body["CodePackageVersion"]);
            Assert.Equal(codePackagePatchSetDescription.ContentName, handler.Body["ContentName"]);
            Assert.Equal(codePackagePatchSetDescription.Nodes, handler.Body["Nodes"].Values<string>());
            Assert.Equal("ImageStore", handler.Body["Source"]["Kind"]);
            Assert.Equal(((ImageStoreCodePackagePatchSource)codePackagePatchSetDescription.Source).ImageStoreRelativePath,
                handler.Body["Source"]["ImageStoreRelativePath"]);
            Assert.Null(handler.Body["PartitionId"]);
        }

        [Fact]
        public async Task SendsDistinctProvisionedSourceName()
        {
            ProvisionedCodePackagePatchSource source = new(fuzzy.String(), fuzzy.String(), fuzzy.String(),
                codePackagePatchSetDescription.CodePackageName + fuzzy.String());
            CodePackagePatchSetDescription description = new(codePackagePatchSetDescription.ServiceName,
                codePackagePatchSetDescription.CodePackageName, codePackagePatchSetDescription.CodePackageVersion,
                source);

            _ = await sut.SetCodePackagePatchAsync(description, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(description.CodePackageName, handler.Body["CodePackageName"]);
            Assert.Equal(source.CodePackageName, handler.Body["Source"]["CodePackageName"]);
            Assert.Equal(source.ApplicationTypeName, handler.Body["Source"]["ApplicationTypeName"]);
            Assert.Equal(source.ApplicationTypeVersion, handler.Body["Source"]["ApplicationTypeVersion"]);
            Assert.Equal(source.ServiceManifestName, handler.Body["Source"]["ServiceManifestName"]);
        }

        [Fact]
        public async Task SendsExactPartitionTarget()
        {
            PartitionId partitionId = new(Guid.NewGuid());
            CodePackagePatchSetDescription description = new(codePackagePatchSetDescription.ServiceName,
                codePackagePatchSetDescription.CodePackageName, codePackagePatchSetDescription.CodePackageVersion,
                codePackagePatchSetDescription.Source, partitionId);

            _ = await sut.SetCodePackagePatchAsync(description, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(partitionId.ToString(), handler.Body["PartitionId"]);
        }

        [Fact]
        public async Task ReturnsPublicationWhenNoNodeAccepted()
        {
            string patchId = fuzzy.String();
            string contentId = fuzzy.String();
            string path = fuzzy.String();
            handler.Response["PatchId"] = patchId;
            handler.Response["ContentId"] = contentId;
            handler.Response["ImageStoreRelativePath"] = path;
            handler.Response["UnresolvedNodes"] = new JArray(new JObject
            {
                ["NodeName"] = fuzzy.String(),
                ["Error"] = new JObject { ["Code"] = "FABRIC_E_TIMEOUT", ["Message"] = fuzzy.String() }
            });

            CodePackagePatchResult actual = await sut.SetCodePackagePatchAsync(codePackagePatchSetDescription,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(patchId, actual.PatchId);
            Assert.Equal(contentId, actual.ContentId);
            Assert.Equal(path, actual.ImageStoreRelativePath);
            Assert.Empty(actual.SuccessfulNodes);
            Assert.Equal((string)handler.Response["UnresolvedNodes"][0]["NodeName"], Assert.Single(actual.UnresolvedNodes).NodeName.ToString());
        }

        [Fact]
        public async Task ThrowsWhenDescriptionIsNull()
        {
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SetCodePackagePatchAsync(null,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(nameof(codePackagePatchSetDescription), exception.ParamName);
        }

        [Fact]
        public async Task SendsProvidedServerTimeout()
        {
            long serverTimeout = fuzzy.Int64().Between(1, 4294967295);

            Stopwatch elapsed = Stopwatch.StartNew();
            _ = await sut.SetCodePackagePatchAsync(codePackagePatchSetDescription, serverTimeout,
                TestContext.Current.CancellationToken);

            AssertTimeout(serverTimeout, elapsed.Elapsed);
        }
    }

    public sealed class ClearCodePackagePatchAsync : CodePackageClientTest
    {
        readonly CodePackagePatchClearDescription codePackagePatchClearDescription = new(fuzzy.String());

        [Fact]
        public async Task SendsOnlyStableTargetIdentifier()
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            _ = await sut.ClearCodePackagePatchAsync(codePackagePatchClearDescription,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpMethod.Post, handler.Method);
            Assert.Equal("/$/ClearCodePackagePatch", handler.RequestUri.AbsolutePath);
            AssertTimeout(60, elapsed.Elapsed);
            Assert.Equal(nameof(CodePackagePatchClearDescription.PatchId), Assert.Single(handler.Body.Properties()).Name);
            Assert.Equal(codePackagePatchClearDescription.PatchId, handler.Body["PatchId"]);
        }

        [Fact]
        public async Task ThrowsWhenDescriptionIsNull()
        {
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => sut.ClearCodePackagePatchAsync(null,
                cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal(nameof(codePackagePatchClearDescription), exception.ParamName);
        }
    }

    public sealed class GetCodePackagePatchListAsync : CodePackageClientTest
    {
        [Fact]
        public async Task SendsNoBodyOrImplicitFilters()
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            CodePackagePatchResult actual = await sut.GetCodePackagePatchListAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(HttpMethod.Get, handler.Method);
            Assert.Equal("/$/GetCodePackagePatchList", handler.RequestUri.AbsolutePath);
            Assert.Null(handler.Body);
            Assert.Equal(new[] { "api-version", "timeout" }, Query().Keys.OrderBy(name => name));
            AssertTimeout(60, elapsed.Elapsed);
            Assert.Empty(actual.SuccessfulNodes);
            Assert.Empty(actual.FailedNodes);
            Assert.Empty(actual.UnresolvedNodes);
        }

        [Fact]
        public async Task SendsVersionWithoutCompanionFilters()
        {
            string codePackageVersion = fuzzy.String();

            _ = await sut.GetCodePackagePatchListAsync(codePackageVersion: codePackageVersion,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(codePackageVersion, Query()["CodePackageVersion"]);
            Assert.DoesNotContain("CodePackageName", Query().Keys);
            Assert.DoesNotContain("ServiceName", Query().Keys);
        }

        [Fact]
        public async Task ReturnsIndependentVersionMappingsSharingContent()
        {
            string contentId = fuzzy.String();
            string version = fuzzy.String();
            string differentVersion = version + fuzzy.String();
            string patchId = fuzzy.String();
            string differentPatchId = patchId + fuzzy.String();
            JObject current = Mapping(patchId, version, contentId, "ReplacementRunning");
            JObject inactive = Mapping(differentPatchId, differentVersion, contentId, "DifferentCodePackageVersion");
            handler.Response["SuccessfulNodes"] = new JArray(new JObject
            {
                ["NodeName"] = fuzzy.String(),
                ["Patches"] = new JArray(current, inactive)
            });

            CodePackagePatchResult actual = await sut.GetCodePackagePatchListAsync(
                cancellationToken: TestContext.Current.CancellationToken);

            CodePackagePatchInfo[] mappings = Assert.Single(actual.SuccessfulNodes).Patches.ToArray();
            Assert.Equal(new[] { patchId, differentPatchId }, mappings.Select(mapping => mapping.PatchId));
            Assert.Equal(new[] { version, differentVersion }, mappings.Select(mapping => mapping.Target.CodePackageVersion));
            Assert.All(mappings, mapping => Assert.Equal(contentId, mapping.ContentId));
            Assert.Equal(CodePackagePatchState.Active, mappings[1].State);
            Assert.Equal(CodePackagePatchProgress.DifferentCodePackageVersion, mappings[1].Progress);
            Assert.Null(mappings[1].LastError);
        }

        [Fact]
        public async Task EncodesPatchIdentifierAndNodesAsSingleValues()
        {
            string patchId = $"v1/{fuzzy.String()}/package";
            string nodes = $"\"{fuzzy.String()},&=\"";

            _ = await sut.GetCodePackagePatchListAsync(patchId: patchId, nodes: nodes,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(patchId, Query()["PatchId"]);
            Assert.Equal(nodes, Query()["Nodes"]);
        }

        [Fact]
        public async Task EncodesServiceAndPackageFiltersIndependently()
        {
            string serviceName = $"fabric:/{fuzzy.String().LettersOrDigits()}/name&value";
            string codePackageName = fuzzy.String() + "&name=value";
            string codePackageVersion = fuzzy.String() + "&version=value";
            PartitionId partitionId = new(Guid.NewGuid());

            _ = await sut.GetCodePackagePatchListAsync(serviceName: serviceName, codePackageName: codePackageName,
                codePackageVersion: codePackageVersion, partitionId: partitionId,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(serviceName, Query()["ServiceName"]);
            Assert.Equal(codePackageName, Query()["CodePackageName"]);
            Assert.Equal(codePackageVersion, Query()["CodePackageVersion"]);
            Assert.Equal(partitionId.ToString(), Query()["PartitionId"]);
        }

        [Fact]
        public async Task PreservesServiceErrors()
        {
            string message = fuzzy.String();
            handler.StatusCode = HttpStatusCode.BadRequest;
            handler.Response = new JObject
            {
                ["Error"] = new JObject { ["Code"] = "E_INVALIDARG", ["Message"] = message }
            };

            var exception = await Assert.ThrowsAsync<ServiceFabricException>(() => sut.GetCodePackagePatchListAsync(
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(message, exception.Message);
        }

        static JObject Mapping(string patchId, string version, string contentId, string progress) => new()
        {
            ["PatchId"] = patchId,
            ["Target"] = new JObject
            {
                ["ApplicationInstanceId"] = fuzzy.String(),
                ["ServiceManifestName"] = fuzzy.String(),
                ["CodePackageName"] = fuzzy.String(),
                ["CodePackageVersion"] = version
            },
            ["Revision"] = fuzzy.UInt64().ToString(),
            ["State"] = "Active",
            ["ContentId"] = contentId,
            ["ContentReady"] = true,
            ["Progress"] = progress
        };
    }

    void AssertTimeout(long seconds, TimeSpan elapsed) =>
        Assert.InRange(long.Parse(Query()["timeout"]), Math.Max(1, seconds - (long)Math.Ceiling(elapsed.TotalSeconds)), seconds);

    Dictionary<string, string> Query() => handler.RequestUri.Query.TrimStart('?').Split('&')
        .Select(item => item.Split(['='], 2))
        .ToDictionary(item => Uri.UnescapeDataString(item[0]), item => Uri.UnescapeDataString(item[1]));

    sealed class RecordingHandler : DelegatingHandler
    {
        internal readonly ConcurrentQueue<RecordedRequest> Requests = new();
        internal string ApplicationId = fuzzy.String().LettersOrDigits() + "~" + fuzzy.String().LettersOrDigits();
        internal string ApplicationTypeName = fuzzy.String().LettersOrDigits();
        internal bool UploadMode;
        internal bool FailUploads;
        internal bool FailLargeFiles;
        internal bool FailSet;
        internal bool FailCleanup;
        internal bool RejectCleanupCredentials;
        internal int UploadFailuresRemaining;
        internal Func<HttpRequestMessage, CancellationToken, Task> BeforeRequest;
        internal Func<CancellationToken, Task> BeforeUpload;
        internal HttpMethod Method;
        internal Uri RequestUri;
        internal JObject Body;
        internal HttpStatusCode StatusCode = HttpStatusCode.OK;
        internal HttpStatusCode SetStatusCode = HttpStatusCode.OK;
        internal JObject Response = new()
        {
            ["SuccessfulNodes"] = new JArray(),
            ["FailedNodes"] = new JArray(),
            ["UnresolvedNodes"] = new JArray()
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            byte[] bytes = request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync();
            Requests.Enqueue(new RecordedRequest(request.Method, request.RequestUri, bytes));
            Body = request.Content?.Headers.ContentType?.MediaType == "application/json"
                ? JObject.Parse(Encoding.UTF8.GetString(bytes)) : null;
            if (UploadMode)
            {
                if (BeforeRequest != null)
                    await BeforeRequest(request, cancellationToken);
                if (request.RequestUri.AbsolutePath.EndsWith("GetApplicationName", StringComparison.Ordinal))
                    return ResponseMessage(request, HttpStatusCode.OK, new JObject
                    {
                        ["Id"] = ApplicationId
                    }.ToString());
                if (request.RequestUri.AbsolutePath.StartsWith("/Applications/", StringComparison.Ordinal))
                    return ResponseMessage(request, HttpStatusCode.OK, new JObject
                    {
                        ["Id"] = ApplicationId,
                        ["TypeName"] = ApplicationTypeName
                    }.ToString());
                if (request.RequestUri.AbsolutePath.EndsWith("GetClusterManifest", StringComparison.Ordinal))
                    return ResponseMessage(request, HttpStatusCode.OK, new JObject
                    {
                        ["Manifest"] = "<ClusterManifest xmlns=\"http://schemas.microsoft.com/2011/01/fabric\"><FabricSettings><Section Name=\"Management\"><Parameter Name=\"ImageStoreConnectionString\" Value=\"fabric:ImageStore\" /></Section></FabricSettings></ClusterManifest>"
                    }.ToString());
                if (request.Method == HttpMethod.Put)
                {
                    if (BeforeUpload != null)
                        await BeforeUpload(cancellationToken);
                    if (FailUploads || (FailLargeFiles && bytes.LongLength > 25L * 1024 * 1024) ||
                        Interlocked.Decrement(ref UploadFailuresRemaining) >= 0)
                        throw new HttpRequestException("Interrupted test upload.");
                }
                if (FailSet && request.RequestUri.AbsolutePath.EndsWith("SetCodePackagePatch", StringComparison.Ordinal))
                    throw new HttpRequestException("The Set response was lost.");
                if (request.RequestUri.AbsolutePath.EndsWith("SetCodePackagePatch", StringComparison.Ordinal))
                {
                    string path = (string)Body["Source"]["ImageStoreRelativePath"];
                    Response["ImageStoreRelativePath"] = path;
                    Response["ContentId"] = path.Split('/').Last();
                    return ResponseMessage(request, SetStatusCode, Response.ToString());
                }
                if (RejectCleanupCredentials && request.Method == HttpMethod.Delete)
                    throw new Microsoft.ServiceFabric.Client.Exceptions.InvalidCredentialsException("Cleanup credentials expired.");
                if (FailCleanup && request.Method == HttpMethod.Delete)
                    return ResponseMessage(request, HttpStatusCode.InternalServerError,
                        new JObject { ["Error"] = new JObject { ["Code"] = "E_FAIL", ["Message"] = "Cleanup unavailable." } }.ToString());
            }
            return new HttpResponseMessage(StatusCode)
            {
                RequestMessage = request,
                Content = new StringContent(Response.ToString())
            };
        }

        static HttpResponseMessage ResponseMessage(HttpRequestMessage request, HttpStatusCode status, string body) => new(status)
        {
            RequestMessage = request,
            Content = new StringContent(body)
        };
    }

    sealed class RecordedRequest(HttpMethod method, Uri uri, byte[] body)
    {
        internal readonly HttpMethod Method = method;
        internal readonly Uri Uri = uri;
        internal readonly byte[] Body = body;
        internal string Path => Uri.UnescapeDataString(Uri.AbsolutePath).Replace('\\', '/');
    }
}