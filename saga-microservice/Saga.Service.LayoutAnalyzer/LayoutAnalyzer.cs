using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Saga.Service.LayoutAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutAnalyzer : DiagnosticAnalyzer
{
    public const string DomainRuleId = "SAGLAY001";
    public const string FeatureSliceRuleId = "SAGLAY002";
    public const string InfrastructureRuleId = "SAGLAY003";
    public const string ContractsRuleId = "SAGLAY004";

    private const string Category = "Layout";

    // Scaffolded in Phase 1: descriptors reserved, severity Hidden, disabled by default.
    // Phase 10 promotes severity to Error, enables by default, and fills in analysis logic.
    private static readonly DiagnosticDescriptor DomainRule = new(
        DomainRuleId,
        "Domain may not reference Infrastructure or Features",
        "File in '{0}' may not 'using {1}': Domain has no infrastructure or feature dependencies",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    private static readonly DiagnosticDescriptor FeatureSliceRule = new(
        FeatureSliceRuleId,
        "Feature slice may not reference another feature slice",
        "File in slice '{0}' may not 'using {1}': feature slices are isolated; duplicate or extract to Domain/Shared on the third use",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    private static readonly DiagnosticDescriptor InfrastructureRule = new(
        InfrastructureRuleId,
        "Infrastructure may not reference Features",
        "File in '{0}' may not 'using {1}': Infrastructure implements abstractions from Domain only",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    private static readonly DiagnosticDescriptor ContractsRule = new(
        ContractsRuleId,
        "Contracts may not reference any other internal Saga.Service.* namespace",
        "File in '{0}' may not 'using {1}': cross-service contracts must depend only on framework types",
        Category,
        DiagnosticSeverity.Hidden,
        isEnabledByDefault: false);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DomainRule, FeatureSliceRule, InfrastructureRule, ContractsRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Phase 1: no analysis actions registered — Phase 10 wires RegisterSyntaxTreeAction.
    }
}
