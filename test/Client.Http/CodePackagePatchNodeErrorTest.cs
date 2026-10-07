using System;
using Fuzzy;
using Xunit;

namespace Microsoft.ServiceFabric.Common;

public abstract class CodePackagePatchNodeErrorTest
{
    readonly NodeName nodeName = fuzzy.String();
    readonly FabricErrorError error = new(FabricErrorCodes.FABRIC_E_TIMEOUT, fuzzy.String());
    static readonly IFuzz fuzzy = new RandomFuzz(Environment.TickCount);

    public sealed class Constructor : CodePackagePatchNodeErrorTest
    {
        [Fact]
        public void InitializesProperties()
        {
            CodePackagePatchNodeError sut = new(nodeName, error);
            Assert.Same(nodeName, sut.NodeName);
            Assert.Same(error, sut.Error);
        }

        [Theory, InlineData(nameof(nodeName)), InlineData(nameof(error))]
        public void RejectsMissingErrorField(string missing)
        {
            var exception = Assert.Throws<ArgumentNullException>(() => new CodePackagePatchNodeError(
                missing == nameof(nodeName) ? null : nodeName, missing == nameof(error) ? null : error));
            Assert.Equal(missing, exception.ParamName);
        }
    }
}