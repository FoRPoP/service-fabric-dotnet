using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fuzzy;
using Microsoft.ServiceFabric.Client.Exceptions;
using Microsoft.ServiceFabric.Common;
using Microsoft.ServiceFabric.Common.Exceptions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.ServiceFabric.Client.Http;

public abstract partial class CodePackageClientTest
{
    public sealed class SetCodePackagePatchAsync_LocalDirectory : CodePackageClientTest, IDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        readonly byte[] contents = fuzzy.Array(fuzzy.Byte);
        readonly string name = fuzzy.String().LettersOrDigits() + ".bin";
        readonly string serviceId = fuzzy.String().LettersOrDigits() + "~" + fuzzy.String().LettersOrDigits();
        readonly CodePackagePatchSetDescription description;

        public SetCodePackagePatchAsync_LocalDirectory()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, name), contents);
            description = new CodePackagePatchSetDescription("fabric:/" + serviceId.Replace('~', '/'),
                fuzzy.String(), fuzzy.String(), new LocalDirectoryCodePackagePatchSource(directory), contentName: fuzzy.String().LettersOrDigits());
            handler.UploadMode = true;
            handler.Response["PatchId"] = fuzzy.String();
        }

        void IDisposable.Dispose()
        {
            client.Dispose();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        [Fact]
        public async Task UploadsDirectoryBeforeSubmittingStoredReference()
        {
            Directory.CreateDirectory(Path.Combine(directory, "empty"));

            CodePackagePatchResult actual = await sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken);

            RecordedRequest[] requests = handler.Requests.ToArray();
            RecordedRequest upload = Assert.Single(requests, request => request.Method == HttpMethod.Put && request.Path.EndsWith(name, StringComparison.Ordinal));
            Assert.Equal(contents, upload.Body);
            RecordedRequest[] markers = requests.Where(request => request.Path.EndsWith("/_.dir", StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, markers.Length);
            RecordedRequest set = Assert.Single(requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
            JObject body = JObject.Parse(Encoding.UTF8.GetString(set.Body));
            Assert.Equal("ImageStore", body["Source"]["Kind"]);
            string uploadedPath = (string)body["Source"]["ImageStoreRelativePath"];
            Assert.StartsWith($"Store/{handler.ApplicationTypeName}/HotPatches/{description.ContentName}-", uploadedPath);
            Assert.Equal("/ImageStore/" + uploadedPath + "/" + name, upload.Path);
            Assert.DoesNotContain(directory, Encoding.UTF8.GetString(set.Body));
            Assert.Equal(description.ContentName, body["ContentName"]);
            Assert.Equal("11.11", QueryValue(set.Uri, "api-version"));
            Assert.All(markers, marker => Assert.True(Array.IndexOf(requests, marker) < Array.IndexOf(requests, set)));
            Assert.All(markers, marker => Assert.StartsWith("/ImageStore/" + uploadedPath + "/", marker.Path));
            Assert.DoesNotContain(requests, request => request.Method == HttpMethod.Delete);
            Assert.Equal(uploadedPath, actual.ImageStoreRelativePath);
            Assert.Equal(uploadedPath.Split('/').Last(), actual.ContentId);
            Assert.Equal(contents, File.ReadAllBytes(Path.Combine(directory, name)));
        }

        [Fact]
        public async Task ResolvesTheOwningApplicationBeforeUpload()
        {
            _ = await sut.SetCodePackagePatchAsync(description, cancellationToken: TestContext.Current.CancellationToken);

            RecordedRequest[] requests = handler.Requests.ToArray();
            Assert.Equal("/Services/" + serviceId + "/$/GetApplicationName", requests[0].Path);
            Assert.Equal("/Applications/" + handler.ApplicationId, requests[1].Path);
            Assert.Equal(HttpMethod.Get, requests[0].Method);
            Assert.Equal(HttpMethod.Get, requests[1].Method);
            Assert.Equal("True", QueryValue(requests[1].Uri, "ExcludeApplicationParameters"));
            Assert.All(requests.Where(request => request.Method == HttpMethod.Put),
                request => Assert.True(Array.IndexOf(requests, request) > 1));
        }

        [Fact]
        public async Task ReadsTheOriginalDirectoryAtUploadTime()
        {
            byte[] changed = contents.Concat(fuzzy.Array(fuzzy.Byte)).Concat(new byte[] { 1 }).ToArray();
            handler.BeforeRequest = (request, cancellation) =>
            {
                if (request.RequestUri.AbsolutePath.EndsWith("GetApplicationName", StringComparison.Ordinal))
                    File.WriteAllBytes(Path.Combine(directory, name), changed);
                return Task.CompletedTask;
            };

            _ = await sut.SetCodePackagePatchAsync(description, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(changed, Assert.Single(handler.Requests, request => request.Path.EndsWith(name, StringComparison.Ordinal)).Body);
            Assert.Equal(changed, File.ReadAllBytes(Path.Combine(directory, name)));
        }

        [Fact]
        public async Task GeneratesContentIdWithoutAnOptionalName()
        {
            CodePackagePatchSetDescription unnamed = new(description.ServiceName, description.CodePackageName,
                description.CodePackageVersion, description.Source);

            CodePackagePatchResult result = await sut.SetCodePackagePatchAsync(unnamed,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(Guid.ParseExact(result.ContentId, "N").ToString("N"), result.ContentId);
            Assert.Equal($"Store/{handler.ApplicationTypeName}/HotPatches/{result.ContentId}", result.ImageStoreRelativePath);
        }

        [Fact]
        public async Task GivesSeparatePublicationsWithTheSameNameDifferentPaths()
        {
            CodePackagePatchResult first = await sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken);
            CodePackagePatchResult second = await sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(first.ImageStoreRelativePath, second.ImageStoreRelativePath);
            Assert.NotEqual(first.ContentId, second.ContentId);
            Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Delete);
        }

        [Theory, InlineData("service"), InlineData("application")]
        public async Task DoesNotUploadWhenOwnerLookupFails(string query)
        {
            string path = query == "service" ? "/Services/" + serviceId + "/$/GetApplicationName"
                : "/Applications/" + handler.ApplicationId;
            handler.BeforeRequest = (request, cancellation) => request.RequestUri.AbsolutePath == path
                ? throw new HttpRequestException("Owner lookup failed.") : Task.CompletedTask;

            _ = await Assert.ThrowsAsync<ServiceFabricRequestException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        }

        [Theory, InlineData(null), InlineData("")]
        public async Task RejectsMissingApplicationIdentifierBeforeUpload(string applicationId)
        {
            handler.ApplicationId = applicationId;

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.EndsWith("/GetApplicationName", Assert.Single(handler.Requests).Path);
        }

        [Theory, InlineData(null), InlineData(""), InlineData("../other")]
        public async Task RejectsUnsafeApplicationTypeBeforeUpload(string applicationTypeName)
        {
            handler.ApplicationTypeName = applicationTypeName;

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        }

        [Theory, InlineData(""), InlineData(" "), InlineData("."), InlineData(".."), InlineData("../other"),
            InlineData("other\\path"), InlineData("other:path"), InlineData("bad*"), InlineData("bad?"),
            InlineData("bad\""), InlineData("bad|"), InlineData("bad<"), InlineData("bad>"),
            InlineData("bad\0"), InlineData("bad\n"), InlineData("bad."), InlineData("bad ")]
        public async Task RejectsUnsafeContentNameBeforeNetworkRequests(string contentName)
        {
            CodePackagePatchSetDescription invalid = new(description.ServiceName, description.CodePackageName,
                description.CodePackageVersion, description.Source, contentName: contentName);

            var error = await Assert.ThrowsAsync<ArgumentException>(() => sut.SetCodePackagePatchAsync(invalid,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(nameof(description.ContentName), error.ParamName);
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task RetriesTheSameFileBeforeSet()
        {
            handler.UploadFailuresRemaining = 1;

            _ = await sut.SetCodePackagePatchAsync(description, cancellationToken: TestContext.Current.CancellationToken);

            RecordedRequest[] uploads = handler.Requests.Where(request => request.Path.EndsWith(name, StringComparison.Ordinal)).ToArray();
            Assert.Equal(2, uploads.Length);
            Assert.Equal(uploads[0].Path, uploads[1].Path);
            Assert.Equal(uploads[0].Body, uploads[1].Body);
            Assert.Single(handler.Requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
        }

        [Fact]
        public async Task DoesNotSubmitSetAfterUploadFailure()
        {
            handler.FailUploads = true;

            var error = await Assert.ThrowsAsync<ServiceFabricRequestException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(2, handler.Requests.Count(request => request.Method == HttpMethod.Put));
            Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/_.dir", StringComparison.Ordinal));
            string path = (string)error.Data["CodePackagePatchUploadPath"];
            Assert.StartsWith($"Store/{handler.ApplicationTypeName}/HotPatches/", path);
            Assert.Equal("/ImageStore/" + path, Assert.Single(handler.Requests, request => request.Method == HttpMethod.Delete).Path);
        }

        [Fact]
        public async Task RetainsPublicationWhenSetOutcomeIsUnknown()
        {
            handler.FailSet = true;

            var error = await Assert.ThrowsAsync<ServiceFabricRequestException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            RecordedRequest set = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
            JObject request = JObject.Parse(Encoding.UTF8.GetString(set.Body));
            Assert.Equal((string)request["Source"]["ImageStoreRelativePath"], error.Data["CodePackagePatchUploadPath"]);
            Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Delete);
        }

        [Fact]
        public async Task RetainsPublicationWhenSetIsRejected()
        {
            handler.SetStatusCode = HttpStatusCode.BadRequest;
            handler.Response["Error"] = new JObject { ["Code"] = "E_INVALIDARG", ["Message"] = fuzzy.String() };

            var error = await Assert.ThrowsAsync<ServiceFabricException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal((string)handler.Response["Error"]["Message"], error.Message);
            Assert.Equal((string)handler.Response["ImageStoreRelativePath"], error.Data["CodePackagePatchUploadPath"]);
            Assert.DoesNotContain(handler.Requests, request => request.Method == HttpMethod.Delete);
        }

        [Fact]
        public async Task PreservesUploadFailureWhenCleanupFails()
        {
            handler.FailUploads = true;
            handler.FailCleanup = true;

            var error = await Assert.ThrowsAsync<ServiceFabricRequestException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.StartsWith($"Store/{handler.ApplicationTypeName}/HotPatches/", (string)error.Data["CodePackagePatchUploadPath"]);
            Assert.Single(handler.Requests, request => request.Method == HttpMethod.Delete);
        }

        [Fact]
        public async Task PreservesUploadFailureWhenCleanupCredentialsExpire()
        {
            handler.FailUploads = true;
            handler.RejectCleanupCredentials = true;

            var error = await Assert.ThrowsAsync<ServiceFabricRequestException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.StartsWith($"Store/{handler.ApplicationTypeName}/HotPatches/", (string)error.Data["CodePackagePatchUploadPath"]);
            Assert.Single(handler.Requests, request => request.Method == HttpMethod.Delete);
        }

        [Fact]
        public async Task RejectsMissingDirectoryBeforeNetworkRequests()
        {
            Directory.Delete(directory, true);

            _ = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Empty(handler.Requests);
        }

        [Theory, InlineData("_.dir", false), InlineData("_.DIR", false), InlineData("_.dir", true)]
        public async Task RejectsReservedCompletionMarkerBeforeUpload(string marker, bool folder)
        {
            string path = Path.Combine(directory, marker);
            if (folder)
                Directory.CreateDirectory(path);
            else
                File.WriteAllBytes(path, contents);

            _ = await Assert.ThrowsAsync<ArgumentException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Empty(handler.Requests);
        }

        [Theory, InlineData(false), InlineData(true)]
        public async Task RejectsDirectoryLinksBeforeNetworkRequests(bool root)
        {
            string target = Path.Combine(directory, "target");
            string alias = Path.Combine(directory, "alias");
            Directory.CreateDirectory(target);
            CreateDirectoryLink(alias, target);
            try
            {
                CodePackagePatchSetDescription linked = new(description.ServiceName, description.CodePackageName,
                    description.CodePackageVersion, new LocalDirectoryCodePackagePatchSource(root ? alias : directory));

                _ = await Assert.ThrowsAsync<ArgumentException>(() => sut.SetCodePackagePatchAsync(linked,
                    cancellationToken: TestContext.Current.CancellationToken));

                Assert.Empty(handler.Requests);
            }
            finally
            {
                Directory.Delete(alias);
            }
        }

        [Fact]
        public async Task HonorsCancellationBeforeDirectoryValidation()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: cancellation.Token));

            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task UploadsCompleteLargeFileBeforeSet()
        {
            byte lastByte = fuzzy.Byte().Minimum(1);
            using (FileStream large = new(Path.Combine(directory, "large.bin"), FileMode.CreateNew))
            {
                large.SetLength(25L * 1024 * 1024 + 1);
                large.Position = large.Length - 1;
                large.WriteByte(lastByte);
            }

            _ = await sut.SetCodePackagePatchAsync(description, cancellationToken: TestContext.Current.CancellationToken);

            RecordedRequest[] requests = handler.Requests.ToArray();
            RecordedRequest upload = Assert.Single(requests, request => request.Path.EndsWith("/large.bin", StringComparison.Ordinal));
            Assert.Equal(HttpMethod.Put, upload.Method);
            Assert.Equal(25L * 1024 * 1024 + 1, upload.Body.LongLength);
            Assert.Equal(lastByte, upload.Body[upload.Body.Length - 1]);
            Assert.DoesNotContain(requests, request => request.Path.EndsWith("/UploadChunk", StringComparison.Ordinal));
            Assert.DoesNotContain(requests, request => request.Path.EndsWith("/CommitUploadSession", StringComparison.Ordinal));
            RecordedRequest set = Assert.Single(requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
            Assert.True(Array.IndexOf(requests, upload) < Array.IndexOf(requests, set));
        }

        [Fact]
        public async Task DoesNotCompleteOrSetAfterLargeFileFailure()
        {
            using (FileStream large = new(Path.Combine(directory, "large.bin"), FileMode.CreateNew))
                large.SetLength(25L * 1024 * 1024 + 1);
            handler.FailLargeFiles = true;

            _ = await Assert.ThrowsAsync<ServiceFabricRequestException>(() => sut.SetCodePackagePatchAsync(description,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Contains(handler.Requests, request => request.Path.EndsWith("/large.bin", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/_.dir", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/CommitUploadSession", StringComparison.Ordinal));
            Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
        }

        [Fact]
        public async Task GivesSetOnlyTheBudgetRemainingAfterUpload()
        {
            int calls = 0;
            handler.BeforeUpload = cancellation => Interlocked.Increment(ref calls) == 1
                ? Task.Delay(TimeSpan.FromMilliseconds(1100), cancellation) : Task.CompletedTask;

            _ = await sut.SetCodePackagePatchAsync(description, 30, TestContext.Current.CancellationToken);

            RecordedRequest set = Assert.Single(handler.Requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
            long remaining = long.Parse(QueryValue(set.Uri, "timeout"));
            Assert.InRange(remaining, 1, 29);
        }

        [Fact]
        public async Task GivesUploadOnlyTheBudgetRemainingAfterOwnerLookup()
        {
            handler.BeforeRequest = (request, cancellation) => request.RequestUri.AbsolutePath.EndsWith("GetApplicationName", StringComparison.Ordinal)
                ? Task.Delay(TimeSpan.FromMilliseconds(1100), cancellation) : Task.CompletedTask;

            _ = await sut.SetCodePackagePatchAsync(description, 30, TestContext.Current.CancellationToken);

            RecordedRequest upload = handler.Requests.First(request => request.Method == HttpMethod.Put);
            Assert.InRange(long.Parse(QueryValue(upload.Uri, "timeout")), 1, 29);
        }

        [Fact]
        public async Task DoesNotUploadAfterOwnerLookupExhaustsTheOperationTimeout()
        {
            handler.BeforeRequest = (request, cancellation) => Task.Delay(Timeout.Infinite, cancellation);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.SetCodePackagePatchAsync(description,
                1, TestContext.Current.CancellationToken));

            Assert.EndsWith("/GetApplicationName", Assert.Single(handler.Requests).Path);
        }

        [Fact]
        public async Task DoesNotSetAfterUploadExhaustsTheOperationTimeout()
        {
            handler.BeforeUpload = cancellation => Task.Delay(Timeout.Infinite, cancellation);

            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.SetCodePackagePatchAsync(description,
                1, TestContext.Current.CancellationToken));

            Assert.DoesNotContain(handler.Requests, request => request.Path.EndsWith("/SetCodePackagePatch", StringComparison.Ordinal));
            Assert.StartsWith($"Store/{handler.ApplicationTypeName}/HotPatches/", (string)error.Data["CodePackagePatchUploadPath"]);
        }

        static void CreateDirectoryLink(string alias, string target)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            using Process process = Process.Start(new ProcessStartInfo(windows ? "cmd.exe" : "ln",
                windows ? $"/c mklink /J \"{alias}\" \"{target}\"" : $"-s \"{target}\" \"{alias}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }

        static string QueryValue(Uri uri, string name) => uri.Query.TrimStart('?').Split('&')
            .Select(value => value.Split(['='], 2)).Where(value => value[0] == name)
            .Select(value => Uri.UnescapeDataString(value[1])).Single();
    }
}