using System;
using System.IO;

namespace Microsoft.ServiceFabric.Common;

/// <summary>
/// Supplies a complete local code-package directory for automatic upload before setting a patch.
/// </summary>
/// <remarks>
/// The source is read directly without a snapshot. Keep its contents unchanged during upload.
/// </remarks>
public sealed class LocalDirectoryCodePackagePatchSource : CodePackagePatchSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalDirectoryCodePackagePatchSource"/> class.
    /// </summary>
    /// <param name="directoryPath">The caller-local directory containing the complete replacement code package.</param>
    /// <exception cref="ArgumentException">The path is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    public LocalDirectoryCodePackagePatchSource(string directoryPath) : base(CodePackagePatchSourceKind.LocalDirectory)
    {
        if (directoryPath == null)
            throw new ArgumentNullException(nameof(directoryPath));
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("A local code-package directory is required.", nameof(directoryPath));

        DirectoryPath = Path.GetFullPath(directoryPath);
    }

    /// <summary>
    /// Gets the absolute caller-local path. Its contents, not this path, are uploaded to the cluster.
    /// </summary>
    public string DirectoryPath { get; }
}