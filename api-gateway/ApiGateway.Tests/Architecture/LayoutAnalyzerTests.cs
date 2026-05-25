using System.Collections.Immutable;
using ApiGateway.Service.LayoutAnalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ApiGateway.Tests.Architecture;

public class LayoutAnalyzerTests
{
    [Fact]
    public async Task Feature_WhenFullyQualifiedCrossSliceReference_ThenReportsFeatureRule()
    {
        const string targetSource = """
            using ApiGateway.Features.Operator.ReplayFailure;
            namespace ApiGateway.Features.Operator.DiscardFailure;
            public sealed class Sample
            {
                private ReplayHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace ApiGateway.Features.Operator.ReplayFailure;
            public sealed class ReplayHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            using ApiGateway.Features.Operator.ListFailures;
            namespace ApiGateway.Infrastructure.Polling;
            public sealed class Sample
            {
                private ListHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace ApiGateway.Features.Operator.ListFailures;
            public sealed class ListHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    [Fact]
    public async Task Domain_WhenAnyTypeDeclared_ThenReportsDomainRule()
    {
        const string targetSource = """
            namespace ApiGateway.Domain;
            public sealed class ForbiddenAggregate { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact]
    public async Task Contracts_WhenAnyTypeDeclared_ThenReportsContractsRule()
    {
        const string targetSource = """
            namespace ApiGateway.Contracts.Integration;
            public sealed class ForbiddenIntegrationEvent { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.ContractsRuleId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "ApiGatewayLayoutAnalyzerTests",
            syntaxTrees: syntaxTrees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LayoutAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
