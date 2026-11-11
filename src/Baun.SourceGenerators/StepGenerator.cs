using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace BaunSourceGenerators;

/// <summary>
/// Completes every build class, that is every class with methods returning a <c>Step</c>, by implementing
/// <c>IBaunBuild</c> and registering each step under its method name.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StepGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor BuildClassMustBePartial = new(
        id: "BAUN001",
        title: "Build class must be partial",
        messageFormat: "The build class '{0}' must be declared 'partial' so baun can register its steps",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor BuildClassNotSupported = new(
        id: "BAUN002",
        title: "Build class is not supported",
        messageFormat: "The build class '{0}' must be a non-generic top-level class",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultipleEntrySteps = new(
        id: "BAUN003",
        title: "Build class has more than one entry step",
        messageFormat: "The build class '{0}' has more than one [Entry] step: {1}",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var steps = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsStepMethodCandidate(node),
                transform: static (context, cancellationToken) => GetStepMethod(context, cancellationToken))
            .Where(static step => step is not null)
            .Select(static (step, _) => step!)
            .Collect();

        context.RegisterSourceOutput(steps, static (context, steps) => Generate(context, steps));
    }

    private static bool IsStepMethodCandidate(SyntaxNode node) =>
        node is MethodDeclarationSyntax method
        && method.ParameterList.Parameters.Count == 0
        && method.TypeParameterList is null
        && GetSimpleName(method.ReturnType) == "Step";

    private static string? GetSimpleName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
        AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
        _ => null,
    };

    private static StepMethod? GetStepMethod(GeneratorSyntaxContext context, System.Threading.CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not IMethodSymbol method
            || method.IsStatic
            || !IsBaunStep(method.ReturnType)
            || method.ContainingType is not { TypeKind: TypeKind.Class } type)
        {
            return null;
        }

        var isPartial = type.DeclaringSyntaxReferences.All(reference =>
            reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

        var isSupported = type.ContainingType is null && !type.IsGenericType;

        return new StepMethod(
            TypeKey: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            TypeName: type.Name,
            MethodName: method.Name,
            IsEntry: method.GetAttributes().Any(attribute => IsBaunEntryAttribute(attribute.AttributeClass)),
            IsPartial: isPartial,
            IsSupported: isSupported,
            TypeLocation: LocationInfo.From(type.Locations.FirstOrDefault()));
    }

    private static bool IsBaunStep(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Step", TypeKind: TypeKind.Delegate } named
        && named.ContainingNamespace.IsGlobalNamespace
        && named.ContainingAssembly?.Name == "Baun";

    private static bool IsBaunEntryAttribute(INamedTypeSymbol? type) =>
        type is { Name: "EntryAttribute" }
        && type.ContainingNamespace.IsGlobalNamespace
        && type.ContainingAssembly?.Name == "Baun";

    private static void Generate(SourceProductionContext context, IEnumerable<StepMethod> steps)
    {
        foreach (var type in steps.GroupBy(step => step.TypeKey))
        {
            var first = type.First();

            if (!first.IsSupported)
            {
                context.ReportDiagnostic(Diagnostic.Create(BuildClassNotSupported, first.TypeLocation?.ToLocation(), first.TypeName));
                continue;
            }

            if (!first.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(BuildClassMustBePartial, first.TypeLocation?.ToLocation(), first.TypeName));
                continue;
            }

            var methods = type
                .GroupBy(step => step.MethodName)
                .Select(group => (Name: group.Key, IsEntry: group.Any(step => step.IsEntry)))
                .ToList();

            var entrySteps = methods.Where(method => method.IsEntry).Select(method => method.Name).ToList();

            if (entrySteps.Count > 1)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MultipleEntrySteps,
                    first.TypeLocation?.ToLocation(),
                    first.TypeName,
                    string.Join(", ", entrySteps)));
                continue;
            }

            var hintName = type.Key.Replace("global::", string.Empty).Replace('<', '_').Replace('>', '_') + ".Steps.g.cs";

            context.AddSource(hintName, SourceText.From(GenerateSource(first, methods), Encoding.UTF8));
        }
    }

    private static string GenerateSource(StepMethod type, IReadOnlyList<(string Name, bool IsEntry)> methods)
    {
        var source = new StringBuilder();

        source.AppendLine("// <auto-generated/>");
        source.AppendLine("#nullable enable");
        source.AppendLine();

        if (type.Namespace is not null)
        {
            source.AppendLine($"namespace {type.Namespace};");
            source.AppendLine();
        }

        source.AppendLine($"partial class {type.TypeName} : global::IBaunBuild");
        source.AppendLine("{");
        source.AppendLine("    global::System.Collections.Generic.IReadOnlyList<global::BaunStep> global::IBaunBuild.GetSteps() =>");
        source.AppendLine("        new global::BaunStep[]");
        source.AppendLine("        {");

        foreach (var (name, isEntry) in methods)
        {
            var entry = isEntry ? ", IsEntry: true" : string.Empty;
            source.AppendLine($"            new global::BaunStep(\"{name}\", {name}{entry}),");
        }

        source.AppendLine("        };");
        source.AppendLine("}");

        return source.ToString();
    }

    private sealed record StepMethod(
        string TypeKey,
        string? Namespace,
        string TypeName,
        string MethodName,
        bool IsEntry,
        bool IsPartial,
        bool IsSupported,
        LocationInfo? TypeLocation);

    private sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public static LocationInfo? From(Location? location) =>
            location?.SourceTree is null
                ? null
                : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

        public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
    }
}
