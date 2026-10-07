using System;
using System.Collections.Generic;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchClearDescriptionTest
{
    readonly string patchId = fuzzy.String();
    readonly IEnumerable<string> nodes = fuzzy.Array(fuzzy.String);
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchClearDescriptionTest
    {
        [Fact]
        public void InitializesProperties()
        {
            CodePackagePatchClearDescription sut = new(patchId, nodes);
            Assert.Equal(patchId, sut.PatchId);
            Assert.Same(nodes, sut.Nodes);
        }

        [Fact]
        public void AllowsOnlyPatchId()
        {
            CodePackagePatchClearDescription sut = new(patchId);
            Assert.Equal(patchId, sut.PatchId);
            Assert.Null(sut.Nodes);
        }

        [Fact]
        public void RejectsNullPatchId()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchClearDescription(null));
            Assert.Equal(nameof(patchId), exception.ParamName);
        }
    }
}