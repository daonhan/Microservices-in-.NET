using NetArchTest.Rules;

namespace Order.Tests.Architecture;

public class LayoutTests
{
    private static readonly System.Reflection.Assembly OrderServiceAssembly = typeof(Program).Assembly;

    [Fact(Skip = "Enabled in phase 8")]
    public void Domain_DoesNotReference_InfrastructureOrFeatures()
    {
        var result = Types.InAssembly(OrderServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Order.Service.Domain")
            .ShouldNot()
            .HaveDependencyOnAny("Order.Service.Infrastructure", "Order.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain types may not reference Order.Service.Infrastructure or Order.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "Enabled in phase 8")]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        var featureTypes = Types.InAssembly(OrderServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Order.Service.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Order.Service.Features.", StringComparison.Ordinal))
            .Select(ns => ns.Split('.')[3])
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"Order.Service.Features.{s}")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            var result = Types.InAssembly(OrderServiceAssembly)
                .That()
                .ResideInNamespaceStartingWith($"Order.Service.Features.{slice}")
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

    [Fact(Skip = "Enabled in phase 8")]
    public void Infrastructure_DoesNotReference_Features()
    {
        var result = Types.InAssembly(OrderServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Order.Service.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Order.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference Order.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "Enabled in phase 8")]
    public void Contracts_DoNotReference_OtherOrderServiceNamespaces()
    {
        var result = Types.InAssembly(OrderServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Order.Service.Contracts")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Order.Service.Domain",
                "Order.Service.Infrastructure",
                "Order.Service.Features",
                "Order.Service.Endpoints",
                "Order.Service.ApiModels",
                "Order.Service.IntegrationEvents")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Contracts types may not reference any other internal Order.Service.* namespace: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
