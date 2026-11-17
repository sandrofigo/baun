using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Baun.SourceGenerators;

/// <summary>
/// Suppresses the nullable warning CS8618 for parameters without an initial value, such as <c>Solution Solution;</c>.
/// baun sets them before any step runs, or fails if nothing does, so they don't need <c>= null!</c>.
/// </summary>
/// <remarks>
/// The compiler reports CS8618 on the field or property when the class has no constructor of its own, and on the
/// constructor otherwise. Only the first case is suppressed.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterNullabilitySuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor ParameterIsSetByBaun = new(
        id: "BAUNSPR001",
        suppressedDiagnosticId: "CS8618",
        justification: "baun sets parameters before any step runs, and fails if a required parameter has no value");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } = ImmutableArray.Create(ParameterIsSetByBaun);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            var tree = diagnostic.Location.SourceTree;

            if (tree is null)
            {
                continue;
            }

            var node = tree.GetRoot(context.CancellationToken).FindNode(diagnostic.Location.SourceSpan);

            if (node is not (VariableDeclaratorSyntax or PropertyDeclarationSyntax))
            {
                continue;
            }

            var symbol = context.GetSemanticModel(tree).GetDeclaredSymbol(node, context.CancellationToken);

            if (symbol is IFieldSymbol or IPropertySymbol && symbol.GetAttributes().Any(attribute => IsParameterAttribute(attribute.AttributeClass)))
            {
                context.ReportSuppression(Suppression.Create(ParameterIsSetByBaun, diagnostic));
            }
        }
    }

    private static bool IsParameterAttribute(INamedTypeSymbol? type) =>
        type is { Name: "ParameterAttribute", ContainingNamespace: { Name: "Baun", ContainingNamespace.IsGlobalNamespace: true } }
        && type.ContainingAssembly?.Name == "Baun";
}
