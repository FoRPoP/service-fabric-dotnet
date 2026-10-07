using System;
using System.Collections.Generic;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchInfoTest
{
    readonly string patchId = fuzzy.String();
    readonly CodePackagePatchTarget target = new(fuzzy.String(), fuzzy.String(), fuzzy.String(), fuzzy.String());
    readonly string revision = fuzzy.UInt64().ToString();
    readonly string contentId = fuzzy.String();
    readonly string imageStoreRelativePath = fuzzy.String();
    readonly IEnumerable<CodePackagePatchProcessInfo> processes = fuzzy.Array(() =>
        new CodePackagePatchProcessInfo(fuzzy.String(), fuzzy.String(), true));
    readonly FabricErrorError lastError = new(FabricErrorCodes.FABRIC_E_TIMEOUT, fuzzy.String());
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchInfoTest
    {
        [Theory, InlineData(null), InlineData(false), InlineData(true)]
        public void InitializesProperties(bool? contentReady)
        {
            CodePackagePatchInfo sut = new(patchId, target, revision, CodePackagePatchState.Active, contentId,
                imageStoreRelativePath, contentReady, CodePackagePatchProgress.Blocked, processes, lastError);

            Assert.Equal(patchId, sut.PatchId);
            Assert.Same(target, sut.Target);
            Assert.Equal(revision, sut.Revision);
            Assert.Equal(CodePackagePatchState.Active, sut.State);
            Assert.Equal(contentId, sut.ContentId);
            Assert.Equal(imageStoreRelativePath, sut.ImageStoreRelativePath);
            Assert.Equal(contentReady, sut.ContentReady);
            Assert.Equal(CodePackagePatchProgress.Blocked, sut.Progress);
            Assert.Same(processes, sut.Processes);
            Assert.Same(lastError, sut.LastError);
        }

        [Fact]
        public void RetainsDifferentVersionAsNonErrorObservation()
        {
            CodePackagePatchInfo sut = new(patchId, target, revision, CodePackagePatchState.Active, contentId,
                progress: CodePackagePatchProgress.DifferentCodePackageVersion);

            Assert.Equal(CodePackagePatchState.Active, sut.State);
            Assert.Equal(CodePackagePatchProgress.DifferentCodePackageVersion, sut.Progress);
            Assert.Null(sut.LastError);
        }

        [Fact]
        public void AllowsClearedMarkerWithoutContent()
        {
            CodePackagePatchInfo sut = new(patchId, target, revision, CodePackagePatchState.Cleared);

            Assert.Equal(CodePackagePatchState.Cleared, sut.State);
            Assert.Equal(revision, sut.Revision);
            Assert.Null(sut.ContentId);
        }

        [Theory]
        [InlineData("patchId")]
        [InlineData("target")]
        [InlineData("revision")]
        [InlineData("state")]
        public void RejectsMissingMappingField(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchInfo(
                missing == nameof(patchId) ? null : patchId, missing == nameof(target) ? null : target,
                missing == nameof(revision) ? null : revision, missing == "state" ? null : CodePackagePatchState.Active));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}