using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Payment.Service.LayoutAnalyzer;

namespace Payment.Tests.Architecture;

public class LayoutAnalyzerTests
{
    [Fact]
    public async Task Domain_WhenFullyQualifiedInfrastructureReference_ThenReportsDomainRule()
    {
        const string targetSource = """
            using Payment.Service.Infrastructure.Data.EntityFramework;
            namespace Payment.Service.Domain;
            public sealed class Sample
            {
                private PaymentContext? _context;
            }
            """;
        const string referencedSource = """
            namespace Payment.Service.Infrastructure.Data.EntityFramework;
            public sealed class PaymentContext { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact]
    public async Task Feature_WhenFullyQualifiedOtherSliceReference_ThenReportsFeatureRule()
    {
        const string targetSource = """
            using Payment.Service.Features.RefundPayment;
            namespace Payment.Service.Features.CapturePayment;
            public sealed class Sample
            {
                private RefundHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Payment.Service.Features.RefundPayment;
            public sealed class RefundHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            using Payment.Service.Features.CapturePayment;
            namespace Payment.Service.Infrastructure.Outbox;
            public sealed class Sample
            {
                private CaptureHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Payment.Service.Features.CapturePayment;
            public sealed class CaptureHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    [Fact]
    public async Task Contracts_WhenFullyQualifiedDomainReference_ThenReportsContractsRule()
    {
        const string targetSource = """
            using Payment.Service.Domain;
            namespace Payment.Service.Contracts.Integration;
            public sealed class Sample
            {
                private PaymentAggregate? _aggregate;
            }
            """;
        const string referencedSource = """
            namespace Payment.Service.Domain;
            public sealed class PaymentAggregate { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.ContractsRuleId);
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
