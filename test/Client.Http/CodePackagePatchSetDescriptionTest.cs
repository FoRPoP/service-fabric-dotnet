using System;
using System.Collections.Generic;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchSetDescriptionTest
{
    readonly ServiceName serviceName = $"fabric:/{fuzzy.String().LettersOrDigits()}";
    readonly string codePackageName = fuzzy.String();
    readonly string codePackageVersion = fuzzy.String();
    readonly CodePackagePatchSource source = new ImageStoreCodePackagePatchSource(fuzzy.String());
    readonly PartitionId partitionId = new(Guid.NewGuid());
    readonly string contentName = fuzzy.String();
    readonly IEnumerable<string> nodes = fuzzy.Array(fuzzy.String);
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchSetDescriptionTest
    {
        [Fact]
        public void InitializesProperties()
        {
            CodePackagePatchSetDescription sut = new(serviceName, codePackageName, codePackageVersion, source,
                partitionId, contentName, nodes);

            Assert.Same(serviceName, sut.ServiceName);
            Assert.Equal(codePackageName, sut.CodePackageName);
            Assert.Equal(codePackageVersion, sut.CodePackageVersion);
            Assert.Same(source, sut.Source);
            Assert.Same(partitionId, sut.PartitionId);
            Assert.Equal(contentName, sut.ContentName);
            Assert.Same(nodes, sut.Nodes);
        }

        [Fact]
        public void AllowsOnlyRequiredInputs()
        {
            CodePackagePatchSetDescription sut = new(serviceName, codePackageName, codePackageVersion, source);

            Assert.Null(sut.PartitionId);
            Assert.Null(sut.ContentName);
            Assert.Null(sut.Nodes);
        }

        [Theory]
        [InlineData(nameof(serviceName))]
        [InlineData(nameof(codePackageName))]
        [InlineData(nameof(codePackageVersion))]
        [InlineData(nameof(source))]
        public void RejectsMissingInput(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchSetDescription(
                missing == nameof(serviceName) ? null : serviceName,
                missing == nameof(codePackageName) ? null : codePackageName,
                missing == nameof(codePackageVersion) ? null : codePackageVersion,
                missing == nameof(source) ? null : source));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}