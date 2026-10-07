// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Microsoft.ServiceFabric.Common
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Removes the current mapping at the version-qualified target identified by PatchId on the selected nodes without
    /// deleting the shared Image Store package.
    /// Once a node accepts removal, Hosting completes the remaining process work independently of the caller.
    /// Clear is available when EnableHotPatching is disabled and does not require replacement code to become healthy.
    /// Service Fabric decodes the target from the identifier and prepares revision checks internally, including when no
    /// mapping exists. No current service lookup or caller-supplied target is required.
    /// A fresh Clear removes the current selection. Internal retries preserve the original revision checks and cannot
    /// remove a later replacement. Mappings for other code package versions are not removed.
    /// </summary>
    public partial class CodePackagePatchClearDescription
    {
        /// <summary>
        /// Initializes a new instance of the CodePackagePatchClearDescription class.
        /// </summary>
        /// <param name="patchId">The stable identifier of a resolved application instance, service package, code package name
        /// and declared version, and package or partition scope.
        /// Service Fabric returns a versioned string that identifies the same target across nodes and repeated Set operations.
        /// Content and revision are not part of this identifier.
        /// Pass the returned value to Clear without reconstructing the target. Service Fabric can decode it even when a node
        /// has no saved mapping or the original service no longer exists.
        /// </param>
        /// <param name="nodes">Nodes to target. Omission resolves all currently-up nodes once, which may differ from the
        /// original Set recipients.
        /// An explicitly empty list is invalid. Duplicate names are removed. Clear must eventually reach every accepting or
        /// possibly accepting node.
        /// </param>
        public CodePackagePatchClearDescription(
            string patchId,
            IEnumerable<string> nodes = default(IEnumerable<string>))
        {
            patchId.ThrowIfNull(nameof(patchId));
            this.PatchId = patchId;
            this.Nodes = nodes;
        }

        /// <summary>
        /// Gets the stable identifier of a resolved application instance, service package, code package name and declared
        /// version, and package or partition scope.
        /// Service Fabric returns a versioned string that identifies the same target across nodes and repeated Set operations.
        /// Content and revision are not part of this identifier.
        /// Pass the returned value to Clear without reconstructing the target. Service Fabric can decode it even when a node
        /// has no saved mapping or the original service no longer exists.
        /// </summary>
        public string PatchId { get; }

        /// <summary>
        /// Gets nodes to target. Omission resolves all currently-up nodes once, which may differ from the original Set
        /// recipients.
        /// An explicitly empty list is invalid. Duplicate names are removed. Clear must eventually reach every accepting or
        /// possibly accepting node.
        /// </summary>
        public IEnumerable<string> Nodes { get; }
    }
}
