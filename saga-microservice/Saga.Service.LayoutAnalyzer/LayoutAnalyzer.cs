using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    private static readonly DiagnosticDescriptor DomainRule = new(
        DomainRuleId,
        "Domain may not reference Infrastructure or Features",
        "File in '{0}' may not 'using {1}': Domain has no infrastructure or feature dependencies",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Saga Domain depends on Contracts.Integration.InboundEvents by design — "
            + "the pure state machines pattern-match on inbound integration event types and saga emits commands directly "
            + "(no IIntegrationMap seam). Contracts is therefore not on the forbidden list for Domain.");

    private static readonly DiagnosticDescriptor FeatureSliceRule = new(
        FeatureSliceRuleId,
        "Feature slice may not reference another feature slice",
        "File in slice '{0}' may not 'using {1}': feature slices are isolated; duplicate or extract to Domain/Shared on the third use",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InfrastructureRule = new(
        InfrastructureRuleId,
        "Infrastructure may not reference Features",
        "File in '{0}' may not 'using {1}': Infrastructure implements abstractions from Domain only",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ContractsRule = new(
        ContractsRuleId,
        "Contracts may not reference any other internal Saga.Service.* namespace",
        "File in '{0}' may not 'using {1}': cross-service contracts must depend only on framework types",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DomainRule, FeatureSliceRule, InfrastructureRule, ContractsRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTree);
    }

    private static void AnalyzeSyntaxTree(SyntaxTreeAnalysisContext context)
    {
        var root = context.Tree.GetRoot(context.CancellationToken);
        var fileNamespace = GetFileNamespace(root);
        if (fileNamespace is null || !StartsWith(fileNamespace, "Saga.Service"))
        {
            return;
        }

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var target = NormalizeQualifiedName(usingDirective.Name?.ToString());
            if (target is null || !StartsWith(target, "Saga.Service"))
            {
                continue;
            }

            CheckRules(context, usingDirective.Name ?? (SyntaxNode)usingDirective, fileNamespace, target);
        }

        foreach (var qualifiedName in root.DescendantNodes().OfType<QualifiedNameSyntax>())
        {
            if (qualifiedName.Ancestors().OfType<UsingDirectiveSyntax>().Any())
            {
                continue;
            }

            var target = NormalizeQualifiedName(qualifiedName.ToString());
            if (target is null || !StartsWith(target, "Saga.Service"))
            {
                continue;
            }

            CheckRules(context, qualifiedName, fileNamespace, target);
        }
    }

    private static string? GetFileNamespace(SyntaxNode root)
    {
        var fileScoped = root.DescendantNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScoped is not null)
        {
            return fileScoped.Name.ToString();
        }

        var block = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        return block?.Name.ToString();
    }

    private static void CheckRules(
        SyntaxTreeAnalysisContext context,
        SyntaxNode node,
        string fileNamespace,
        string targetNamespace)
    {
        var location = node.GetLocation();

        if (StartsWith(fileNamespace, "Saga.Service.Domain")
            && (StartsWith(targetNamespace, "Saga.Service.Infrastructure")
                || StartsWith(targetNamespace, "Saga.Service.Features")))
        {
            context.ReportDiagnostic(Diagnostic.Create(DomainRule, location, fileNamespace, targetNamespace));
            return;
        }

        if (StartsWith(fileNamespace, "Saga.Service.Features")
            && StartsWith(targetNamespace, "Saga.Service.Features"))
        {
            // Two-level slice identity: the slice is the first TWO segments after Features
            // (e.g. "OrderSaga.StockReserved" vs "OrderSaga.PaymentAuthorized" are distinct,
            // and "OrderSaga.PaymentRefunded" vs "RefundSaga.PaymentRefunded" are distinct).
            var fileSlice = GetSliceSegment(fileNamespace);
            var targetSlice = GetSliceSegment(targetNamespace);
            if (fileSlice is not null
                && targetSlice is not null
                && !fileSlice.Equals(targetSlice, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(FeatureSliceRule, location, fileSlice, targetNamespace));
                return;
            }
        }

        if (StartsWith(fileNamespace, "Saga.Service.Infrastructure")
            && StartsWith(targetNamespace, "Saga.Service.Features"))
        {
            context.ReportDiagnostic(Diagnostic.Create(InfrastructureRule, location, fileNamespace, targetNamespace));
            return;
        }

        if (StartsWith(fileNamespace, "Saga.Service.Contracts")
            && (StartsWith(targetNamespace, "Saga.Service.Domain")
                || StartsWith(targetNamespace, "Saga.Service.Infrastructure")
                || StartsWith(targetNamespace, "Saga.Service.Features")))
        {
            context.ReportDiagnostic(Diagnostic.Create(ContractsRule, location, fileNamespace, targetNamespace));
        }
    }

    private static bool StartsWith(string? value, string prefix)
        => value is not null
            && (value.Equals(prefix, StringComparison.Ordinal)
                || value.StartsWith(prefix + ".", StringComparison.Ordinal));

    private static string? NormalizeQualifiedName(string? value)
    {
        const string globalPrefix = "global::";
        return value is not null && value.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? value.Substring(globalPrefix.Length)
            : value;
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
