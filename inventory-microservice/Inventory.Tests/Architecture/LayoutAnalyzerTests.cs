using System.Collections.Immutable;
using Inventory.Service.LayoutAnalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Inventory.Tests.Architecture;

public class LayoutAnalyzerTests
{
    [Fact]
    public async Task Domain_WhenFullyQualifiedInfrastructureReference_ThenReportsDomainRule()
    {
        const string targetSource = """
            namespace Inventory.Service.Domain;
            public sealed class Sample
            {
                private Inventory.Service.Infrastructure.Data.EntityFramework.InventoryContext? _context;
            }
            """;
        const string referencedSource = """
            namespace Inventory.Service.Infrastructure.Data.EntityFramework;
            public sealed class InventoryContext { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact]
    public async Task Feature_WhenFullyQualifiedOtherSliceReference_ThenReportsFeatureRule()
    {
        const string targetSource = """
            namespace Inventory.Service.Features.Restock;
            public sealed class Sample
            {
                private Inventory.Service.Features.ReserveByHttp.ReserveHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Inventory.Service.Features.ReserveByHttp;
            public sealed class ReserveHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            namespace Inventory.Service.Infrastructure.Outbox;
            public sealed class Sample
            {
                private Inventory.Service.Features.Restock.RestockHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Inventory.Service.Features.Restock;
            public sealed class RestockHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "InventoryLayoutAnalyzerTests",
            syntaxTrees: syntaxTrees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LayoutAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
