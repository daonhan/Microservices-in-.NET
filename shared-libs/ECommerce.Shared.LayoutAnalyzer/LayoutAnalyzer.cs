using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    // Per-package layer namespace allowlists. Populated incrementally per carve phase
    // (Phase 2: Kernel). For each package, list the namespaces declared by files
    // physically located under that layer folder. Used to detect cross-layer
    // `using` violations (SHALAY001/002).
    private static readonly Dictionary<string, ImmutableArray<string>> KernelImplNamespaces =
        new(StringComparer.Ordinal)
        {
            ["Kernel"] = ImmutableArray.Create(
                "ECommerce.Shared.Observability.Metrics"),
        };

    private static readonly Dictionary<string, ImmutableArray<string>> KernelCompositionNamespaces =
        new(StringComparer.Ordinal)
        {
            ["Kernel"] = ImmutableArray<string>.Empty,
        };

    // Per-source-package cross-package using allowlist. Activated incrementally
    // per carve phase (Phase 3: Contracts → Kernel-only). For each source package
    // listed here, every `using ECommerce.Shared.*` directive must be either the
    // source package's own namespace or one of the allowed cross-package namespaces.
    // Entries are matched by exact namespace string — sub-namespaces (e.g.
    // ECommerce.Shared.Infrastructure.EventBus.Abstractions, owned by EventBus pkg)
    // are NOT silently allowed by listing the parent.
    private static readonly Dictionary<string, ImmutableArray<string>> CrossPackageAllowlist =
        new(StringComparer.Ordinal)
        {
            ["Contracts"] = ImmutableArray.Create(
                // Own namespace (Contracts package)
                "ECommerce.Shared.IntegrationEvents.Commands",
                // Kernel-owned namespaces
                "ECommerce.Shared.Infrastructure.EventBus",
                "ECommerce.Shared.Infrastructure.Messaging",
                "ECommerce.Shared.Observability.Metrics",
                "ECommerce.Shared.Kernel.TelemetryConventions"),
            ["Testing.Qa"] = ImmutableArray.Create(
                // Own namespace (Testing.Qa package)
                "ECommerce.Shared.Qa",
                // Kernel-owned namespaces
                "ECommerce.Shared.Infrastructure.EventBus",
                "ECommerce.Shared.Infrastructure.Messaging",
                "ECommerce.Shared.Observability.Metrics",
                "ECommerce.Shared.Kernel.TelemetryConventions"),
        };

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
        var classification = ClassifyFilePath(context.Tree.FilePath);
        if (classification.Package is null || classification.Layer is null)
        {
            return;
        }

        var root = context.Tree.GetRoot(context.CancellationToken);
        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var target = usingDirective.Name?.ToString();
            if (string.IsNullOrEmpty(target))
            {
                continue;
            }

            CheckLayerRules(context, usingDirective, classification.Package!, classification.Layer!, target!);
        }
    }

    private static void CheckLayerRules(
        SyntaxTreeAnalysisContext context,
        UsingDirectiveSyntax directive,
        string package,
        string layer,
        string targetNamespace)
    {
        var location = (directive.Name ?? (SyntaxNode)directive).GetLocation();

        if (string.Equals(layer, "Abstractions", StringComparison.Ordinal)
            && KernelImplNamespaces.TryGetValue(package, out var implNs)
            && MatchesAny(targetNamespace, implNs))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                AbstractionsToImplRule, location, package + "/Abstractions", targetNamespace));
            return;
        }

        if (string.Equals(layer, "Impl", StringComparison.Ordinal)
            && KernelCompositionNamespaces.TryGetValue(package, out var compNs)
            && MatchesAny(targetNamespace, compNs))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                ImplToCompositionRule, location, package + "/Impl", targetNamespace));
        }

        if (CrossPackageAllowlist.TryGetValue(package, out var allowed)
            && targetNamespace.StartsWith("ECommerce.Shared.", StringComparison.Ordinal)
            && !allowed.Contains(targetNamespace))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                CrossPackageRule, location, package, targetNamespace));
        }
    }

    private static bool MatchesAny(string targetNamespace, ImmutableArray<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (targetNamespace.Equals(candidate, StringComparison.Ordinal)
                || targetNamespace.StartsWith(candidate + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    // Returns (Package, Layer) for files under shared-libs/ECommerce.Shared.<Package>/<Layer>/...
    // where Layer ∈ { Abstractions, Impl, Composition }. Returns (null, null) for everything else.
    private static (string? Package, string? Layer) ClassifyFilePath(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return (null, null);
        }

        var normalized = filePath!.Replace('\\', '/');
        const string marker = "/ECommerce.Shared.";
        var markerIdx = normalized.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIdx < 0)
        {
            return (null, null);
        }

        var afterMarker = normalized.Substring(markerIdx + marker.Length);
        var firstSlash = afterMarker.IndexOf('/');
        if (firstSlash <= 0)
        {
            return (null, null);
        }

        var package = afterMarker.Substring(0, firstSlash);
        var afterPackage = afterMarker.Substring(firstSlash + 1);
        var secondSlash = afterPackage.IndexOf('/');
        if (secondSlash <= 0)
        {
            return (null, null);
        }

        var layer = afterPackage.Substring(0, secondSlash);
        if (layer != "Abstractions" && layer != "Impl" && layer != "Composition")
        {
            return (null, null);
        }

        return (package, layer);
    }
}
