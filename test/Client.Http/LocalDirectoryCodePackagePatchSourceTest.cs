using System;
using System.IO;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class LocalDirectoryCodePackagePatchSourceTest
{
    readonly string directoryPath = new RandomFuzz(Environment.TickCount).String().LettersOrDigits();

    public sealed class Constructor : LocalDirectoryCodePackagePatchSourceTest
    {
        [Fact]
        public void ResolvesDirectoryPathWithoutUploading()
        {
            LocalDirectoryCodePackagePatchSource sut = new(directoryPath);

            Assert.Equal(Path.GetFullPath(directoryPath), sut.DirectoryPath);
            Assert.Equal(CodePackagePatchSourceKind.LocalDirectory, sut.Kind);
        }

        [Fact]
        public void RejectsNullDirectoryPath()
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new LocalDirectoryCodePackagePatchSource(null));
            Assert.Equal(nameof(directoryPath), exception.ParamName);
        }

        [Theory, InlineData(""), InlineData(" ")]
        public void RejectsEmptyDirectoryPath(string directoryPath)
        {
            var exception = Assert.Throws<ArgumentException>(() => new LocalDirectoryCodePackagePatchSource(directoryPath));
            Assert.Equal(nameof(directoryPath), exception.ParamName);
        }
    }
}