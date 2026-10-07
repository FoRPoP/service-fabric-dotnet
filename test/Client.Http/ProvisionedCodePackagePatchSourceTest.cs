using System;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class ProvisionedCodePackagePatchSourceTest
{
    readonly string applicationTypeName = fuzzy.String();
    readonly string applicationTypeVersion = fuzzy.String();
    readonly string serviceManifestName = fuzzy.String();
    readonly string codePackageName = fuzzy.String();
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : ProvisionedCodePackagePatchSourceTest
    {
        [Fact]
        public void InitializesProperties()
        {
            ProvisionedCodePackagePatchSource sut = new(applicationTypeName, applicationTypeVersion, serviceManifestName,
                codePackageName);

            Assert.Equal(applicationTypeName, sut.ApplicationTypeName);
            Assert.Equal(applicationTypeVersion, sut.ApplicationTypeVersion);
            Assert.Equal(serviceManifestName, sut.ServiceManifestName);
            Assert.Equal(codePackageName, sut.CodePackageName);
            Assert.Equal(CodePackagePatchSourceKind.ProvisionedCodePackage, sut.Kind);
        }

        [Theory]
        [InlineData(nameof(applicationTypeName))]
        [InlineData(nameof(applicationTypeVersion))]
        [InlineData(nameof(serviceManifestName))]
        [InlineData(nameof(codePackageName))]
        public void RejectsMissingSourceField(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new ProvisionedCodePackagePatchSource(
                missing == nameof(applicationTypeName) ? null : applicationTypeName,
                missing == nameof(applicationTypeVersion) ? null : applicationTypeVersion,
                missing == nameof(serviceManifestName) ? null : serviceManifestName,
                missing == nameof(codePackageName) ? null : codePackageName));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}