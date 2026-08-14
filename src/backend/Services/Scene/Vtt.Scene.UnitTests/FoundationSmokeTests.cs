using Vtt.Scene.Domain;

namespace Vtt.Scene.UnitTests;

public sealed class FoundationSmokeTests
{
    [Fact]
    public void DomainAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(DomainAssemblyMarker).Assembly.FullName);
    }
}

