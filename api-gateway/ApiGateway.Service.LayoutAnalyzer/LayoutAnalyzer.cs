using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ApiGateway.Service.LayoutAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutAnalyzer : DiagnosticAnalyzer
{
    public const string DomainRuleId = "AGWLAY001";
    public const string FeatureSliceRuleId = "AGWLAY002";
    public const string InfrastructureRuleId = "AGWLAY003";
    public const string ContractsRuleId = "AGWLAY004";

    private const string Category = "Layout";

    private static readonly DiagnosticDescriptor DomainRule = new(
        DomainRuleId,
        "Domain may not exist in ApiGateway",
        "File in '{0}' may not 'using {1}': gateway owns no aggregate; no Domain layer permitted",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    private static readonly DiagnosticDescriptor FeatureSliceRule = new(
        FeatureSliceRuleId,
        "Feature slice may not reference another feature slice",
        "File in slice '{0}' may not 'using {1}': feature slices are isolated; duplicate or extract to Infrastructure/Shared on the third use",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    private static readonly DiagnosticDescriptor InfrastructureRule = new(
        InfrastructureRuleId,
        "Infrastructure may not reference Features",
        "File in '{0}' may not 'using {1}': Infrastructure implements abstractions independent of feature slices",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    private static readonly DiagnosticDescriptor ContractsRule = new(
        ContractsRuleId,
        "Contracts may not exist in ApiGateway",
        "File in '{0}' may not 'using {1}': gateway publishes no integration events; no Contracts layer permitted",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DomainRule, FeatureSliceRule, InfrastructureRule, ContractsRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Phase 1 scaffold: rules declared but disabled. Phase 11 promotes to error severity and registers analysis actions.
    }
}
