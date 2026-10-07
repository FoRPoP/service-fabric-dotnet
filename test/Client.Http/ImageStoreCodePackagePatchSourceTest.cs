using System;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class ImageStoreCodePackagePatchSourceTest
{
    readonly string imageStoreRelativePath = new RandomFuzz(Environment.TickCount).String();

    public sealed class Constructor : ImageStoreCodePackagePatchSourceTest
    {
        [Fact]
        public void InitializesProperties()
        {
            ImageStoreCodePackagePatchSource sut = new(imageStoreRelativePath);
            Assert.Equal(imageStoreRelativePath, sut.ImageStoreRelativePath);
            Assert.Equal(CodePackagePatchSourceKind.ImageStore, sut.Kind);
        }

        [Fact]
        public void RejectsNullImageStoreRelativePath()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new ImageStoreCodePackagePatchSource(null));
            Assert.Equal(nameof(imageStoreRelativePath), exception.ParamName);
        }
    }
}