using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Saga.Service.LayoutAnalyzer;

namespace Saga.Tests.Architecture;

public class LayoutAnalyzerTests
{
    [Fact(Skip = "enabled in Phase 10")]
    public async Task Domain_WhenFullyQualifiedInfrastructureReference_ThenReportsDomainRule()
    {
        const string targetSource = """
            using Saga.Service.Infrastructure.Data.EntityFramework;
            namespace Saga.Service.Domain;
            public sealed class Sample
            {
                private SagaContext? _context;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Infrastructure.Data.EntityFramework;
            public sealed class SagaContext { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact(Skip = "enabled in Phase 10")]
    public async Task Feature_WhenFullyQualifiedOtherSliceReference_ThenReportsFeatureRule()
    {
        const string targetSource = """
            using Saga.Service.Features.RefundSaga.RefundRequested;
            namespace Saga.Service.Features.OrderSaga.OrderCreated;
            public sealed class Sample
            {
                private RefundHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Features.RefundSaga.RefundRequested;
            public sealed class RefundHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact(Skip = "enabled in Phase 10")]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            using Saga.Service.Features.OrderSaga.OrderCreated;
            namespace Saga.Service.Infrastructure.Outbox;
            public sealed class Sample
            {
                private OrderCreatedHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Features.OrderSaga.OrderCreated;
            public sealed class OrderCreatedHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    [Fact(Skip = "enabled in Phase 10")]
    public async Task Contracts_WhenFullyQualifiedDomainReference_ThenReportsContractsRule()
    {
        const string targetSource = """
            using Saga.Service.Domain;
            namespace Saga.Service.Contracts.Integration;
            public sealed class Sample
            {
                private SagaInstance? _instance;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Domain;
            public sealed class SagaInstance { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.ContractsRuleId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "SagaLayoutAnalyzerTests",
            syntaxTrees: syntaxTrees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LayoutAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
