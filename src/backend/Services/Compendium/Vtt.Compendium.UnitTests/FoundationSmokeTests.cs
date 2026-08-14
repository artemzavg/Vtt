using Vtt.Compendium.Domain;

namespace Vtt.Compendium.UnitTests;

public sealed class FoundationSmokeTests
{
    [Fact]
    public void DomainAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(DomainAssemblyMarker).Assembly.FullName);
    }
}

