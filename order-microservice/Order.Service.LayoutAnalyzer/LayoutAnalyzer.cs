using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Order.Service.LayoutAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutAnalyzer : DiagnosticAnalyzer
{
    public const string DomainRuleId = "ORDLAY001";
    public const string FeatureSliceRuleId = "ORDLAY002";
    public const string InfrastructureRuleId = "ORDLAY003";
    public const string ContractsRuleId = "ORDLAY004";

    private const string Category = "Layout";

    private static readonly DiagnosticDescriptor DomainRule = new(
        DomainRuleId,
        "Domain may not reference Infrastructure or Features",
        "File in '{0}' may not 'using {1}': Domain has no infrastructure or feature dependencies",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

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
        "Contracts may not reference any other internal Order.Service.* namespace",
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
        if (fileNamespace is null || !StartsWith(fileNamespace, "Order.Service"))
        {
            return;
        }

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var target = usingDirective.Name?.ToString();
            if (target is null || !StartsWith(target, "Order.Service"))
            {
                continue;
            }

            CheckRules(context, usingDirective, fileNamespace, target);
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
        UsingDirectiveSyntax directive,
        string fileNamespace,
        string targetNamespace)
    {
        var location = (directive.Name ?? (SyntaxNode)directive).GetLocation();

        if (StartsWith(fileNamespace, "Order.Service.Domain")
            && (StartsWith(targetNamespace, "Order.Service.Infrastructure")
                || StartsWith(targetNamespace, "Order.Service.Features")))
        {
            context.ReportDiagnostic(Diagnostic.Create(DomainRule, location, fileNamespace, targetNamespace));
            return;
        }

        if (StartsWith(fileNamespace, "Order.Service.Features.")
            && StartsWith(targetNamespace, "Order.Service.Features."))
        {
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

        if (StartsWith(fileNamespace, "Order.Service.Infrastructure")
            && StartsWith(targetNamespace, "Order.Service.Features"))
        {
            context.ReportDiagnostic(Diagnostic.Create(InfrastructureRule, location, fileNamespace, targetNamespace));
            return;
        }

        if (StartsWith(fileNamespace, "Order.Service.Contracts")
            && (StartsWith(targetNamespace, "Order.Service.Domain")
                || StartsWith(targetNamespace, "Order.Service.Infrastructure")
                || StartsWith(targetNamespace, "Order.Service.Features")
                || StartsWith(targetNamespace, "Order.Service.Endpoints")
                || StartsWith(targetNamespace, "Order.Service.ApiModels")
                || StartsWith(targetNamespace, "Order.Service.IntegrationEvents")))
        {
            context.ReportDiagnostic(Diagnostic.Create(ContractsRule, location, fileNamespace, targetNamespace));
        }
    }

    private static bool StartsWith(string? value, string prefix)
        => value is not null
            && (value.Equals(prefix, StringComparison.Ordinal)
                || value.StartsWith(prefix + ".", StringComparison.Ordinal));

    private static string? GetSliceSegment(string ns)
    {
        const string prefix = "Order.Service.Features.";
        if (!ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = ns.Substring(prefix.Length);
        var dot = remainder.IndexOf('.');
        return dot < 0 ? remainder : remainder.Substring(0, dot);
    }
}
