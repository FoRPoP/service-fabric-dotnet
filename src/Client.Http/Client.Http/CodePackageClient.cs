using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceFabric.Client.Exceptions;
using Microsoft.ServiceFabric.Common;
using Microsoft.ServiceFabric.Common.Exceptions;

namespace Microsoft.ServiceFabric.Client.Http;

sealed partial class CodePackageClient : ICodePackageClient
{
    Task<CodePackagePatchResult> ICodePackageClient.SetCodePackagePatchAsync(CodePackagePatchSetDescription codePackagePatchSetDescription,
        long? serverTimeout, CancellationToken cancellationToken) =>
        httpClient.RunWithTimeout(serverTimeout ?? 1800, cancellation =>
            SetPreparedPatch(codePackagePatchSetDescription, serverTimeout ?? 1800, cancellation), cancellationToken);

    Task<CodePackagePatchResult> ICodePackageClient.ClearCodePackagePatchAsync(CodePackagePatchClearDescription codePackagePatchClearDescription,
        long? serverTimeout, CancellationToken cancellationToken) =>
        httpClient.RunWithTimeout(serverTimeout ?? 60, cancellation =>
            ClearCodePackagePatchAsync(codePackagePatchClearDescription, serverTimeout ?? 60, cancellation), cancellationToken);

    Task<CodePackagePatchResult> ICodePackageClient.GetCodePackagePatchListAsync(string patchId, string serviceName,
        string codePackageName, string codePackageVersion, PartitionId partitionId, string nodes, long? serverTimeout,
        CancellationToken cancellationToken) =>
        httpClient.RunWithTimeout(serverTimeout ?? 60, cancellation =>
            GetCodePackagePatchListAsync(EncodeQueryValue(patchId), EncodeQueryValue(serviceName), EncodeQueryValue(codePackageName),
                EncodeQueryValue(codePackageVersion), partitionId, EncodeQueryValue(nodes), serverTimeout ?? 60, cancellation), cancellationToken);

    static string EncodeQueryValue(string value) => value == null ? null : Uri.EscapeDataString(value);

    async Task<CodePackagePatchResult> SetPreparedPatch(CodePackagePatchSetDescription codePackagePatchSetDescription, long timeout,
        CancellationToken cancellationToken)
    {
        CodePackagePatchSetDescription description = codePackagePatchSetDescription ?? throw new ArgumentNullException(nameof(codePackagePatchSetDescription));
        if (description.Source is not LocalDirectoryCodePackagePatchSource local)
            return await SetCodePackagePatchAsync(description, timeout, cancellationToken).ConfigureAwait(false);

        ValidateUploadDirectory(new DirectoryInfo(local.DirectoryPath), cancellationToken);
        if (description.ContentName != null && !IsPathSegment(description.ContentName))
            throw new ArgumentException("ContentName must be a nonempty, safe Image Store path segment.", nameof(description.ContentName));

        ApplicationNameInfo applicationName = await httpClient.Services.GetApplicationNameInfoAsync(
            description.ServiceName.GetId().Replace("/", "~"), timeout, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(applicationName?.Id))
            throw new InvalidOperationException("The service query did not return an application identifier.");

        ApplicationInfo application = await httpClient.Applications.GetApplicationInfoAsync(
            Uri.EscapeDataString(applicationName.Id), true, timeout, cancellationToken).ConfigureAwait(false);
        if (!IsPathSegment(application?.TypeName))
            throw new InvalidOperationException("The application query did not return a safe application type name.");

        string contentId = Guid.NewGuid().ToString("N");
        if (description.ContentName != null)
            contentId = description.ContentName + "-" + contentId;
        string uploadedPath = $"Store/{application.TypeName}/HotPatches/{contentId}";
        bool uploaded = false;
        try
        {
            await httpClient.ImageStore.UploadApplicationPackageAsync(local.DirectoryPath, false, uploadedPath, timeout,
                cancellationToken).ConfigureAwait(false);
            uploaded = true;
            cancellationToken.ThrowIfCancellationRequested();
            CodePackagePatchSetDescription prepared = new(description.ServiceName, description.CodePackageName,
                description.CodePackageVersion, new ImageStoreCodePackagePatchSource(uploadedPath), description.PartitionId,
                description.ContentName, description.Nodes);
            return await SetCodePackagePatchAsync(prepared, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            error.Data["CodePackagePatchUploadPath"] = uploadedPath;
            if (!uploaded)
                await RemoveIncompletePublication(uploadedPath, timeout, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    static bool IsPathSegment(string value) => !string.IsNullOrWhiteSpace(value) &&
        !value.EndsWith(".", StringComparison.Ordinal) && !value.EndsWith(" ", StringComparison.Ordinal) &&
        !value.Any(character => char.IsControl(character) || "<>:\"/\\|?*".IndexOf(character) >= 0);

    static void ValidateUploadDirectory(DirectoryInfo directory, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!directory.Exists)
            throw new DirectoryNotFoundException($"The code package directory does not exist: {directory.FullName}");
        RejectLink(directory);
        foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
        {
            cancellation.ThrowIfCancellationRequested();
            if (entry.Name.Equals("_.dir", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The code package contains the reserved Image Store completion marker _.dir.");
            if (entry is DirectoryInfo child)
                ValidateUploadDirectory(child, cancellation);
            else
                RejectLink(entry);
        }
    }

    static void RejectLink(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException($"Code package uploads do not follow filesystem links: {entry.FullName}");
    }

    async Task RemoveIncompletePublication(string path, long timeout, CancellationToken cancellationToken)
    {
        try
        {
            await httpClient.ImageStore.DeleteImageStoreContentAsync(path, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException || error is TimeoutException ||
            error is ServiceFabricException || error is ServiceFabricRequestException || error is InvalidCredentialsException)
        {
            ServiceFabricHttpClientEventSource.Current.WarningMessage(httpClient.ClientId,
                $"Incomplete patch publication {path} could not be removed: {error.Message}");
        }
    }
}