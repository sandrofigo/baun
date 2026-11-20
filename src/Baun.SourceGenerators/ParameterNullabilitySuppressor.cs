using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Baun.SourceGenerators;

/// <summary>
/// Suppresses the nullable warning CS8618 for parameters without an initial value, such as <c>Solution Solution;</c>.
/// baun sets them before any step runs, or fails if nothing does, so they don't need <c>= null!</c>. It also suppresses
/// CS0649 for <c>readonly</c> parameter fields, which baun assigns although the compiler can't see it.
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

    private static readonly SuppressionDescriptor ReadOnlyParameterIsAssignedByBaun = new(
        id: "BAUNSPR002",
        suppressedDiagnosticId: "CS0649",
        justification: "baun assigns readonly parameter fields before any step runs");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions { get; } =
        ImmutableArray.Create(ParameterIsSetByBaun, ReadOnlyParameterIsAssignedByBaun);

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
                var descriptor = diagnostic.Id == ReadOnlyParameterIsAssignedByBaun.SuppressedDiagnosticId
                    ? ReadOnlyParameterIsAssignedByBaun
                    : ParameterIsSetByBaun;
                context.ReportSuppression(Suppression.Create(descriptor, diagnostic));
            }
        }
    }

    private static bool IsParameterAttribute(INamedTypeSymbol? type) =>
        type is { Name: "ParameterAttribute", ContainingNamespace: { Name: "Baun", ContainingNamespace.IsGlobalNamespace: true } }
        && type.ContainingAssembly?.Name == "Baun";
}
