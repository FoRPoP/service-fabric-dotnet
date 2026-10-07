using System;
using System.Collections.Generic;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchResultTest
{
    readonly IEnumerable<CodePackagePatchNodeInfo> successfulNodes = fuzzy.Array(() =>
        new CodePackagePatchNodeInfo(fuzzy.String(), Array.Empty<CodePackagePatchInfo>()));
    readonly IEnumerable<CodePackagePatchNodeError> failedNodes = fuzzy.Array(() => new CodePackagePatchNodeError(
        fuzzy.String(), new FabricErrorError(FabricErrorCodes.E_INVALIDARG, fuzzy.String())));
    readonly IEnumerable<CodePackagePatchNodeError> unresolvedNodes = fuzzy.Array(() => new CodePackagePatchNodeError(
        fuzzy.String(), new FabricErrorError(FabricErrorCodes.FABRIC_E_TIMEOUT, fuzzy.String())));
    readonly string patchId = fuzzy.String();
    readonly string contentId = fuzzy.String();
    readonly string imageStoreRelativePath = fuzzy.String();
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchResultTest
    {
        [Fact]
        public void InitializesProperties()
        {
            CodePackagePatchResult sut = new(successfulNodes, failedNodes, unresolvedNodes, patchId, contentId,
                imageStoreRelativePath);

            Assert.Same(successfulNodes, sut.SuccessfulNodes);
            Assert.Same(failedNodes, sut.FailedNodes);
            Assert.Same(unresolvedNodes, sut.UnresolvedNodes);
            Assert.Equal(patchId, sut.PatchId);
            Assert.Equal(contentId, sut.ContentId);
            Assert.Equal(imageStoreRelativePath, sut.ImageStoreRelativePath);
        }

        [Fact]
        public void AllowsQueryWithoutRootPublication()
        {
            CodePackagePatchResult sut = new(successfulNodes, failedNodes, unresolvedNodes);
            Assert.Null(sut.PatchId);
            Assert.Null(sut.ContentId);
            Assert.Null(sut.ImageStoreRelativePath);
        }

        [Theory]
        [InlineData(nameof(successfulNodes))]
        [InlineData(nameof(failedNodes))]
        [InlineData(nameof(unresolvedNodes))]
        public void RejectsMissingOutcomeCollection(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchResult(
                missing == nameof(successfulNodes) ? null : successfulNodes,
                missing == nameof(failedNodes) ? null : failedNodes,
                missing == nameof(unresolvedNodes) ? null : unresolvedNodes));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}