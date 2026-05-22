using System.Collections.Immutable;
using Auth.Service.LayoutAnalyzer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Auth.Tests.Architecture;

public class LayoutAnalyzerTests
{
    [Fact]
    public async Task Domain_WhenFullyQualifiedInfrastructureReference_ThenReportsDomainRule()
    {
        const string targetSource = """
            namespace Auth.Service.Domain;
            public sealed class Sample
            {
                private Auth.Service.Infrastructure.Data.EntityFramework.AuthContext? _context;
            }
            """;
        const string referencedSource = """
            namespace Auth.Service.Infrastructure.Data.EntityFramework;
            public sealed class AuthContext { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.DomainRuleId);
    }

    [Fact]
    public async Task Feature_WhenFullyQualifiedOtherSliceReference_ThenReportsFeatureRule()
    {
        const string targetSource = """
            namespace Auth.Service.Features.Login;
            public sealed class Sample
            {
                private Auth.Service.Features.GetJwks.JwksDocument? _document;
            }
            """;
        const string referencedSource = """
            namespace Auth.Service.Features.GetJwks;
            public sealed class JwksDocument { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.FeatureSliceRuleId);
    }

    [Fact]
    public async Task Infrastructure_WhenFullyQualifiedFeatureReference_ThenReportsInfrastructureRule()
    {
        const string targetSource = """
            namespace Auth.Service.Infrastructure.Signing;
            public sealed class Sample
            {
                private Auth.Service.Features.Login.LoginHandler? _handler;
            }
            """;
        const string referencedSource = """
            namespace Auth.Service.Features.Login;
            public sealed class LoginHandler { }
            """;

        var diagnostics = await GetDiagnosticsAsync(targetSource, referencedSource);

        Assert.Contains(diagnostics, d => d.Id == LayoutAnalyzer.InfrastructureRuleId);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(params string[] sources)
    {
        var syntaxTrees = sources.Select(source => CSharpSyntaxTree.ParseText(source)).ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "AuthLayoutAnalyzerTests",
            syntaxTrees: syntaxTrees,
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new LayoutAnalyzer()));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
