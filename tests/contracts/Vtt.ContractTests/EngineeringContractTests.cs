using System.Text.Json;

namespace Vtt.ContractTests;

public sealed class EngineeringContractTests
{
    [Fact]
    public void PublicContractsUseRequiredSpecificationVersions()
    {
        using var openApi = ReadJson("contracts/openapi/engineering-fixture.v1.json");
        using var asyncApi = ReadJson("contracts/asyncapi/engineering-fixture.v1.json");
        using var envelope = ReadJson("contracts/events/event-envelope.v1.schema.json");

        Assert.Equal("3.1.1", openApi.RootElement.GetProperty("openapi").GetString());
        Assert.Equal("3.0.0", asyncApi.RootElement.GetProperty("asyncapi").GetString());
        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            envelope.RootElement.GetProperty("$schema").GetString());
    }

    [Fact]
    public void EventSubjectAndEnvelopeAreExplicitlyVersioned()
    {
        using var asyncApi = ReadJson("contracts/asyncapi/engineering-fixture.v1.json");
        using var envelope = ReadJson("contracts/events/event-envelope.v1.schema.json");
        var address = asyncApi.RootElement
            .GetProperty("channels")
            .GetProperty("probeIncremented")
            .GetProperty("address")
            .GetString();
        var required = envelope.RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            "vtt.engineering-fixture.engineering-probe.incremented.v1",
            address);
        Assert.Contains("eventId", required);
        Assert.Contains("aggregate", required);
        Assert.Contains("correlationId", required);
        Assert.Contains("schemaVersion", required);
    }

    [Fact]
    public void ProblemDetailsContractHasStableMachineCodeAndCorrelation()
    {
        using var openApi = ReadJson("contracts/openapi/engineering-fixture.v1.json");
        var schema = openApi.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ProblemDetails");
        var required = schema.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Contains("code", required);
        Assert.Contains("correlationId", required);
        Assert.DoesNotContain("stackTrace", schema.GetProperty("properties").EnumerateObject()
            .Select(property => property.Name));
    }

    [Fact]
    public void ContractFieldsDoNotExposeCommonSecretOrPiiNames()
    {
        var files = new[]
        {
            "contracts/openapi/engineering-fixture.v1.json",
            "contracts/events/event-envelope.v1.schema.json",
            "contracts/events/engineering-probe-incremented.v1.schema.json",
        };
        var forbidden = new HashSet<string>(
            ["password", "secret", "token", "email", "rawBody"],
            StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();

        foreach (var file in files)
        {
            using var document = ReadJson(file);
            FindForbiddenProperties(document.RootElement, file, forbidden, violations);
        }

        Assert.Empty(violations);
    }

    private static void FindForbiddenProperties(
        JsonElement element,
        string path,
        HashSet<string> forbidden,
        List<string> violations)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals("properties"))
                {
                    foreach (var field in property.Value.EnumerateObject())
                    {
                        if (forbidden.Contains(field.Name))
                        {
                            violations.Add($"{path}:{field.Name}");
                        }
                    }
                }

                FindForbiddenProperties(property.Value, path, forbidden, violations);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                FindForbiddenProperties(item, path, forbidden, violations);
            }
        }
    }

    private static JsonDocument ReadJson(string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Vtt.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
