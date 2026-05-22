using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Inventory.Service.LayoutAnalyzer;

/// <summary>
/// Roslyn guardrail for the Inventory.Service Clean Architecture + Vertical Slices layout.
/// Phase 1 scaffold (issue #195): the diagnostics are declared but no detection logic runs.
/// Detection logic and the hidden -> error severity promotion land in Phase 9 (issue #206).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutAnalyzer : DiagnosticAnalyzer
{
    public const string DomainRuleId = "INVLAY001";
    public const string FeatureSliceRuleId = "INVLAY002";
    public const string InfrastructureRuleId = "INVLAY003";

    private const string Category = "Layout";

    private static readonly DiagnosticDescriptor DomainRule = new(
        DomainRuleId,
        "Domain may not reference Infrastructure or Features",
        "File in '{0}' may not reference '{1}': Domain has no infrastructure or feature dependencies",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FeatureSliceRule = new(
        FeatureSliceRuleId,
        "Feature slice may not reference another feature slice",
        "File in slice '{0}' may not reference '{1}': feature slices are isolated; duplicate or extract to Domain/Shared on the third use",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InfrastructureRule = new(
        InfrastructureRuleId,
        "Infrastructure may not reference Features",
        "File in '{0}' may not reference '{1}': Infrastructure implements abstractions from Domain only",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DomainRule, FeatureSliceRule, InfrastructureRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Phase 1 scaffold (issue #195): rules are intentionally off — no syntax actions registered.
        // Phase 9 (issue #206) wires the banned-namespace / banned-symbol detection.
    }
}
