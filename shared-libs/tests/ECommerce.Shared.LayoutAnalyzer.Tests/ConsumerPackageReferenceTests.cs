using System.Xml.Linq;

namespace ECommerce.Shared.LayoutAnalyzer.Tests;

public sealed class ConsumerPackageReferenceTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedSharedPackages =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["auth-microservice/Auth.Service/Auth.Service.csproj"] =
            [
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["basket-microservice/Basket.Service/Basket.Service.csproj"] =
            [
                "ECommerce.Shared.EventBus",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["product-microservice/Product.Service/Product.Service.csproj"] =
            [
                "ECommerce.Shared.EventBus",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["order-microservice/Order.Service/Order.Service.csproj"] =
            [
                "ECommerce.Shared.Contracts",
                "ECommerce.Shared.EventBus",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["inventory-microservice/Inventory.Service/Inventory.Service.csproj"] =
            [
                "ECommerce.Shared.Contracts",
                "ECommerce.Shared.EventBus",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["payment-microservice/Payment.Service/Payment.Service.csproj"] =
            [
                "ECommerce.Shared.Contracts",
                "ECommerce.Shared.EventBus",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["shipping-microservice/Shipping.Service/Shipping.Service.csproj"] =
            [
                "ECommerce.Shared.Contracts",
                "ECommerce.Shared.EventBus",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["saga-microservice/Saga.Service/Saga.Service.csproj"] =
            [
                "ECommerce.Shared.Contracts",
                "ECommerce.Shared.EventBus",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
                "ECommerce.Shared.Testing.Qa",
            ],
            ["api-gateway/ApiGateway/ApiGateway.csproj"] =
            [
                "ECommerce.Shared.DeadLetter",
                "ECommerce.Shared.Messaging",
                "ECommerce.Shared.Platform",
            ],
        };

    [Fact]
    public void ProductionServices_Use_Narrow_SharedPackageSets()
    {
        var repoRoot = FindRepositoryRoot();
        var expectedVersion = ReadSharedVersion(repoRoot);

        foreach (var (relativePath, expectedPackages) in ExpectedSharedPackages)
        {
            var references = ReadPackageReferences(repoRoot, relativePath)
                .Where(reference => reference.Id.StartsWith("ECommerce.Shared", StringComparison.Ordinal))
                .OrderBy(reference => reference.Id, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                expectedPackages.Order(StringComparer.Ordinal),
                references.Select(reference => reference.Id));
            Assert.All(references, reference => Assert.Equal(expectedVersion, reference.Version));
        }
    }

    [Fact]
    public void ProductionServices_DoNotBypass_Messaging_OrUse_DeadLetter_ForProviderSelection()
    {
        var repoRoot = FindRepositoryRoot();
        var references = ExpectedSharedPackages.Keys
            .SelectMany(relativePath => ReadPackageReferences(repoRoot, relativePath)
                .Select(reference => (Project: relativePath, Reference: reference)))
            .Where(item => item.Reference.Id.StartsWith("ECommerce.Shared", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(references, item => item.Reference.Id == "ECommerce.Shared");
        Assert.DoesNotContain(references, item => item.Reference.Id == "ECommerce.Shared.RabbitMq");
        Assert.DoesNotContain(references, item => item.Reference.Id == "ECommerce.Shared.AzureServiceBus");

        Assert.Equal(
            ["api-gateway/ApiGateway/ApiGateway.csproj"],
            references
                .Where(item => item.Reference.Id == "ECommerce.Shared.DeadLetter")
                .Select(item => item.Project)
                .Order(StringComparer.Ordinal));
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "shared-libs"))
                && Directory.Exists(Path.Combine(directory.FullName, "auth-microservice")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string ReadSharedVersion(DirectoryInfo repoRoot)
    {
        var propsPath = Path.Combine(repoRoot.FullName, "shared-libs", "Directory.Build.props");
        var props = XDocument.Load(propsPath);
        return props.Root?.Element("PropertyGroup")?.Element("Version")?.Value
            ?? throw new InvalidOperationException("shared-libs/Directory.Build.props does not declare <Version>.");
    }

    private static IEnumerable<PackageReference> ReadPackageReferences(DirectoryInfo repoRoot, string relativePath)
    {
        var path = Path.Combine(repoRoot.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var project = XDocument.Load(path);

        return project
            .Descendants("PackageReference")
            .Select(element => new PackageReference(
                element.Attribute("Include")?.Value ?? string.Empty,
                element.Attribute("Version")?.Value ?? string.Empty));
    }

    private sealed record PackageReference(string Id, string Version);
}
