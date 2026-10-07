using System;
using Fuzzy;
using Microsoft.ServiceFabric.Common;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchTargetTest
{
    readonly string applicationInstanceId = fuzzy.String();
    readonly string serviceManifestName = fuzzy.String();
    readonly string codePackageName = fuzzy.String();
    readonly string codePackageVersion = fuzzy.String();
    readonly PartitionId partitionId = new(Guid.NewGuid());
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchTargetTest
    {
        [Fact]
        public void InitializesProperties()
        {
            CodePackagePatchTarget sut = new(applicationInstanceId, serviceManifestName, codePackageName,
                codePackageVersion, partitionId);

            Assert.Equal(applicationInstanceId, sut.ApplicationInstanceId);
            Assert.Equal(serviceManifestName, sut.ServiceManifestName);
            Assert.Equal(codePackageName, sut.CodePackageName);
            Assert.Equal(codePackageVersion, sut.CodePackageVersion);
            Assert.Same(partitionId, sut.PartitionId);
        }

        [Fact]
        public void AllowsPackageScope()
        {
            CodePackagePatchTarget sut = new(applicationInstanceId, serviceManifestName, codePackageName, codePackageVersion);
            Assert.Null(sut.PartitionId);
        }

        [Theory]
        [InlineData("applicationInstanceId")]
        [InlineData("serviceManifestName")]
        [InlineData("codePackageName")]
        [InlineData("codePackageVersion")]
        public void RejectsMissingTargetField(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchTarget(
                missing == nameof(applicationInstanceId) ? null : applicationInstanceId,
                missing == nameof(serviceManifestName) ? null : serviceManifestName,
                missing == nameof(codePackageName) ? null : codePackageName,
                missing == nameof(codePackageVersion) ? null : codePackageVersion));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}