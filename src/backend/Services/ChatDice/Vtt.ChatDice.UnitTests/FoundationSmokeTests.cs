using Vtt.ChatDice.Domain;

namespace Vtt.ChatDice.UnitTests;

public sealed class FoundationSmokeTests
{
    [Fact]
    public void DomainAssemblyCanBeLoaded()
    {
        Assert.NotNull(typeof(DomainAssemblyMarker).Assembly.FullName);
    }
}

