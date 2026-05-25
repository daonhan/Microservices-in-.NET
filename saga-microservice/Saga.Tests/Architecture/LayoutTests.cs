using NetArchTest.Rules;

namespace Saga.Tests.Architecture;

public class LayoutTests
{
    private static readonly System.Reflection.Assembly SagaServiceAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_DoesNotReference_InfrastructureOrFeatures()
    {
        // Saga Domain depends on Contracts.Integration.InboundEvents by design — the pure state
        // machines pattern-match on inbound integration event types and saga emits commands directly
        // (no IIntegrationMap seam). Contracts is therefore not on the forbidden list for Domain.
        var result = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Saga.Service.Infrastructure",
                "Saga.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain types may not reference Infrastructure or Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        // Two-level slice identity: slice = first TWO segments after "Saga.Service.Features."
        // (e.g. "OrderSaga.StockReserved" vs "OrderSaga.PaymentAuthorized" are distinct,
        // and "OrderSaga.PaymentRefunded" vs "RefundSaga.PaymentRefunded" are distinct).
        var featureTypes = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Saga.Service.Features.", StringComparison.Ordinal))
            .Select(GetSliceSegment)
            .Where(s => s is not null)
            .Select(s => s!)
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            // Trailing '.' so the dependency match stops at a namespace boundary:
            // without it a shorter slice name would also match a longer one sharing its prefix.
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"Saga.Service.Features.{s}.")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            // Anchored regex for the same boundary reason on the selector side.
            // Escape dots in the slice path (e.g. "OrderSaga.StockReserved") for the regex.
            var sliceRegex = slice.Replace(".", @"\.");
            var result = Types.InAssembly(SagaServiceAssembly)
                .That()
                .ResideInNamespaceMatching($@"^Saga\.Service\.Features\.{sliceRegex}(\.|$)")
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
        var result = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Saga.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference Saga.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Contracts_DoNotReference_InternalLayers()
    {
        var result = Types.InAssembly(SagaServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Saga.Service.Contracts")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Saga.Service.Domain",
                "Saga.Service.Infrastructure",
                "Saga.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Contracts types may not reference Domain, Infrastructure or Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    private static string? GetSliceSegment(string ns)
    {
        const string prefix = "Saga.Service.Features.";
        if (!ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = ns.Substring(prefix.Length);
        var firstDot = remainder.IndexOf('.');
        if (firstDot < 0)
        {
            return null;
        }

        var afterFirst = remainder.Substring(firstDot + 1);
        var secondDot = afterFirst.IndexOf('.');
        var secondSegment = secondDot < 0 ? afterFirst : afterFirst.Substring(0, secondDot);
        return string.Concat(remainder.Substring(0, firstDot), ".", secondSegment);
    }
}
