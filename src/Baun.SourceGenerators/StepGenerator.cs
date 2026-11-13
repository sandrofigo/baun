using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Baun.SourceGenerators;

/// <summary>
/// Completes every build class, that is every class with methods returning a <c>Step</c>, by implementing
/// <c>IBaunBuild</c> and registering each step under its method name. The step named by the class's
/// <c>[Entry]</c> attribute is marked as the entry step.
/// <para>
/// In an executable without top-level statements or a <c>Main</c> method, it also adds an entry point that runs
/// the build class, so a build doesn't need to write one.
/// </para>
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

    private static readonly DiagnosticDescriptor EntryIsNotAStep = new(
        id: "BAUN004",
        title: "Entry is not a step",
        messageFormat: "The [Entry] of build class '{0}' must name one of its steps, but '{1}' is not a step. Use nameof, for example [Entry(nameof(Test))].",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultipleBuildClasses = new(
        id: "BAUN005",
        title: "More than one build class without an entry point",
        messageFormat: "baun can't choose which build class to run: {0}. Run one yourself, for example with 'await BaunBuild.RunAsync<{1}>();'.",
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

        // Whether the code already has an entry point: top-level statements or a static Main method.
        var hasEntryPoint = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => IsEntryPoint(node),
                transform: static (_, _) => true)
            .Collect()
            .Select(static (entryPoints, _) => !entryPoints.IsEmpty);

        var isExecutable = context.CompilationProvider
            .Select(static (compilation, _) => compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication);

        var needsEntryPoint = hasEntryPoint
            .Combine(isExecutable)
            .Select(static (pair, _) => !pair.Left && pair.Right);

        context.RegisterSourceOutput(
            steps.Combine(needsEntryPoint),
            static (context, pair) => Generate(context, pair.Left, needsEntryPoint: pair.Right));
    }

    private static bool IsEntryPoint(SyntaxNode node) =>
        node is GlobalStatementSyntax
        || node is MethodDeclarationSyntax { Identifier.ValueText: "Main" } method && method.Modifiers.Any(SyntaxKind.StaticKeyword);

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

        var entryAttribute = type.GetAttributes().FirstOrDefault(attribute => IsBaunEntryAttribute(attribute.AttributeClass));
        var entry = entryAttribute is null ? null : GetEntry(entryAttribute, type, cancellationToken);

        return new StepMethod(
            TypeKey: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace: type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            TypeName: type.Name,
            MethodName: method.Name,
            Entry: entry,
            IsPartial: isPartial,
            IsSupported: isSupported,
            TypeLocation: LocationInfo.From(type.Locations.FirstOrDefault()));
    }

    private static EntryInfo GetEntry(AttributeData attribute, INamedTypeSymbol type, System.Threading.CancellationToken cancellationToken)
    {
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) as AttributeSyntax;
        var stepName = attribute.ConstructorArguments.FirstOrDefault().Value as string;

        // nameof of a name that doesn't exist, such as nameof(Tset), is already a compiler error,
        // so baun doesn't report it a second time.
        var usesNameof = syntax?.ArgumentList?.Arguments.FirstOrDefault()?.Expression is InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
        };

        var isCompilerError = usesNameof && stepName is not null && type.GetMembers(stepName).IsEmpty;

        return new EntryInfo(
            StepName: isCompilerError ? null : stepName,
            Location: LocationInfo.From(syntax?.GetLocation()));
    }

    private static bool IsBaunStep(ITypeSymbol type) =>
        type is INamedTypeSymbol { Name: "Step", TypeKind: TypeKind.Delegate } named
        && IsInBaunNamespace(named);

    private static bool IsBaunEntryAttribute(INamedTypeSymbol? type) =>
        type is { Name: "EntryAttribute" }
        && IsInBaunNamespace(type);

    private static bool IsInBaunNamespace(INamedTypeSymbol type) =>
        type.ContainingNamespace is { Name: "Baun", ContainingNamespace.IsGlobalNamespace: true }
        && type.ContainingAssembly?.Name == "Baun";

    private static void Generate(SourceProductionContext context, IEnumerable<StepMethod> steps, bool needsEntryPoint)
    {
        var buildClasses = new List<StepMethod>();

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

            var stepNames = type.Select(step => step.MethodName).Distinct().ToList();

            // The class is still completed, so a wrong entry is the only error.
            if (first.Entry is { StepName: { } entryName } entry && !stepNames.Contains(entryName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    EntryIsNotAStep,
                    entry.Location?.ToLocation() ?? first.TypeLocation?.ToLocation(),
                    first.TypeName,
                    entryName));
            }

            var methods = stepNames.Select(name => (Name: name, IsEntry: name == first.Entry?.StepName)).ToList();

            var hintName = type.Key.Replace("global::", string.Empty).Replace('<', '_').Replace('>', '_') + ".Steps.g.cs";

            context.AddSource(hintName, SourceText.From(GenerateSource(first, methods), Encoding.UTF8));
            buildClasses.Add(first);
        }

        if (!needsEntryPoint || buildClasses.Count == 0)
        {
            return;
        }

        if (buildClasses.Count > 1)
        {
            foreach (var buildClass in buildClasses)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MultipleBuildClasses,
                    buildClass.TypeLocation?.ToLocation(),
                    string.Join(", ", buildClasses.Select(type => type.TypeName)),
                    buildClass.TypeName));
            }

            return;
        }

        context.AddSource("BaunEntryPoint.g.cs", SourceText.From(GenerateEntryPoint(buildClasses[0]), Encoding.UTF8));
    }

    private static string GenerateEntryPoint(StepMethod buildClass) => $$"""
        // <auto-generated/>
        #nullable enable

        /// <summary>
        /// Runs the build. Added by baun because the build has no top-level statements and no Main method.
        /// </summary>
        internal static class BaunEntryPoint
        {
            private static global::System.Threading.Tasks.Task Main() =>
                global::Baun.BaunBuild.RunAsync<{{buildClass.TypeKey}}>();
        }

        """;

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

        source.AppendLine($"partial class {type.TypeName} : global::Baun.IBaunBuild");
        source.AppendLine("{");
        source.AppendLine("    global::System.Collections.Generic.IReadOnlyList<global::Baun.BaunStep> global::Baun.IBaunBuild.GetSteps() =>");
        source.AppendLine("        new global::Baun.BaunStep[]");
        source.AppendLine("        {");

        foreach (var (name, isEntry) in methods)
        {
            var entry = isEntry ? ", IsEntry: true" : string.Empty;
            source.AppendLine($"            new global::Baun.BaunStep(\"{name}\", {name}{entry}),");
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
        EntryInfo? Entry,
        bool IsPartial,
        bool IsSupported,
        LocationInfo? TypeLocation);

    private sealed record EntryInfo(string? StepName, LocationInfo? Location);

    private sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public static LocationInfo? From(Location? location) =>
            location?.SourceTree is null
                ? null
                : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

        public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
    }
}
