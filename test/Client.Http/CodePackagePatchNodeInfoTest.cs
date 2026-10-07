using System;
using System.Collections.Generic;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchNodeInfoTest
{
    readonly NodeName nodeName = fuzzy.String();
    readonly IEnumerable<CodePackagePatchInfo> patches = fuzzy.Array(() => new CodePackagePatchInfo(fuzzy.String(),
        new CodePackagePatchTarget(fuzzy.String(), fuzzy.String(), fuzzy.String(), fuzzy.String()),
        fuzzy.UInt64().ToString(), CodePackagePatchState.Cleared));
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchNodeInfoTest
    {
        [Fact]
        public void InitializesProperties()
        {
            CodePackagePatchNodeInfo sut = new(nodeName, patches);
            Assert.Same(nodeName, sut.NodeName);
            Assert.Same(patches, sut.Patches);
        }

        [Fact]
        public void AllowsReachableNodeWithNoMappings()
        {
            CodePackagePatchNodeInfo sut = new(nodeName, Array.Empty<CodePackagePatchInfo>());
            Assert.Empty(sut.Patches);
        }

        [Theory, InlineData(nameof(nodeName)), InlineData(nameof(patches))]
        public void RejectsMissingResponseField(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchNodeInfo(
                missing == nameof(nodeName) ? null : nodeName, missing == nameof(patches) ? null : patches));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}