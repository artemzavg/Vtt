using System.Xml.Linq;

namespace Vtt.ArchitectureTests;

public sealed class ProjectDependencyRulesTests
{
    private static readonly string[] ExpectedContexts =
    [
        "Campaign",
        "Character",
        "ChatDice",
        "Compendium",
        "Edge",
        "Gameplay",
        "Identity",
        "Media",
        "Ruleset",
        "Scene",
        "Search",
        "Session",
    ];

    [Fact]
    public void EveryBoundedContextHasTheFoundationProjectSet()
    {
        var repositoryRoot = FindRepositoryRoot();
        var servicesRoot = Path.Combine(repositoryRoot, "src", "backend", "Services");
        var expectedLayers = new[]
        {
            "Api",
            "Application",
            "Contracts",
            "Domain",
            "Infrastructure",
            "IntegrationTests",
            "UnitTests",
        };
        var missing = new List<string>();

        foreach (var context in ExpectedContexts)
        {
            foreach (var layer in expectedLayers)
            {
                var project = Path.Combine(
                    servicesRoot,
                    context,
                    $"Vtt.{context}.{layer}",
                    $"Vtt.{context}.{layer}.csproj");

                if (!File.Exists(project))
                {
                    missing.Add(Path.GetRelativePath(repositoryRoot, project));
                }
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void ProductionProjectsRespectLayerAndServiceBoundaries()
    {
        var repositoryRoot = FindRepositoryRoot();
        var servicesRoot = Path.Combine(repositoryRoot, "src", "backend", "Services");
        var violations = new List<string>();

        foreach (var project in Directory.EnumerateFiles(
                     servicesRoot,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(project);
            if (projectName.EndsWith("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            var context = Directory.GetParent(Path.GetDirectoryName(project)!)!.Name;
            var layer = projectName[(projectName.LastIndexOf('.') + 1)..];
            var document = XDocument.Load(project);
            var references = document
                .Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => ResolveProjectReference(project, include!));

            foreach (var reference in references)
            {
                if (!reference.StartsWith(servicesRoot, StringComparison.OrdinalIgnoreCase))
                {
                    if (layer is not ("Api" or "Infrastructure" or "Application"))
                    {
                        violations.Add(
                            $"{projectName}: layer {layer} cannot reference building blocks.");
                    }

                    continue;
                }

                var targetName = Path.GetFileNameWithoutExtension(reference);
                var targetContext = Directory.GetParent(Path.GetDirectoryName(reference)!)!.Name;
                var targetLayer = targetName[(targetName.LastIndexOf('.') + 1)..];

                if (!string.Equals(context, targetContext, StringComparison.Ordinal))
                {
                    violations.Add($"{projectName} references another context: {targetName}.");
                    continue;
                }

                var allowed = layer switch
                {
                    "Api" => targetLayer is "Application" or "Contracts" or "Infrastructure",
                    "Infrastructure" => targetLayer is "Application" or "Domain",
                    "Application" => targetLayer is "Domain",
                    "Domain" or "Contracts" => false,
                    _ => false,
                };

                if (!allowed)
                {
                    violations.Add(
                        $"{projectName} has a forbidden reference to {targetName}.");
                }
            }

            if (layer is "Domain" or "Contracts" &&
                document.Descendants("PackageReference").Any())
            {
                violations.Add(
                    $"{projectName}: {layer} must not depend on NuGet packages in the foundation.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void BuildingBlocksDoNotContainSharedDomainProjects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildingBlocksRoot = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "BuildingBlocks");
        var forbidden = Directory
            .EnumerateFileSystemEntries(
                buildingBlocksRoot,
                "*Domain*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(repositoryRoot, path));

        Assert.Empty(forbidden);
    }

    [Fact]
    public void EngineeringFixturePreservesLayerAndBuildingBlockDirection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "PlatformFixtures",
            "Engineering");
        var violations = new List<string>();

        foreach (var project in Directory.EnumerateFiles(
                     fixtureRoot,
                     "*.csproj",
                     SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(project);
            if (projectName.EndsWith("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            var layer = projectName[(projectName.LastIndexOf('.') + 1)..];
            var document = XDocument.Load(project);
            var targets = document.Descendants("ProjectReference")
                .Select(reference => reference.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Select(include => ResolveProjectReference(project, include!))
                .Select(Path.GetFileNameWithoutExtension)
                .ToArray();

            foreach (var target in targets)
            {
                var allowed = layer switch
                {
                    "Api" => target is
                        "Vtt.ServiceDefaults" or
                        "Vtt.EngineeringFixture.Application" or
                        "Vtt.EngineeringFixture.Contracts" or
                        "Vtt.EngineeringFixture.Infrastructure",
                    "Infrastructure" => target is
                        "Vtt.Cqrs" or
                        "Vtt.Messaging" or
                        "Vtt.Messaging.Nats" or
                        "Vtt.Persistence.Marten" or
                        "Vtt.EngineeringFixture.Application" or
                        "Vtt.EngineeringFixture.Domain",
                    "Application" => target is
                        "Vtt.Cqrs" or
                        "Vtt.Messaging" or
                        "Vtt.EngineeringFixture.Domain",
                    "Domain" => target is "Vtt.EventSourcing",
                    "Contracts" => false,
                    _ => false,
                };

                if (!allowed)
                {
                    violations.Add($"{projectName} has a forbidden reference to {target}.");
                }
            }

            if (layer is "Domain" or "Contracts" &&
                document.Descendants("PackageReference").Any())
            {
                violations.Add($"{projectName}: {layer} must be technology-independent.");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void BuildingBlocksDoNotReferenceFixtureOrProductDomainTypes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var buildingBlocksRoot = Path.Combine(
            repositoryRoot,
            "src",
            "backend",
            "BuildingBlocks");
        var violations = Directory.EnumerateFiles(
                buildingBlocksRoot,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("Vtt.EngineeringFixture", StringComparison.Ordinal) ||
                       source.Contains("Vtt.Campaign.Domain", StringComparison.Ordinal) ||
                       source.Contains("Vtt.Character.Domain", StringComparison.Ordinal);
            })
            .Select(file => Path.GetRelativePath(repositoryRoot, file));

        Assert.Empty(violations);
    }

    [Theory]
    [InlineData(@"..\Target\Vtt.Target.Domain.csproj")]
    [InlineData("../Target/Vtt.Target.Domain.csproj")]
    public void ProjectReferenceResolutionAcceptsBothSeparatorStyles(string include)
    {
        var sourceProject = Path.Combine(
            Path.GetTempPath(),
            "Source",
            "Vtt.Source.Api.csproj");
        var expected = Path.GetFullPath(Path.Combine(
            Path.GetTempPath(),
            "Target",
            "Vtt.Target.Domain.csproj"));

        var actual = ResolveProjectReference(sourceProject, include);

        Assert.Equal(expected, actual);
    }

    private static string ResolveProjectReference(string project, string include)
    {
        var normalizedInclude = include
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(project)!,
            normalizedInclude));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "Vtt.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not locate the repository root.");
    }
}
