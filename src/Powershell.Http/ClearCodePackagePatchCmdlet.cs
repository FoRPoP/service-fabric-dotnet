using System.Management.Automation;
using Microsoft.ServiceFabric.Common;

namespace Microsoft.ServiceFabric.Powershell.Http;

/// <summary>
/// Removes the current mapping at a version-qualified target without deleting published content.
/// </summary>
[Cmdlet(VerbsCommon.Clear, "SFCodePackagePatch", SupportsShouldProcess = true)]
[OutputType(typeof(CodePackagePatchResult))]
public sealed class ClearCodePackagePatchCmdlet : CommonCmdletBase
{
    /// <summary>
    /// Gets or sets the target returned by Set or Get, including after its original service is deleted.
    /// </summary>
    [Parameter(Mandatory = true, ValueFromPipelineByPropertyName = true)]
    public string PatchId { get; set; }

    /// <summary>
    /// Gets or sets recipient nodes. Removal must eventually reach every accepting or possibly accepting node.
    /// </summary>
    [Parameter]
    public string[] Nodes { get; set; }

    /// <summary>
    /// Gets or sets the submission limit in seconds. The default is 60; expiry does not cancel accepted work.
    /// </summary>
    [Parameter, ValidateRange(1, 4294967295)]
    public long ServerTimeout { get; set; } = 60;

    /// <inheritdoc />
    protected override void ProcessRecordInternal()
    {
        if (!ShouldProcess(PatchId, "Clear code package patch"))
            return;

        CodePackagePatchClearDescription description = new(PatchId, Nodes);
        CodePackagePatchResult result = ServiceFabricClient.CodePackages.ClearCodePackagePatchAsync(description,
            ServerTimeout, CancellationToken).GetAwaiter().GetResult();
        WriteObject(result);
    }
}