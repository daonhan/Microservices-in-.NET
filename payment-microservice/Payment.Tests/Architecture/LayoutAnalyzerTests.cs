using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Payment.Service.LayoutAnalyzer;

namespace Payment.Tests.Architecture;

// Scaffolded in Phase 1 (issue #227) with every test skipped; the analyzer detection logic and
// these tests are unskipped and enforced in Phase 10 (issue #240).
public class LayoutAnalyzerTests
{
    private const string Phase10 = "enabled in Phase 10";

    [Fact(Skip = Phase10)]
    public async Task Domain_WhenFullyQualifiedInfrastructureReference_ThenReportsDomainRule()
    {
        const string targetSource = """
            namespace Payment.Service.Domain;
            public sealed class Sample
            {
                private Payment.Service.Infrastructure.Data.EntityFramework.PaymentContext? _context;
            }
            """;
        const string referencedSource = """
            namespace Payment.Service.Infrastructure.Data.EntityFramework;
            public sealed class PaymentContext { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact(Skip = Phase10)]
    public async Task Feature_WhenFullyQualifiedOtherSliceReference_ThenReportsFeatureRule()
    {
        const string targetSource = """
            namespace Payment.Service.Features.CapturePayment;
            public sealed class Sample
            {
                private Payment.Service.Features.RefundPayment.RefundHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Payment.Service.Features.RefundPayment;
            public sealed class RefundHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact(Skip = Phase10)]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            namespace Payment.Service.Infrastructure.Outbox;
            public sealed class Sample
            {
                private Payment.Service.Features.CapturePayment.CaptureHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Payment.Service.Features.CapturePayment;
            public sealed class CaptureHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "PaymentLayoutAnalyzerTests",
            syntaxTrees: syntaxTrees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LayoutAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
