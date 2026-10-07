using System.Management.Automation;
using Microsoft.ServiceFabric.Common;

namespace Microsoft.ServiceFabric.Powershell.Http;

/// <summary>
/// Publishes replacement code and accepts a version-qualified patch mapping on selected nodes.
/// </summary>
[Cmdlet(VerbsCommon.Set, "SFCodePackagePatch", SupportsShouldProcess = true)]
[OutputType(typeof(CodePackagePatchResult))]
public sealed class SetCodePackagePatchCmdlet : CommonCmdletBase
{
    /// <summary>
    /// Gets or sets the service used to identify the target application instance and package.
    /// </summary>
    [Parameter(Mandatory = true)]
    public ServiceName ServiceName { get; set; }

    /// <summary>
    /// Gets or sets the original code package name, independently of the replacement source.
    /// </summary>
    [Parameter(Mandatory = true)]
    public string CodePackageName { get; set; }

    /// <summary>
    /// Gets or sets the declared service-manifest code package version, not an application or rollout version.
    /// </summary>
    [Parameter(Mandatory = true)]
    public string CodePackageVersion { get; set; }

    /// <summary>
    /// Gets or sets the typed replacement source.
    /// Local directories upload directly to retained HotPatches content and must stay unchanged during upload.
    /// Stored and provisioned sources are passed by reference.
    /// </summary>
    [Parameter(Mandatory = true), ValidateNotNull]
    public CodePackagePatchSource Source { get; set; }

    /// <summary>
    /// Gets or sets one exclusive-process partition to target. Omit for package-wide scope.
    /// </summary>
    [Parameter]
    public PartitionId PartitionId { get; set; }

    /// <summary>
    /// Gets or sets the prefix for a new publication's identifier without renaming a reused publication.
    /// </summary>
    [Parameter]
    public string ContentName { get; set; }

    /// <summary>
    /// Gets or sets recipient nodes. Omit to target the nodes currently up; an empty list is invalid.
    /// </summary>
    [Parameter]
    public string[] Nodes { get; set; }

    /// <summary>
    /// Gets or sets the submission limit in seconds. The default is 1800; expiry does not cancel accepted work.
    /// </summary>
    [Parameter, ValidateRange(1, 4294967295)]
    public long ServerTimeout { get; set; } = 1800;

    /// <inheritdoc />
    protected override void ProcessRecordInternal()
    {
        if (!ShouldProcess($"{ServiceName}/{CodePackageName}:{CodePackageVersion}", "Set code package patch"))
            return;

        CodePackagePatchSetDescription description = new(ServiceName, CodePackageName, CodePackageVersion, Source,
            PartitionId, ContentName, Nodes);
        CodePackagePatchResult result = ServiceFabricClient.CodePackages.SetCodePackagePatchAsync(description,
            ServerTimeout, CancellationToken).GetAwaiter().GetResult();
        WriteObject(result);
    }
}