using Vtt.Edge.Domain;

namespace Vtt.Edge.UnitTests;

public sealed class FoundationSmokeTests
{
    [Fact]
    public void DomainAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(DomainAssemblyMarker).Assembly.FullName);
    }
}

