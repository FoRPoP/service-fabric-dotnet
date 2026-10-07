using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Fuzzy;
using Microsoft.ServiceFabric.Client.Exceptions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.ServiceFabric.Client.Http;

public abstract class ImageStoreClientTest : IDisposable
{
    readonly IImageStoreClient sut;
    readonly IServiceFabricClient client;
    readonly UploadHandler handler = new();
    readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    readonly string source;
    readonly string destination = Guid.NewGuid().ToString("N");
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    ImageStoreClientTest()
    {
        source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        client = new ServiceFabricClientBuilder().UseEndpoints(new Uri("http://upload-test.invalid"))
            .ConfigureHttpClientSettings(new HttpClientHandler(), handler).BuildAsync().GetAwaiter().GetResult();
        sut = client.ImageStore;
    }

    void IDisposable.Dispose()
    {
        client.Dispose();
        Directory.Delete(root, true);
    }

    public sealed class GetPathInImageStore : ImageStoreClientTest
    {
        [Theory, InlineData("/tmp/hotfix", "/tmp/hotfix/assets/tmp/hotfix/helper", "assets/tmp/hotfix/helper"),
            InlineData("/tmp/hotfix/", "/tmp/hotfix/code.bin", "code.bin"),
            InlineData("/tmp/hotfix", "/tmp/hotfix/", ""), InlineData("/", "/code.bin", "code.bin")]
        public void PreservesRelativePath(string root, string path, string relative)
        {
            string actual = ImageStoreClient.GetPathInImageStore(root, destination, path);

            Assert.Equal(relative.Length == 0 ? destination : destination + "/" + relative, actual);
        }

        [Theory, InlineData("/tmp/hotfix-other/code.bin"), InlineData("/tmp/hotfix/../other/code.bin")]
        public void RejectsPathsOutsideSource(string path)
        {
            var error = Assert.Throws<ArgumentException>(() => ImageStoreClient.GetPathInImageStore("/tmp/hotfix", destination, path));
            Assert.Equal(nameof(path), error.ParamName);
        }
    }

    public sealed class UploadApplicationPackageAsync : ImageStoreClientTest
    {
        [Theory, InlineData(false), InlineData(true)]
        public async Task PreservesEmptyDirectoriesInFileStore(bool trailingSeparator)
        {
            string store = Path.Combine(root, "store");
            handler.ImageStore = new Uri(store).AbsoluteUri;
            Directory.CreateDirectory(Path.Combine(source, "empty", "nested"));
            byte[] contents = fuzzy.Array(fuzzy.Byte);
            File.WriteAllBytes(Path.Combine(source, "code.bin"), contents);

            await sut.UploadApplicationPackageAsync(trailingSeparator ? source + Path.DirectorySeparatorChar : source, false, destination,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(Directory.Exists(Path.Combine(store, destination, "empty", "nested")));
            Assert.Equal(contents, File.ReadAllBytes(Path.Combine(store, destination, "code.bin")));
            Assert.Empty(handler.Uploads);
        }

        [Fact]
        public async Task DoesNotStartFileCopyAfterCancellation()
        {
            string store = Path.Combine(root, "store");
            handler.ImageStore = new Uri(store).AbsoluteUri;
            using CancellationTokenSource cancellation = new();
            handler.OnManifest = cancellation.Cancel;
            File.WriteAllBytes(Path.Combine(source, "code.bin"), fuzzy.Array(fuzzy.Byte));

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.UploadApplicationPackageAsync(source,
                false, destination, cancellationToken: cancellation.Token));

            Assert.False(Directory.Exists(store));
        }

