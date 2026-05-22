using NetArchTest.Rules;

namespace Product.Tests.Architecture;

public class LayoutTests
{
    private static readonly System.Reflection.Assembly ProductServiceAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_DoesNotReference_InfrastructureOrFeatures()
    {
        var result = Types.InAssembly(ProductServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Product.Service.Domain")
            .ShouldNot()
            .HaveDependencyOnAny("Product.Service.Infrastructure", "Product.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain types may not reference Product.Service.Infrastructure or Product.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        var featureTypes = Types.InAssembly(ProductServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Product.Service.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Product.Service.Features.", StringComparison.Ordinal))
            .Select(ns => ns.Split('.')[3])
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"Product.Service.Features.{s}")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(ProductServiceAssembly)
                .That()
                .ResideInNamespaceStartingWith($"Product.Service.Features.{slice}")
                .ShouldNot()
                .HaveDependencyOnAny(otherSlices)
                .GetResult();

            if (!result.IsSuccessful)
            {
                offenders.AddRange(result.FailingTypeNames ?? []);
            }
        }

        Assert.True(offenders.Count == 0,
            "Features.<X> may not reference Features.<Y> for X != Y: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Infrastructure_DoesNotReference_Features()
    {
        var result = Types.InAssembly(ProductServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Product.Service.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Product.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference Product.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Contracts_DoNotReference_OtherProductServiceNamespaces()
    {
        var result = Types.InAssembly(ProductServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Product.Service.Contracts")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Product.Service.Domain",
                "Product.Service.Infrastructure",
                "Product.Service.Features",
                "Product.Service.Endpoints",
                "Product.Service.ApiModels",
                "Product.Service.IntegrationEvents")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Contracts types may not reference any other internal Product.Service.* namespace: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
