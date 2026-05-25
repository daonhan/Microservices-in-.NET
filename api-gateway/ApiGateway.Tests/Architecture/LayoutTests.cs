using NetArchTest.Rules;

namespace ApiGateway.Tests.Architecture;

public class LayoutTests
{
    private static readonly System.Reflection.Assembly ApiGatewayAssembly = typeof(Program).Assembly;

    [Fact(Skip = "enabled in Phase 11")]
    public void Features_DoNotReference_OtherFeatureSlices()
    {
        var featureTypes = Types.InAssembly(ApiGatewayAssembly)
            .That()
            .ResideInNamespaceStartingWith("ApiGateway.Features")
            .GetTypes()
            .ToList();

        var slices = featureTypes
            .Select(t => t.Namespace ?? string.Empty)
            .Where(ns => ns.StartsWith("ApiGateway.Features.", StringComparison.Ordinal))
            .Select(GetSliceSegment)
            .Where(s => s is not null)
            .Select(s => s!)
            .Distinct()
            .ToList();

        var offenders = new List<string>();
        foreach (var slice in slices)
        {
            var otherSlices = slices.Where(s => !string.Equals(s, slice, StringComparison.Ordinal))
                .Select(s => $"ApiGateway.Features.{s}.")
                .ToArray();

            if (otherSlices.Length == 0)
            {
                continue;
            }

            var sliceRegex = slice.Replace(".", @"\.");
            var result = Types.InAssembly(ApiGatewayAssembly)
                .That()
                .ResideInNamespaceMatching($@"^ApiGateway\.Features\.{sliceRegex}(\.|$)")
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

    [Fact(Skip = "enabled in Phase 11")]
    public void Infrastructure_DoesNotReference_Features()
    {
        var result = Types.InAssembly(ApiGatewayAssembly)
            .That()
            .ResideInNamespaceStartingWith("ApiGateway.Infrastructure")
            .ShouldNot()
            .HaveDependencyOn("ApiGateway.Features")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure types may not reference ApiGateway.Features: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "enabled in Phase 11")]
    public void Features_OnlyReference_InfrastructureOrShared()
    {
        var result = Types.InAssembly(ApiGatewayAssembly)
            .That()
            .ResideInNamespaceStartingWith("ApiGateway.Features")
            .ShouldNot()
            .HaveDependencyOnAny(
                "ApiGateway.Domain",
                "ApiGateway.Contracts")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Features types may only depend on Infrastructure or ECommerce.Shared: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact(Skip = "enabled in Phase 11")]
    public void LegacyTopLevelFolders_DoNotExist()
    {
        string[] forbiddenNamespaces =
        [
            "ApiGateway.Endpoints",
            "ApiGateway.ApiModels",
            "ApiGateway.Models",
            "ApiGateway.Gateway",
            "ApiGateway.Operator"
        ];

        var offenders = Types.InAssembly(ApiGatewayAssembly)
            .GetTypes()
            .Where(t => forbiddenNamespaces.Any(ns =>
                (t.Namespace ?? string.Empty).StartsWith(ns + ".", StringComparison.Ordinal)
                || string.Equals(t.Namespace, ns, StringComparison.Ordinal)))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Legacy top-level folders must not exist: " + string.Join(", ", offenders));
    }

    [Fact(Skip = "enabled in Phase 11")]
    public void Domain_FolderDoesNotExist()
    {
        var offenders = Types.InAssembly(ApiGatewayAssembly)
            .That()
            .ResideInNamespaceStartingWith("ApiGateway.Domain")
            .GetTypes()
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "ApiGateway has no Domain layer (gateway owns no aggregate): "
            + string.Join(", ", offenders));
    }

    [Fact(Skip = "enabled in Phase 11")]
    public void Contracts_FolderDoesNotExist()
    {
        var offenders = Types.InAssembly(ApiGatewayAssembly)
            .That()
            .ResideInNamespaceStartingWith("ApiGateway.Contracts")
            .GetTypes()
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "ApiGateway has no Contracts layer (gateway publishes no integration events): "
            + string.Join(", ", offenders));
    }

    private static string? GetSliceSegment(string ns)
    {
        const string prefix = "ApiGateway.Features.";
        if (!ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = ns.Substring(prefix.Length);
        var firstDot = remainder.IndexOf('.');
        if (firstDot < 0)
        {
            return remainder;
        }

        var afterFirst = remainder.Substring(firstDot + 1);
        var secondDot = afterFirst.IndexOf('.');
        var secondSegment = secondDot < 0 ? afterFirst : afterFirst.Substring(0, secondDot);
        return string.Concat(remainder.Substring(0, firstDot), ".", secondSegment);
    }
}
