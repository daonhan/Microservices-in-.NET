using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Saga.Service.LayoutAnalyzer;

namespace Saga.Tests.Architecture;

public class LayoutAnalyzerTests
{
    [Fact]
    public async Task Domain_WhenFullyQualifiedInfrastructureReference_ThenReportsDomainRule()
    {
        const string targetSource = """
            namespace Saga.Service.Domain;
            public sealed class Sample
            {
                private Saga.Service.Infrastructure.Data.EntityFramework.SagaContext? _context;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Infrastructure.Data.EntityFramework;
            public sealed class SagaContext { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact]
    public async Task Feature_WhenFullyQualifiedCrossSagaSliceReference_ThenReportsFeatureRule()
    {
        // OrderSaga.OrderCreated using RefundSaga.RefundRequested — distinct sagas, distinct slices.
        const string targetSource = """
            namespace Saga.Service.Features.OrderSaga.OrderCreated;
            public sealed class Sample
            {
                private Saga.Service.Features.RefundSaga.RefundRequested.RefundHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Features.RefundSaga.RefundRequested;
            public sealed class RefundHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Feature_WhenFullyQualifiedSameSagaSiblingSliceReference_ThenReportsFeatureRule()
    {
        // Two-level slice identity: OrderSaga.StockReserved and OrderSaga.PaymentAuthorized
        // share the OrderSaga prefix but are still distinct slices.
        const string targetSource = """
            namespace Saga.Service.Features.OrderSaga.StockReserved;
            public sealed class Sample
            {
                private Saga.Service.Features.OrderSaga.PaymentAuthorized.PaymentAuthorizedHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Features.OrderSaga.PaymentAuthorized;
            public sealed class PaymentAuthorizedHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Feature_WhenSameSliceReference_ThenDoesNotReportFeatureRule()
    {
        // Same two-level slice path — intra-slice usings are allowed.
        const string targetSource = """
            namespace Saga.Service.Features.OrderSaga.StockReserved;
            public sealed class Sample
            {
                private Saga.Service.Features.OrderSaga.StockReserved.Helper? _helper;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Features.OrderSaga.StockReserved;
            public sealed class Helper { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.DoesNotContain(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            namespace Saga.Service.Infrastructure.Outbox;
            public sealed class Sample
            {
                private Saga.Service.Features.OrderSaga.OrderCreated.OrderCreatedHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Saga.Service.Features.OrderSaga.OrderCreated;
            public sealed class OrderCreatedHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    [Fact]
    public async Task Contracts_WhenFullyQualifiedDomainReference_ThenReportsContractsRule()
    {
        const string targetSource = """
            namespace Saga.Service.Contracts.Integration;
            public sealed class Sample
            {
                private Saga.Service.Domain.SagaInstance? _instance;
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
