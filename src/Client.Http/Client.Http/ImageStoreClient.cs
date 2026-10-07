// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Client.Exceptions;
using Microsoft.ServiceFabric.Common;
using Microsoft.ServiceFabric.Common.Utilities;

namespace Microsoft.ServiceFabric.Client.Http
{
    sealed partial class ImageStoreClient : IImageStoreClient
    {
        const int MaxConcurrentUpload = 10;
        const int MaxUploadTry = 2;
        const string ZipExtension = "zip";

        string imageStorePath;
        bool isLocalStore = false;

        Task IImageStoreClient.UploadFileAsync(byte[] fileContentsToUpload, string pathInImageStore, long? serverTimeout, CancellationToken cancellationToken) =>
            UploadFileAsync(fileContentsToUpload, pathInImageStore, null, serverTimeout, cancellationToken);

        Task IImageStoreClient.UploadFileChunkAsync(byte[] fileChunkToUpload, string pathInImageStore, Guid? sessionId, long startBytePosition, long endBytePosition, long length, long? serverTimeout, CancellationToken cancellationToken) =>
            UploadFileChunkAsync(fileChunkToUpload, pathInImageStore, sessionId, startBytePosition, endBytePosition, length, null, serverTimeout, cancellationToken);

        async Task IImageStoreClient.UploadApplicationPackageAsync(string applicationPackagePath, bool compressPackage, string applicationPackagePathInImageStore, long? serverTimeout, CancellationToken cancellationToken)
        {
            applicationPackagePath.ThrowIfNull(nameof(applicationPackagePath));

            if (!Directory.Exists(applicationPackagePath))
                throw new InvalidOperationException($"Application package path {applicationPackagePath} not found.");

            string absPkgPath = Path.GetFullPath(applicationPackagePath);

            if (compressPackage)
                await CompressApplicationPackage(absPkgPath);

            string pkgPathInImageStore = applicationPackagePathInImageStore;
            if (string.IsNullOrEmpty(pkgPathInImageStore))
                pkgPathInImageStore = new DirectoryInfo(absPkgPath).Name;

            pkgPathInImageStore = pkgPathInImageStore.Trim('\\', '/');

            await LoadImageStoreConnectionString();
            if (isLocalStore)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string targetRoot = Path.Combine(imageStorePath, pkgPathInImageStore);
                Directory.CreateDirectory(targetRoot);
                foreach (string directory in Directory.EnumerateDirectories(absPkgPath, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(Path.Combine(targetRoot, GetRelativePath(absPkgPath, directory)));
                }

                IEnumerable<System.IO.FileInfo> files = Directory.EnumerateFiles(absPkgPath, "*", SearchOption.AllDirectories)
                    .Select(file => new System.IO.FileInfo(file));

                foreach (System.IO.FileInfo file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string targetPath = Path.Combine(targetRoot, GetRelativePath(absPkgPath, file.FullName));
                    if (!Directory.Exists(Path.GetDirectoryName(targetPath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    }

                    File.Copy(file.FullName, targetPath, true);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                var requestId = Guid.NewGuid();
                ServiceFabricHttpClientEventSource.Current.InfoMessage(
                    $"{httpClient.ClientId}:{requestId}",
                    "Processing call for ApplicationClient.DeleteApplicationAsync");

                // list of Files to upload.
                IEnumerable<System.IO.FileInfo> files = Directory.EnumerateFiles(absPkgPath, "*", SearchOption.AllDirectories)
                    .Select(file => new System.IO.FileInfo(file));

                // List of dirs to determine where to upload _.dir fileInfo.
                var dirPathsInImageStore = new List<string> { GetPathInImageStore(absPkgPath, pkgPathInImageStore, absPkgPath) };

                dirPathsInImageStore.AddRange(
                    Directory.EnumerateDirectories(absPkgPath, "*", SearchOption.AllDirectories)
                        .Select(dir => GetPathInImageStore(absPkgPath, pkgPathInImageStore, dir)));

                IEnumerable<FileUploadInfo> singleFileUploadInfos = files
                    .Select(file => new FileUploadInfo(
                        file,
                        GetPathInImageStore(absPkgPath, pkgPathInImageStore, file.FullName)));

                await UploadAllSingleFiles(singleFileUploadInfos, requestId, serverTimeout, cancellationToken);

                // upload _.dirs with up to MaxConcurrentUpload in parallel. 
                await UploadDirectoryCompletionMarkerFiles(dirPathsInImageStore, requestId, serverTimeout, cancellationToken);
            }
        }

        async Task UploadFileAsync(byte[] fileContentsToUpload, string pathInImageStore, string requestId, long? serverTimeout, CancellationToken cancellationToken)
        {
            await LoadImageStoreConnectionString();
            if (isLocalStore)
            {
                File.WriteAllBytes(Path.Combine(imageStorePath, pathInImageStore), fileContentsToUpload);
            }
            else
            {
                fileContentsToUpload.ThrowIfNull(nameof(fileContentsToUpload));
                await UploadFileContent(() => new ByteArrayContent(fileContentsToUpload), pathInImageStore,
                    requestId, serverTimeout, cancellationToken).ConfigureAwait(false);
            }
        }

        async Task UploadFileContent(Func<HttpContent> content, string pathInImageStore, string requestId,
            long? serverTimeout, CancellationToken cancellation)
        {
            pathInImageStore.ThrowIfNull(nameof(pathInImageStore));
            serverTimeout?.ThrowIfOutOfInclusiveRange("serverTimeout", 1, 4294967295);
            string url = "ImageStore/" + Uri.EscapeDataString(pathInImageStore);
            requestId = requestId ?? Guid.NewGuid().ToString();
            var queryParams = new List<string>();
            serverTimeout?.AddToQueryParameters(queryParams, $"timeout={serverTimeout}");
            queryParams.Add("api-version=6.0");
            url += "?" + string.Join("&", queryParams);

            HttpRequestMessage request = null;
            HttpRequestMessage RequestFunc()
            {
                request?.Dispose();
                request = new HttpRequestMessage
                {
                    Method = HttpMethod.Put,
                    Content = content()
                };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                return request;
            }

            try
            {
                await httpClient.SendAsync(RequestFunc, url, requestId, cancellation).ConfigureAwait(false);
            }
            finally
            {
                request?.Dispose();
            }
        }

        Task UploadFileChunkAsync(byte[] fileChunkToUpload, string pathInImageStore, Guid? sessionId, long startBytePosition, long endBytePosition, long length, string requestId, long? serverTimeout, CancellationToken cancellationToken)
        {
            fileChunkToUpload.ThrowIfNull(nameof(fileChunkToUpload));
            pathInImageStore.ThrowIfNull(nameof(pathInImageStore));
            sessionId.ThrowIfNull(nameof(sessionId));
            startBytePosition.ThrowIfNull(nameof(startBytePosition));
            endBytePosition.ThrowIfNull(nameof(endBytePosition));
            length.ThrowIfNull(nameof(length));

            serverTimeout?.ThrowIfOutOfInclusiveRange("serverTimeout", 1, 4294967295);
            requestId = requestId ?? Guid.NewGuid().ToString();
            string url = "ImageStore/{pathInImageStore}/$/UploadChunk";
            url = url.Replace("{pathInImageStore}", Uri.EscapeDataString(pathInImageStore.ToString()));
            var queryParams = new List<string>();

            // Append to queryParams if not null.
            sessionId?.AddToQueryParameters(queryParams, $"session-id={sessionId}");
            serverTimeout?.AddToQueryParameters(queryParams, $"timeout={serverTimeout}");
            queryParams.Add("api-version=6.0");
            url += "?" + string.Join("&", queryParams);

            HttpRequestMessage RequestFunc()
            {
                var request = new HttpRequestMessage()
                {
                    Method = HttpMethod.Put,
                    Content = new ByteArrayContent(fileChunkToUpload),
                };

                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");
                request.Content.Headers.ContentRange = new ContentRangeHeaderValue(startBytePosition, endBytePosition, length);
                return request;
            }

            return httpClient.SendAsync(RequestFunc, url, requestId, cancellationToken);
        }

        static Task CompressApplicationPackage(string appPkgPath)
        {
            var dirsToCompress = new List<string>();

            // Get the service packages in application package
            foreach (DirectoryInfo servicePackage in new DirectoryInfo(appPkgPath).GetDirectories())
                // Get Code/Config/Data packages for each service package.
                dirsToCompress.AddRange(servicePackage.GetDirectories().Select(package => package.FullName));

            return Task.WhenAll(dirsToCompress.Select(dir => Task.Run(() => CompressDirectory(dir, $"{dir}.{ZipExtension}", true))).ToArray());
        }

        static void CompressDirectory(string sourceDirToCompress, string destCompressedFile, bool deleteSourceDirAfterCompression)
        {
            if (File.Exists(destCompressedFile))
                File.Delete(destCompressedFile);

            ZipFile.CreateFromDirectory(sourceDirToCompress, destCompressedFile);

            if (deleteSourceDirAfterCompression)
                Directory.Delete(sourceDirToCompress, true);
        }

        internal static string GetPathInImageStore(string compressedPkgPath, string pkgPathInImageStore, string pathInCompressedPackage)
        {
            string relativePath = GetRelativePath(compressedPkgPath, pathInCompressedPackage).Replace(Path.DirectorySeparatorChar, '/');

            return relativePath.Equals(string.Empty)
                ? pkgPathInImageStore
                : $"{pkgPathInImageStore}/{relativePath}";
        }

        static string GetRelativePath(string root, string path)
        {
            string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedPath = Path.GetFullPath(path);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(normalizedRoot, comparison))
                return string.Empty;

            string prefix = normalizedRoot + Path.DirectorySeparatorChar;
            if (!normalizedPath.StartsWith(prefix, comparison))
                throw new ArgumentException("The upload path must be inside its source directory.", nameof(path));
            return normalizedPath.Substring(prefix.Length);
        }

        Task UploadAllSingleFiles(IEnumerable<FileUploadInfo> singleFileUploadInfos, Guid requestId, long? serverTimeout, CancellationToken cancellationToken)
        {
            var singleFileUploadInfosBag = new ConcurrentBag<FileUploadInfo>(singleFileUploadInfos);
            var concurrentOperationsRunner = new ConcurrentOperationsRunner<FileUploadInfo>(
                uploadInfo =>
                    UploadSingleFile(
                        () => new StreamContent(new FileStream(uploadInfo.FileInfo.FullName, FileMode.Open,
                            FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)),
                        uploadInfo.FilePathInImageStore,
                        requestId.ToString(),
                        serverTimeout,
                        cancellationToken),
                singleFileUploadInfosBag.TryTake,
                MaxConcurrentUpload);

            return concurrentOperationsRunner.RunAll();
        }

        Task UploadDirectoryCompletionMarkerFiles(IEnumerable<string> dirPathsInImageStore, Guid requestId, long? serverTimeout, CancellationToken cancellationToken)
        {
            var dirPaths = new ConcurrentBag<string>(dirPathsInImageStore);
            var concurrentOperationsRunner = new ConcurrentOperationsRunner<string>(
                dirPathInImageStore =>
                    UploadSingleFile(
                        () => new ByteArrayContent(Array.Empty<byte>()),
                        $"{dirPathInImageStore}{Path.DirectorySeparatorChar}_.dir",
                        requestId.ToString(),
                        serverTimeout,
                        cancellationToken),
                dirPaths.TryTake,
                MaxConcurrentUpload);

            return concurrentOperationsRunner.RunAll();
        }

        async Task UploadSingleFile(Func<HttpContent> content, string filePathInImageStore, string parentRequestId, long? serverTimeout, CancellationToken cancellationToken)
        {
            for (int tryCount = 1; ; tryCount++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UploadFileContent(
                        content,
                        filePathInImageStore,
                        $"{parentRequestId}:{Guid.NewGuid()}",
                        serverTimeout,
                        cancellationToken);
                    return;
                }
                catch (ServiceFabricRequestException) when (tryCount < MaxUploadTry)
                {
                }
            }
        }

        async Task LoadImageStoreConnectionString()
        {
            if (imageStorePath == null)
            {
                imageStorePath = await httpClient.Cluster.GetImageStoreConnectionStringAsync();
                if (imageStorePath != "fabric:ImageStore" && !imageStorePath.StartsWith("xstore"))
                {
                    imageStorePath = new Uri(imageStorePath).LocalPath;
                    isLocalStore = true;
                }
            }
        }

        class FileUploadInfo
        {
            internal FileUploadInfo(System.IO.FileInfo fileInfo, string filePathInImageStore)
            {
                FileInfo = fileInfo;
                FilePathInImageStore = filePathInImageStore;
            }

            internal System.IO.FileInfo FileInfo { get; }
            internal string FilePathInImageStore { get; }
        }
    }
}
