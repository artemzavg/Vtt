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
                .Select(include => Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(project)!, include!)));

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

