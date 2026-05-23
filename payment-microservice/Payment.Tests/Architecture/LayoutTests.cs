using NetArchTest.Rules;

namespace Payment.Tests.Architecture;

// Scaffolded in Phase 1 (issue #227) with every test skipped; the rules are unskipped and
// enforced in Phase 10 (issue #240) once the Clean Architecture + VSA layout is in place.
public class LayoutTests
{
    private const string Phase10 = "enabled in Phase 10";

    private static readonly System.Reflection.Assembly PaymentServiceAssembly = typeof(Program).Assembly;

    [Fact(Skip = Phase10)]
    public void Domain_DoesNotReference_InfrastructureFeaturesOrContracts()
    {
        var result = Types.InAssembly(PaymentServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Payment.Service.Domain")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Payment.Service.Infrastructure",
                "Payment.Service.Features",
                "Payment.Service.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain types may not reference Infrastructure, Features or Contracts: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = Phase10)]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        var featureTypes = Types.InAssembly(PaymentServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Payment.Service.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Payment.Service.Features.", StringComparison.Ordinal))
            .Select(ns => ns.Split('.')[3])
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            // Trailing '.' so the dependency match stops at a namespace boundary:
            // without it a shorter slice name would also match a longer one sharing its prefix.
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"Payment.Service.Features.{s}.")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            // Anchored regex for the same boundary reason on the selector side.
            var result = Types.InAssembly(PaymentServiceAssembly)
                .That()
                .ResideInNamespaceMatching($@"^Payment\.Service\.Features\.{slice}(\.|$)")
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

    [Fact(Skip = Phase10)]
    public void Infrastructure_DoesNotReference_Features()
    {
        var result = Types.InAssembly(PaymentServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Payment.Service.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Payment.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference Payment.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = Phase10)]
    public void Contracts_DoNotReference_InternalLayers()
    {
        var result = Types.InAssembly(PaymentServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Payment.Service.Contracts")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Payment.Service.Domain",
                "Payment.Service.Infrastructure",
                "Payment.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Contracts types may not reference Domain, Infrastructure or Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
