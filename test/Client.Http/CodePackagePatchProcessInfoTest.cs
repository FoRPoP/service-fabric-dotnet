using System;
using System.Collections.Generic;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchProcessInfoTest
{
    readonly string codePackageInstanceId = fuzzy.String();
    readonly string codePackageVersion = fuzzy.String();
    readonly IEnumerable<Guid?> partitionIds = fuzzy.Array(() => (Guid?)Guid.NewGuid());
    readonly string effectiveCodePath = fuzzy.String();
    readonly string effectiveContentId = fuzzy.String();
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchProcessInfoTest
    {
        [Theory, InlineData(false), InlineData(true)]
        public void InitializesProperties(bool isRunning)
        {
            CodePackagePatchProcessInfo sut = new(codePackageInstanceId, codePackageVersion, isRunning, partitionIds,
                effectiveCodePath, effectiveContentId);

            Assert.Equal(codePackageInstanceId, sut.CodePackageInstanceId);
            Assert.Equal(codePackageVersion, sut.CodePackageVersion);
            Assert.Equal(isRunning, sut.IsRunning);
            Assert.Same(partitionIds, sut.PartitionIds);
            Assert.Equal(effectiveCodePath, sut.EffectiveCodePath);
            Assert.Equal(effectiveContentId, sut.EffectiveContentId);
        }

        [Fact]
        public void AllowsCanonicalCodeWithoutEffectiveContent()
        {
            CodePackagePatchProcessInfo sut = new(codePackageInstanceId, codePackageVersion, true);
            Assert.Null(sut.EffectiveContentId);
        }

        [Theory]
        [InlineData("codePackageInstanceId")]
        [InlineData("codePackageVersion")]
        [InlineData("isRunning")]
        public void RejectsMissingObservation(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchProcessInfo(
                missing == nameof(codePackageInstanceId) ? null : codePackageInstanceId,
                missing == nameof(codePackageVersion) ? null : codePackageVersion,
                missing == "isRunning" ? null : true));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}