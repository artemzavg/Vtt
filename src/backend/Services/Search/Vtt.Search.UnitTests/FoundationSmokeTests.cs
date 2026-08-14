using Vtt.Search.Domain;

namespace Vtt.Search.UnitTests;

public sealed class FoundationSmokeTests
{
    [Fact]
    public void DomainAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(DomainAssemblyMarker).Assembly.FullName);
    }
}

