using NetArchTest.Rules;

namespace Auth.Tests.Architecture;

public class LayoutTests
{
    private static readonly System.Reflection.Assembly AuthServiceAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_DoesNotReference_InfrastructureOrFeatures()
    {
        var result = Types.InAssembly(AuthServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Auth.Service.Domain")
            .ShouldNot()
            .HaveDependencyOnAny("Auth.Service.Infrastructure", "Auth.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Domain types may not reference Auth.Service.Infrastructure or Auth.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        var featureTypes = Types.InAssembly(AuthServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Auth.Service.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("Auth.Service.Features.", StringComparison.Ordinal))
            .Select(ns => ns.Split('.')[3])
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            // Trailing '.' so the dependency match stops at a namespace boundary:
            // without it a shorter slice name would also match a longer one sharing its prefix.
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"Auth.Service.Features.{s}.")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            // Anchored regex for the same boundary reason on the selector side.
            var result = Types.InAssembly(AuthServiceAssembly)
                .That()
                .ResideInNamespaceMatching($@"^Auth\.Service\.Features\.{slice}(\.|$)")
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
        var result = Types.InAssembly(AuthServiceAssembly)
            .That()
            .ResideInNamespaceStartingWith("Auth.Service.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("Auth.Service.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference Auth.Service.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
