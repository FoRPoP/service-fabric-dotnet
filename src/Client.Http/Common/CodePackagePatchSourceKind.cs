namespace Microsoft.ServiceFabric.Common;

/// <summary>
/// Identifies how replacement code is supplied to a patch operation.
/// </summary>
public enum CodePackagePatchSourceKind
{
    /// <summary>
    /// References complete contents in Image Store, including a retained publication.
    /// </summary>
    ImageStore,

    /// <summary>
    /// References a code package in a registered application type.
    /// </summary>
    ProvisionedCodePackage,

    /// <summary>
    /// Uploads a caller-local directory before submitting an Image Store reference. This value is never sent to Set over HTTP.
    /// </summary>
    LocalDirectory,
}