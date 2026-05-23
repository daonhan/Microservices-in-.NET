using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Shipping.Service.LayoutAnalyzer;

namespace Shipping.Tests.Architecture;

public class LayoutAnalyzerTests
{
    [Fact]
    public async Task Domain_WhenFullyQualifiedInfrastructureReference_ThenReportsDomainRule()
    {
        const string targetSource = """
            namespace Shipping.Service.Domain;
            public sealed class Sample
            {
                private Shipping.Service.Infrastructure.Data.EntityFramework.ShippingContext? _context;
            }
            """;
        const string referencedSource = """
            namespace Shipping.Service.Infrastructure.Data.EntityFramework;
            public sealed class ShippingContext { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact]
    public async Task Feature_WhenFullyQualifiedOtherSliceReference_ThenReportsFeatureRule()
    {
        const string targetSource = """
            namespace Shipping.Service.Features.Pack;
            public sealed class Sample
            {
                private Shipping.Service.Features.Pick.PickHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Shipping.Service.Features.Pick;
            public sealed class PickHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            namespace Shipping.Service.Infrastructure.Outbox;
            public sealed class Sample
            {
                private Shipping.Service.Features.Pack.PackHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Shipping.Service.Features.Pack;
            public sealed class PackHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "ShippingLayoutAnalyzerTests",
            syntaxTrees: syntaxTrees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LayoutAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
