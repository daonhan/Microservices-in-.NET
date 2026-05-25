using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        "Namespace '{0}' is forbidden: gateway owns no aggregate; no Domain layer permitted",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor FeatureSliceRule = new(
        FeatureSliceRuleId,
        "Feature slice may not reference another feature slice",
        "File in slice '{0}' may not 'using {1}': feature slices are isolated; duplicate or extract to Infrastructure/Shared on the third use",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InfrastructureRule = new(
        InfrastructureRuleId,
        "Infrastructure may not reference Features",
        "File in '{0}' may not 'using {1}': Infrastructure implements abstractions independent of feature slices",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ContractsRule = new(
        ContractsRuleId,
        "Contracts may not exist in ApiGateway",
        "Namespace '{0}' is forbidden: gateway publishes no integration events; no Contracts layer permitted",
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
        var namespaceNode = GetNamespaceNode(root);
        var fileNamespace = GetNamespaceName(namespaceNode);
        if (fileNamespace is null || !StartsWith(fileNamespace, "ApiGateway"))
        {
            return;
        }

        if (StartsWith(fileNamespace, "ApiGateway.Domain"))
        {
            context.ReportDiagnostic(Diagnostic.Create(DomainRule, GetNamespaceLocation(namespaceNode!), fileNamespace));
            return;
        }

        if (StartsWith(fileNamespace, "ApiGateway.Contracts"))
        {
            context.ReportDiagnostic(Diagnostic.Create(ContractsRule, GetNamespaceLocation(namespaceNode!), fileNamespace));
            return;
        }

        foreach (var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var target = usingDirective.Name?.ToString();
            if (target is null || !StartsWith(target, "ApiGateway"))
            {
                continue;
            }

            CheckRules(context, usingDirective, fileNamespace, target);
        }
    }

    private static SyntaxNode? GetNamespaceNode(SyntaxNode root)
    {
        var fileScoped = root.DescendantNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScoped is not null)
        {
            return fileScoped;
        }

        return root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();
    }

    private static string? GetNamespaceName(SyntaxNode? node) => node switch
    {
        FileScopedNamespaceDeclarationSyntax fs => fs.Name.ToString(),
        NamespaceDeclarationSyntax block => block.Name.ToString(),
        _ => null
    };

    private static Location GetNamespaceLocation(SyntaxNode node) => node switch
    {
        FileScopedNamespaceDeclarationSyntax fs => fs.Name.GetLocation(),
        NamespaceDeclarationSyntax block => block.Name.GetLocation(),
        _ => Location.None
    };

    private static void CheckRules(
        SyntaxTreeAnalysisContext context,
        UsingDirectiveSyntax directive,
        string fileNamespace,
        string targetNamespace)
    {
        var location = (directive.Name ?? (SyntaxNode)directive).GetLocation();

        if (StartsWith(fileNamespace, "ApiGateway.Features")
            && StartsWith(targetNamespace, "ApiGateway.Features"))
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

        if (StartsWith(fileNamespace, "ApiGateway.Infrastructure")
            && StartsWith(targetNamespace, "ApiGateway.Features"))
        {
            context.ReportDiagnostic(Diagnostic.Create(InfrastructureRule, location, fileNamespace, targetNamespace));
        }
    }

    private static bool StartsWith(string? value, string prefix)
        => value is not null
            && (value.Equals(prefix, StringComparison.Ordinal)
                || value.StartsWith(prefix + ".", StringComparison.Ordinal));

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
