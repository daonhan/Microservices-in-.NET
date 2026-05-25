using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ECommerce.Shared.LayoutAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutAnalyzer : DiagnosticAnalyzer
{
    public const string AbstractionsToImplRuleId = "SHALAY001";
    public const string ImplToCompositionRuleId = "SHALAY002";
    public const string CrossPackageRuleId = "SHALAY003";

    private const string Category = "Layout";

    private static readonly DiagnosticDescriptor AbstractionsToImplRule = new(
        AbstractionsToImplRuleId,
        "Abstractions may not reference Impl",
        "File in '{0}' may not 'using {1}': Abstractions define ports independent of adapters",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ImplToCompositionRule = new(
        ImplToCompositionRuleId,
        "Impl may not reference Composition",
        "File in '{0}' may not 'using {1}': Impl adapters do not depend on DI composition",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CrossPackageRule = new(
        CrossPackageRuleId,
        "Cross-package import outside allowlist",
        "File in package '{0}' may not 'using {1}': cross-package dependency not in allowlist",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(AbstractionsToImplRule, ImplToCompositionRule, CrossPackageRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        // Phase 1: descriptors registered but analyzer is inert.
        // Per-package allowlist data + rule firing logic is populated in phases 2-9.
    }
}
