using System.Text.Json;
using YamlDotNet.RepresentationModel;

namespace ECommerce.Shared.Tests;

public sealed class AsbEmulatorComposeProfileTests
{
    [Fact]
    public void Given_Compose_File_When_Inspecting_Asb_Profile_Then_Emulator_Services_Are_Opt_In()
    {
        var services = LoadComposeServices();

        var emulator = GetService(services, "servicebus-emulator");
        var sql = GetService(services, "servicebus-sql");
        var rabbitmq = GetService(services, "rabbitmq");

        Assert.Contains("asb", GetScalarSequence(emulator, "profiles"));
        Assert.Contains("asb", GetScalarSequence(sql, "profiles"));
        Assert.False(TryGetNode(rabbitmq, "profiles", out _));

        Assert.Contains("servicebus-sql", GetDependsOn(emulator));
        Assert.Contains("${ASB_EMULATOR_AMQP_PORT:-5673}:5672", GetScalarSequence(emulator, "ports"));
        Assert.Contains("${ASB_EMULATOR_HTTP_PORT:-5300}:${ASB_EMULATOR_HTTP_PORT:-5300}", GetScalarSequence(emulator, "ports"));
        Assert.Contains("./infra/local/asb-emulator/Config.json:/ServiceBus_Emulator/ConfigFiles/Config.json:ro", GetScalarSequence(emulator, "volumes"));

        Assert.Contains("SQL_SERVER=servicebus-sql", GetEnvironment(emulator));
        Assert.Contains("ACCEPT_EULA=Y", GetEnvironment(emulator));
        Assert.Contains("EMULATOR_HTTP_PORT=${ASB_EMULATOR_HTTP_PORT:-5300}", GetEnvironment(emulator));

        Assert.Contains("ACCEPT_EULA=Y", GetEnvironment(sql));
        Assert.Contains("MSSQL_SA_PASSWORD=${ASB_EMULATOR_SQL_PASSWORD:-}", GetEnvironment(sql));
    }

    [Fact]
    public void Given_Asb_Emulator_Config_When_Read_Then_Local_Namespace_And_Ecommerce_Topic_Are_Defined()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("infra/local/asb-emulator/Config.json")));
        var userConfig = document.RootElement.GetProperty("UserConfig");
        var namespaces = userConfig.GetProperty("Namespaces");

        var @namespace = Assert.Single(namespaces.EnumerateArray());
        Assert.Equal("sbemulatorns", @namespace.GetProperty("Name").GetString());
        Assert.Equal(JsonValueKind.Array, @namespace.GetProperty("Queues").ValueKind);

        var topic = Assert.Single(@namespace.GetProperty("Topics").EnumerateArray());
        Assert.Equal("ecommerce-topic", topic.GetProperty("Name").GetString());
        Assert.Equal(JsonValueKind.Array, topic.GetProperty("Subscriptions").ValueKind);

        Assert.Equal("Console", userConfig.GetProperty("Logging").GetProperty("Type").GetString());
    }

    [Fact]
    public void Given_Qa_Docs_When_Read_Then_Asb_Emulator_Start_And_Teardown_Are_Documented()
    {
        var docs = File.ReadAllText(FindRepoFile("docs/qa/asb-emulator-local.md"));

        Assert.Contains("docker compose --profile asb up", docs, StringComparison.Ordinal);
        Assert.Contains("docker compose --profile asb down -v --remove-orphans", docs, StringComparison.Ordinal);
        Assert.Contains("UseDevelopmentEmulator=true", docs, StringComparison.Ordinal);
        Assert.Contains("ASB_EMULATOR_AMQP_PORT", docs, StringComparison.Ordinal);
        Assert.Contains("ASB_EMULATOR_HTTP_PORT", docs, StringComparison.Ordinal);
    }

    [Fact]
    public void Given_Readme_And_Qa_Docs_When_Read_Then_Local_Asb_Workflow_Is_Discoverable()
    {
        var readme = File.ReadAllText(FindRepoFile("README.md"));
        var docs = File.ReadAllText(FindRepoFile("docs/qa/asb-emulator-local.md"));

        Assert.Contains("docs/qa/asb-emulator-local.md", readme, StringComparison.Ordinal);
        Assert.Contains("RabbitMQ remains the default", readme, StringComparison.Ordinal);
        Assert.Contains("docker compose --profile asb up", readme, StringComparison.Ordinal);

        Assert.Contains("Messaging__Provider=AzureServiceBus", docs, StringComparison.Ordinal);
        Assert.Contains("AzureServiceBus__ConnectionString", docs, StringComparison.Ordinal);
        Assert.Contains("AzureServiceBus__AdministrationConnectionString", docs, StringComparison.Ordinal);
        Assert.Contains("ASB_EMULATOR_TESTS=true", docs, StringComparison.Ordinal);
        Assert.Contains("Phase-4 smoke", docs, StringComparison.Ordinal);
        Assert.Contains("Real Azure topology remains Bicep-owned", docs, StringComparison.Ordinal);
    }

    private static YamlMappingNode LoadComposeServices()
    {
        var stream = new YamlStream();
        using var reader = File.OpenText(FindRepoFile("docker-compose.yaml"));
        stream.Load(reader);

        var root = Assert.IsType<YamlMappingNode>(stream.Documents[0].RootNode);
        return Assert.IsType<YamlMappingNode>(GetRequiredNode(root, "services"));
    }

    private static YamlMappingNode GetService(YamlMappingNode services, string serviceName) =>
        Assert.IsType<YamlMappingNode>(GetRequiredNode(services, serviceName));

    private static string[] GetDependsOn(YamlMappingNode service)
    {
        var dependsOn = GetRequiredNode(service, "depends_on");

        if (dependsOn is YamlSequenceNode sequence)
        {
            return GetScalarValues(sequence);
        }

        if (dependsOn is YamlMappingNode mapping)
        {
            return mapping.Children.Keys
                .OfType<YamlScalarNode>()
                .Select(key => key.Value ?? string.Empty)
                .ToArray();
        }

        return [];
    }

    private static string[] GetEnvironment(YamlMappingNode service)
    {
        var environment = GetRequiredNode(service, "environment");

        if (environment is YamlSequenceNode sequence)
        {
            return GetScalarValues(sequence);
        }

        var mapping = Assert.IsType<YamlMappingNode>(environment);
        return mapping.Children
            .Select(child => $"{GetScalarValue(child.Key)}={GetScalarValue(child.Value)}")
            .ToArray();
    }

    private static string[] GetScalarSequence(YamlMappingNode mapping, string key) =>
        GetScalarValues(Assert.IsType<YamlSequenceNode>(GetRequiredNode(mapping, key)));

    private static string[] GetScalarValues(YamlSequenceNode sequence) =>
        sequence.Children
            .Select(GetScalarValue)
            .ToArray();

    private static YamlNode GetRequiredNode(YamlMappingNode mapping, string key)
    {
        Assert.True(TryGetNode(mapping, key, out var value), $"Missing YAML key '{key}'.");
        return value;
    }

    private static bool TryGetNode(YamlMappingNode mapping, string key, out YamlNode value)
    {
        foreach (var child in mapping.Children)
        {
            if (child.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                value = child.Value;
                return true;
            }
        }

        value = new YamlScalarNode();
        return false;
    }

    private static string GetScalarValue(YamlNode node) =>
        Assert.IsType<YamlScalarNode>(node).Value ?? string.Empty;

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
