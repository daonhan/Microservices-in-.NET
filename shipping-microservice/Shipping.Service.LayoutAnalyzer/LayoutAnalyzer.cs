using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Shipping.Service.LayoutAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutAnalyzer : DiagnosticAnalyzer
{
    public const string DomainRuleId = "SHPLAY001";
    public const string FeatureSliceRuleId = "SHPLAY002";
    public const string InfrastructureRuleId = "SHPLAY003";

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
        context.RegisterSyntaxNodeAction(AnalyzeUsingDirective, SyntaxKind.UsingDirective);
        context.RegisterSyntaxNodeAction(AnalyzeSymbolReference,
            SyntaxKind.IdentifierName,
            SyntaxKind.GenericName,
            SyntaxKind.QualifiedName,
            SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeUsingDirective(SyntaxNodeAnalysisContext context)
    {
        var root = context.Node.SyntaxTree.GetRoot(context.CancellationToken);
        var fileNamespace = GetFileNamespace(root);
        if (fileNamespace is null || !StartsWith(fileNamespace, "Shipping.Service"))
        {
            return;
        }

        var usingDirective = (UsingDirectiveSyntax)context.Node;
        var target = usingDirective.Name?.ToString();
        if (target is null || !StartsWith(target, "Shipping.Service"))
        {
            return;
        }

        var reference = usingDirective.Name is null
            ? (SyntaxNode)usingDirective
            : usingDirective.Name;
        CheckRules(context, reference, fileNamespace, target);
    }

    private static void AnalyzeSymbolReference(SyntaxNodeAnalysisContext context)
    {
        if (context.Node.Parent is UsingDirectiveSyntax)
        {
            return;
        }

        if (context.Node.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax)
        {
            return;
        }

        var root = context.Node.SyntaxTree.GetRoot(context.CancellationToken);
        var fileNamespace = GetFileNamespace(root);
        if (fileNamespace is null || !StartsWith(fileNamespace, "Shipping.Service"))
        {
            return;
        }

        var targetNamespace = GetTargetNamespace(context);
        if (targetNamespace is null || !StartsWith(targetNamespace, "Shipping.Service"))
        {
            return;
        }

        CheckRules(context, context.Node, fileNamespace, targetNamespace);
    }

    private static string? GetTargetNamespace(SyntaxNodeAnalysisContext context)
    {
        var symbolInfo = context.SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        var ns = symbol switch
        {
            INamespaceSymbol namespaceSymbol => namespaceSymbol.ToDisplayString(),
            INamedTypeSymbol typeSymbol => typeSymbol.ContainingNamespace.ToDisplayString(),
            IMethodSymbol methodSymbol => methodSymbol.ContainingType?.ContainingNamespace.ToDisplayString(),
            IPropertySymbol propertySymbol => propertySymbol.ContainingType?.ContainingNamespace.ToDisplayString(),
            IFieldSymbol fieldSymbol => fieldSymbol.ContainingType?.ContainingNamespace.ToDisplayString(),
            IEventSymbol eventSymbol => eventSymbol.ContainingType?.ContainingNamespace.ToDisplayString(),
            _ => symbol?.ContainingNamespace?.ToDisplayString()
        };

        return string.IsNullOrEmpty(ns) ? null : ns;
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
        SyntaxNodeAnalysisContext context,
        SyntaxNode reference,
        string fileNamespace,
        string targetNamespace)
    {
        var location = reference.GetLocation();

        if (StartsWith(fileNamespace, "Shipping.Service.Domain")
            && (StartsWith(targetNamespace, "Shipping.Service.Infrastructure")
                || StartsWith(targetNamespace, "Shipping.Service.Features")))
        {
            context.ReportDiagnostic(Diagnostic.Create(DomainRule, location, fileNamespace, targetNamespace));
            return;
        }

        if (StartsWith(fileNamespace, "Shipping.Service.Features")
            && StartsWith(targetNamespace, "Shipping.Service.Features"))
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

        if (StartsWith(fileNamespace, "Shipping.Service.Infrastructure")
            && StartsWith(targetNamespace, "Shipping.Service.Features"))
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
        const string prefix = "Shipping.Service.Features.";
        if (!ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = ns.Substring(prefix.Length);
        var dot = remainder.IndexOf('.');
        return dot < 0 ? remainder : remainder.Substring(0, dot);
    }
}
