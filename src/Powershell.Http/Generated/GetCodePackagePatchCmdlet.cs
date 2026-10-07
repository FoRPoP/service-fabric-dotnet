// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.ServiceFabric.Powershell.Http
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using Microsoft.ServiceFabric.Common;

    /// <summary>
    /// Gets node-local code package patch mappings and process observations.
    /// </summary>
    [Cmdlet(VerbsCommon.Get, "SFCodePackagePatch")]
    public partial class GetCodePackagePatchCmdlet : CommonCmdletBase
    {
        /// <summary>
        /// Gets or sets PatchId. Optional stable target identifier returned by Set or Get. Restricts results to that
        /// version-qualified target without requiring a service or package lookup.
        /// </summary>
        [Parameter(Mandatory = false, Position = 0)]
        public string PatchId { get; set; }

        /// <summary>
        /// Gets or sets ServiceName. The name of a service.
        /// </summary>
        [Parameter(Mandatory = false, Position = 1)]
        public string ServiceName { get; set; }

        /// <summary>
        /// Gets or sets CodePackageName. The name of code package specified in service manifest registered as part of an
        /// application type in a Service Fabric cluster.
        /// </summary>
        [Parameter(Mandatory = false, Position = 2)]
        public string CodePackageName { get; set; }

        /// <summary>
        /// Gets or sets CodePackageVersion. Optional declared service-manifest code package version. Can be used independently
        /// or combined with the other exact-match filters.
        /// </summary>
        [Parameter(Mandatory = false, Position = 3)]
        public string CodePackageVersion { get; set; }

        /// <summary>
        /// Gets or sets PartitionId. The identity of the partition.
        /// </summary>
        [Parameter(Mandatory = false, Position = 4)]
        public PartitionId PartitionId { get; set; }

        /// <summary>
        /// Gets or sets Nodes. Optional comma-separated node names in one query parameter, for example N1,N2. Use CSV double
        /// quotes around a name containing a comma or double quote, doubling an embedded double quote. Supply the raw CSV
        /// value to the SDK or PowerShell, which URL-encode it automatically. Raw REST callers must URL-encode the complete
        /// CSV value.
        /// Omission targets all currently-up nodes once for this call. Empty entries are invalid and duplicate names are
        /// removed. Do not repeat the Nodes parameter; the gateway retains only the first occurrence.
        /// </summary>
        [Parameter(Mandatory = false, Position = 5)]
        public string Nodes { get; set; }

        /// <summary>
        /// Gets or sets ServerTimeout. The server timeout for performing the operation in seconds. This timeout specifies the
        /// time duration that the client is willing to wait for the requested operation to complete. The default value for
        /// this parameter is 60 seconds.
        /// </summary>
        [Parameter(Mandatory = false, Position = 6)]
        public long? ServerTimeout { get; set; }

        /// <inheritdoc/>
        protected override void ProcessRecordInternal()
        {
            var result = this.ServiceFabricClient.CodePackages.GetCodePackagePatchListAsync(
                patchId: this.PatchId,
                serviceName: this.ServiceName,
                codePackageName: this.CodePackageName,
                codePackageVersion: this.CodePackageVersion,
                partitionId: this.PartitionId,
                nodes: this.Nodes,
                serverTimeout: this.ServerTimeout,
                cancellationToken: this.CancellationToken).GetAwaiter().GetResult();

            if (result != null)
            {
                this.WriteObject(this.FormatOutput(result));
            }
        }

        /// <inheritdoc/>
        protected override object FormatOutput(object output)
        {
            return output;
        }
    }
}