        [Fact]
        public async Task UploadsLargeFileThroughOrdinaryPut()
        {
            byte lastByte = fuzzy.Byte().Minimum(1);
            using (FileStream file = new(Path.Combine(source, "large.bin"), FileMode.CreateNew))
            {
                file.SetLength(25L * 1024 * 1024 + 1);
                file.Position = file.Length - 1;
                file.WriteByte(lastByte);
            }

            await sut.UploadApplicationPackageAsync(source, false, destination,
                cancellationToken: TestContext.Current.CancellationToken);

            var upload = Assert.Single(handler.Uploads, item => item.Uri.AbsolutePath.EndsWith("large.bin", StringComparison.Ordinal));
            Assert.Equal("/ImageStore/" + destination + "/large.bin", Uri.UnescapeDataString(upload.Uri.AbsolutePath));
            Assert.True(upload.Streamed);
            Assert.Equal(25L * 1024 * 1024 + 1, upload.Length);
            Assert.Equal(upload.Length, upload.Body.LongLength);
            Assert.Equal(lastByte, upload.Body[upload.Body.Length - 1]);
            Assert.DoesNotContain(handler.Uploads, item => item.Uri.AbsolutePath.Contains("UploadChunk"));
            Assert.Equal(0, handler.Commits);
            using FileStream reopened = new(Path.Combine(source, "large.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(upload.Length, reopened.Length);
        }

        [Fact]
        public async Task ReopensFileFromTheBeginningOnRetry()
        {
            byte[] contents = fuzzy.Array(fuzzy.Byte);
            File.WriteAllBytes(Path.Combine(source, "code.bin"), contents);
            handler.UploadFailures = 1;

            await sut.UploadApplicationPackageAsync(source, false, destination,
                cancellationToken: TestContext.Current.CancellationToken);

            var uploads = handler.Uploads.Where(upload => upload.Uri.AbsolutePath.EndsWith("code.bin", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, uploads.Length);
            Assert.All(uploads, upload => Assert.Equal(contents, upload.Body));
            Assert.Equal(uploads[0].Uri, uploads[1].Uri);
        }

        [Fact]
        public async Task DoesNotCompleteDirectoryAfterFileRetriesFail()
        {
            byte[] contents = fuzzy.Array(fuzzy.Byte);
            File.WriteAllBytes(Path.Combine(source, "code.bin"), contents);
            handler.UploadFailures = int.MaxValue;

            _ = await Assert.ThrowsAsync<ServiceFabricRequestException>(() => sut.UploadApplicationPackageAsync(source,
                false, destination, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(0, handler.Commits);
            Assert.Equal(2, handler.Uploads.Count);
            Assert.All(handler.Uploads, upload => Assert.EndsWith("code.bin", upload.Uri.AbsolutePath));
            using FileStream reopened = new(Path.Combine(source, "code.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(contents.Length, reopened.Length);
        }

        [Fact]
        public async Task ClosesSourceOnCancellation()
        {
            byte[] contents = fuzzy.Array(fuzzy.Byte);
            File.WriteAllBytes(Path.Combine(source, "code.bin"), contents);
            using CancellationTokenSource cancellation = new();
            handler.BeforeUpload = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            };

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.UploadApplicationPackageAsync(source,
                false, destination, cancellationToken: cancellation.Token));

            Assert.Empty(handler.Uploads);
            using FileStream reopened = new(Path.Combine(source, "code.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Equal(contents.Length, reopened.Length);
        }
    }

    public sealed class UploadFileAsync : ImageStoreClientTest
    {
        [Fact]
        public async Task PreservesTheByteArrayUploadApi()
        {
            byte[] contents = fuzzy.Array(fuzzy.Byte);

            await sut.UploadFileAsync(contents, destination, cancellationToken: TestContext.Current.CancellationToken);

            var upload = Assert.Single(handler.Uploads);
            Assert.Equal("/ImageStore/" + destination, Uri.UnescapeDataString(upload.Uri.AbsolutePath));
            Assert.Equal(contents, upload.Body);
            Assert.Equal(contents.Length, upload.Length);
        }
    }

    public sealed class UploadFileChunkAsync : ImageStoreClientTest
    {
        [Fact]
        public async Task PreservesExplicitChunkUploads()
        {
            byte[] contents = fuzzy.Array(fuzzy.Byte, Length.Between(1, 100));
            Guid sessionId = Guid.NewGuid();

            await sut.UploadFileChunkAsync(contents, destination, sessionId, 0, contents.Length - 1, contents.Length,
                cancellationToken: TestContext.Current.CancellationToken);

            var upload = Assert.Single(handler.Uploads);
            Assert.EndsWith("/$/UploadChunk", upload.Uri.AbsolutePath);
            Assert.Contains("session-id=" + sessionId, upload.Uri.Query);
            Assert.Equal(contents, upload.Body);
        }
    }

    sealed class UploadHandler : DelegatingHandler
    {
        internal readonly ConcurrentQueue<(Uri Uri, byte[] Body, bool Streamed, long? Length)> Uploads = new();
        internal string ImageStore = "fabric:ImageStore";
        internal Action OnManifest;
        internal Func<CancellationToken, Task> BeforeUpload;
        internal int UploadFailures;
        internal int Commits;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = "{}";
            if (request.RequestUri.AbsolutePath.EndsWith("GetClusterManifest", StringComparison.Ordinal))
            {
                XNamespace schema = "http://schemas.microsoft.com/2011/01/fabric";
                XElement manifest = new(schema + "ClusterManifest", new XElement(schema + "FabricSettings",
                    new XElement(schema + "Section", new XAttribute("Name", "Management"),
                        new XElement(schema + "Parameter", new XAttribute("Name", "ImageStoreConnectionString"),
                            new XAttribute("Value", ImageStore)))));
                body = new JObject { ["Manifest"] = manifest.ToString() }.ToString();
                OnManifest?.Invoke();
            }
            else if (request.Method == HttpMethod.Put)
            {
                if (BeforeUpload != null)
                    await BeforeUpload(cancellationToken);
                long? length = request.Content.Headers.ContentLength;
                Uploads.Enqueue((request.RequestUri, await request.Content.ReadAsByteArrayAsync(), request.Content is StreamContent, length));
                if (Interlocked.Decrement(ref UploadFailures) >= 0)
                    throw new HttpRequestException("Interrupted test upload.");
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("CommitUploadSession", StringComparison.Ordinal))
                Interlocked.Increment(ref Commits);

            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(body) };
        }
    }
}