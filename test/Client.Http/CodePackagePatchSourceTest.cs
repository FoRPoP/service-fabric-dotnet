using System;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchSourceTest
{
    public sealed class Constructor : CodePackagePatchSourceTest
    {
        [Theory]
        [InlineData(CodePackagePatchSourceKind.ImageStore)]
        [InlineData(CodePackagePatchSourceKind.ProvisionedCodePackage)]
        public void InitializesKind(CodePackagePatchSourceKind kind)
        {
            CodePackagePatchSource sut = new Source(kind);
            Assert.Equal(kind, sut.Kind);
        }

        [Fact]
        public void RejectsNullKind()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new Source(null));
            Assert.Equal("kind", exception.ParamName);
        }
    }

    sealed class Source(CodePackagePatchSourceKind? kind) : CodePackagePatchSource(kind);
}