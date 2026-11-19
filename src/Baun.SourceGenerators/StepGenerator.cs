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

    private static readonly DiagnosticDescriptor StepNamesDifferOnlyInCase = new(
        id: "BAUN006",
        title: "Step names differ only in case",
        messageFormat: "The build class '{0}' has steps whose names differ only in case: {1}. Step names are case-insensitive on the command line, so rename one of them.",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor StepNameIsCliCommand = new(
        id: "BAUN007",
        title: "Step name is a baun tool command",
        messageFormat: "The step '{0}' has the same name as the baun tool's '{1}' command, so 'baun {1}' runs the command. Run the step with 'baun --step {0}'.",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParameterMustBeWritable = new(
        id: "BAUN008",
        title: "Parameter must be writable",
        messageFormat: "The parameter '{0}' must be a non-static field that isn't readonly or const, or a non-static property with a set accessor, so baun can set it",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParameterTypeNotSupported = new(
        id: "BAUN009",
        title: "Parameter type is not supported",
        messageFormat: "The parameter '{0}' has the type '{1}', which baun can't set. Use string, bool, char, a number type, an enum, Guid, Uri, TimeSpan, AbsolutePath, Solution, Project, a nullable of these, or an array of them.",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParameterNotInBuildClass = new(
        id: "BAUN010",
        title: "Parameter is not in a build class",
        messageFormat: "The parameter '{0}' is in '{1}', which has no steps, so baun never sets it. Move it to the build class.",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParameterNameConflicts = new(
        id: "BAUN011",
        title: "Parameter name conflicts",
        messageFormat: "The parameter '{0}' conflicts with {1}. Names match case-insensitively and ignore '-' and '_', so rename it or set another name with [Parameter(Name = \"...\")].",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ParameterHidesOption = new(
        id: "BAUN012",
        title: "Parameter hides a built-in option",
        messageFormat: "The parameter '{0}' takes over the built-in option '--{1}', which stays available as '--baun-{1}'. Set another name with [Parameter(Name = \"...\")] to keep '--{1}'.",
        category: "Baun",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>The built-in options of a build, which parameters can't be named after, normalized.</summary>
    private static readonly string[] ReservedParameterNames = new[] { "HELP", "H", "VERSION", "V" };

    /// <summary>The built-in options a parameter can take over, normalized, with their names.</summary>
    private static readonly Dictionary<string, string> HideableOptions = new()
    {
        ["PLAN"] = "plan",
        ["STEP"] = "step",
        ["VERBOSITY"] = "verbosity",
    };

    /// <summary>The prefix of the names under which every built-in option stays available, such as <c>--baun-plan</c>, normalized.</summary>
    private const string OptionPrefix = "BAUN";

    /// <summary>
    /// The property added to every build class that returns the repository's root directory, unless the class already
    /// has a member with this name.
    /// </summary>
    private const string RootDirectoryName = "RootDirectory";

    /// <summary>The baun tool's commands, which take precedence over steps with the same name.</summary>
    private static readonly string[] CliCommands = new[] { "init" };

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

        var parameters = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Baun.ParameterAttribute",
                predicate: static (node, _) => node is VariableDeclaratorSyntax or PropertyDeclarationSyntax,
                transform: static (context, _) => GetParameterMember(context))
            .Where(static parameter => parameter is not null)
            .Select(static (parameter, _) => parameter!)
            .Collect();

        context.RegisterSourceOutput(
            steps.Combine(parameters).Combine(needsEntryPoint),
            static (context, pair) => Generate(context, pair.Left.Left, pair.Left.Right, needsEntryPoint: pair.Right));
    }

    private static ParameterMember? GetParameterMember(GeneratorAttributeSyntaxContext context)
    {
        var attribute = context.Attributes.FirstOrDefault(item => item.AttributeClass is { } type && IsInBaunNamespace(type));

        if (attribute is null || context.TargetSymbol.ContainingType is not { } containingType)
        {
            return null;
        }

        var (type, isWritable) = context.TargetSymbol switch
        {
            IFieldSymbol field => (field.Type, !field.IsStatic && !field.IsReadOnly && !field.IsConst),
            IPropertySymbol property => (property.Type, !property.IsStatic && property.SetMethod is { IsInitOnly: false }),
            _ => (null, false),
        };

        if (type is null)
        {
            return null;
        }

        var description = attribute.ConstructorArguments.FirstOrDefault().Value as string;
        var name = attribute.NamedArguments.FirstOrDefault(argument => argument.Key == "Name").Value.Value as string;

        return new ParameterMember(
            TypeKey: containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            TypeName: containingType.Name,
            MemberName: context.TargetSymbol.Name,
            Name: string.IsNullOrWhiteSpace(name) ? context.TargetSymbol.Name : name!,
            Description: description,
            Type: type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            TypeDisplayName: type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            IsWritable: isWritable,
            IsRequired: IsRequiredParameter(context.TargetNode, type),
            IsTypeSupported: IsSupportedParameterType(type),
            Location: LocationInfo.From(context.TargetSymbol.Locations.FirstOrDefault()));
    }

    /// <summary>
    /// Returns whether a parameter is required: a field or auto-property of a non-nullable reference type without an
    /// initial value, such as <c>Solution Solution;</c>. Without nullable reference types, no parameter is required.
    /// </summary>
    private static bool IsRequiredParameter(SyntaxNode declaration, ITypeSymbol type)
    {
        if (!type.IsReferenceType || type.NullableAnnotation != NullableAnnotation.NotAnnotated)
        {
            return false;
        }

        return declaration switch
        {
            VariableDeclaratorSyntax field => field.Initializer is null,
            PropertyDeclarationSyntax property => property.Initializer is null && IsAutoProperty(property),
            _ => false,
        };
    }

    private static bool IsAutoProperty(PropertyDeclarationSyntax property) =>
        property.ExpressionBody is null
        && property.AccessorList is { } accessors
        && accessors.Accessors.All(accessor => accessor.Body is null && accessor.ExpressionBody is null);

    /// <summary>
    /// Returns whether baun can convert text to <paramref name="type"/>: a supported scalar, or a one-dimensional array
    /// of a supported scalar that isn't nullable.
    /// </summary>
    private static bool IsSupportedParameterType(ITypeSymbol type) =>
        type is IArrayTypeSymbol { Rank: 1 } array
            ? !IsNullableValueType(array.ElementType) && IsSupportedScalar(array.ElementType)
            : IsSupportedScalar(type);

    private static bool IsNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    private static bool IsSupportedScalar(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            type = nullable.TypeArguments[0];
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_String:
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
                return true;
        }

        return type is INamedTypeSymbol { ContainingType: null } named
               && named.ContainingNamespace.ToDisplayString() switch
               {
                   "System" => named.Name is "Guid" or "Uri" or "TimeSpan",
                   "Baun" => named.Name is "AbsolutePath",
                   "Baun.DotNet" => named.Name is "Solution" or "Project",
                   _ => false,
               };
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
            MethodLocation: LocationInfo.From(method.Locations.FirstOrDefault()),
            Entry: entry,
            HasRootDirectory: !type.GetMembers(RootDirectoryName).IsEmpty,
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

    private static void Generate(SourceProductionContext context, IEnumerable<StepMethod> steps, IEnumerable<ParameterMember> parameters, bool needsEntryPoint)
    {
        var buildClasses = new List<StepMethod>();
        var stepTypes = new HashSet<string>(steps.Select(step => step.TypeKey));
        var parametersByType = parameters.GroupBy(parameter => parameter.TypeKey).ToDictionary(group => group.Key, group => group.ToList());

        foreach (var parameter in parameters.Where(parameter => !stepTypes.Contains(parameter.TypeKey)))
        {
            context.ReportDiagnostic(Diagnostic.Create(ParameterNotInBuildClass, parameter.Location?.ToLocation(), parameter.MemberName, parameter.TypeName));
        }

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

            foreach (var clash in stepNames.GroupBy(name => name, System.StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    StepNamesDifferOnlyInCase,
                    first.TypeLocation?.ToLocation(),
                    first.TypeName,
                    string.Join(", ", clash)));
            }

            foreach (var step in type.GroupBy(step => step.MethodName).Select(group => group.First()))
            {
                var command = CliCommands.FirstOrDefault(command => string.Equals(command, step.MethodName, System.StringComparison.OrdinalIgnoreCase));

                if (command is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        StepNameIsCliCommand,
                        step.MethodLocation?.ToLocation() ?? first.TypeLocation?.ToLocation(),
                        step.MethodName,
                        command));
                }
            }

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

            var validParameters = GetValidParameters(
                context,
                parametersByType.TryGetValue(type.Key, out var typeParameters) ? typeParameters : new List<ParameterMember>());

            context.AddSource(hintName, SourceText.From(GenerateSource(first, methods, validParameters), Encoding.UTF8));
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

    /// <summary>
    /// Reports parameters baun can't set or whose names conflict, and returns the others in declaration order.
    /// </summary>
    private static List<ParameterMember> GetValidParameters(SourceProductionContext context, List<ParameterMember> parameters)
    {
        var valid = new List<ParameterMember>();
        var names = new Dictionary<string, ParameterMember>();

        foreach (var parameter in parameters)
        {
            var location = parameter.Location?.ToLocation();

            if (!parameter.IsWritable)
            {
                context.ReportDiagnostic(Diagnostic.Create(ParameterMustBeWritable, location, parameter.MemberName));
                continue;
            }

            if (!parameter.IsTypeSupported)
            {
                context.ReportDiagnostic(Diagnostic.Create(ParameterTypeNotSupported, location, parameter.MemberName, parameter.TypeDisplayName));
                continue;
            }

            var normalized = Normalize(parameter.Name);

            if (System.Array.IndexOf(ReservedParameterNames, normalized) >= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(ParameterNameConflicts, location, parameter.MemberName, $"the built-in option '--{parameter.Name.ToLowerInvariant()}'"));
                continue;
            }

            if (normalized.StartsWith(OptionPrefix, System.StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(ParameterNameConflicts, location, parameter.MemberName, "the prefix '--baun-' of the built-in options"));
                continue;
            }

            if (HideableOptions.TryGetValue(normalized, out var option))
            {
                context.ReportDiagnostic(Diagnostic.Create(ParameterHidesOption, location, parameter.MemberName, option));
            }

            if (names.TryGetValue(normalized, out var other))
            {
                context.ReportDiagnostic(Diagnostic.Create(ParameterNameConflicts, location, parameter.MemberName, $"the parameter '{other.MemberName}'"));
                continue;
            }

            names.Add(normalized, parameter);
            valid.Add(parameter);
        }

        return valid;
    }

    /// <summary>
    /// Returns <paramref name="name"/> without '-' and '_', in upper case, as baun matches parameter names at run time.
    /// </summary>
    private static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            if (c != '-' && c != '_')
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
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

    private static string GenerateSource(StepMethod type, IReadOnlyList<(string Name, bool IsEntry)> methods, IReadOnlyList<ParameterMember> parameters)
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

        if (!type.HasRootDirectory)
        {
            source.AppendLine("    /// <summary>");
            source.AppendLine("    /// Gets the root directory of the repository: the nearest folder, from the current directory up through its");
            source.AppendLine("    /// parents, that contains a <c>.baun</c> folder. Without one, it's the current directory.");
            source.AppendLine("    /// </summary>");
            source.AppendLine($"    private static global::Baun.AbsolutePath {RootDirectoryName} => global::Baun.BaunBuild.RootDirectory;");
            source.AppendLine();
        }

        source.AppendLine("    global::System.Collections.Generic.IReadOnlyList<global::Baun.BaunStep> global::Baun.IBaunBuild.GetSteps() =>");
        source.AppendLine("        new global::Baun.BaunStep[]");
        source.AppendLine("        {");

        foreach (var (name, isEntry) in methods)
        {
            var entry = isEntry ? ", IsEntry: true" : string.Empty;
            source.AppendLine($"            new global::Baun.BaunStep(\"{name}\", {name}{entry}),");
        }

        source.AppendLine("        };");
        source.AppendLine();
        source.AppendLine("    global::System.Collections.Generic.IReadOnlyList<global::Baun.BaunParameter> global::Baun.IBaunBuild.GetParameters() =>");
        source.AppendLine("        new global::Baun.BaunParameter[]");
        source.AppendLine("        {");

        foreach (var parameter in parameters)
        {
            var name = SymbolDisplay.FormatLiteral(parameter.Name, quote: true);
            var description = parameter.Description is null ? "null" : SymbolDisplay.FormatLiteral(parameter.Description, quote: true);
            var isRequired = parameter.IsRequired ? "IsRequired: true, " : string.Empty;
            var memberName = SymbolDisplay.FormatLiteral(parameter.MemberName, quote: true);
            source.AppendLine($"            new global::Baun.BaunParameter({name}, typeof({parameter.Type}), {description}, value => this.{parameter.MemberName} = ({parameter.Type})value!, {isRequired}MemberName: {memberName}),");
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
        LocationInfo? MethodLocation,
        EntryInfo? Entry,
        bool HasRootDirectory,
        bool IsPartial,
        bool IsSupported,
        LocationInfo? TypeLocation);

    private sealed record ParameterMember(
        string TypeKey,
        string TypeName,
        string MemberName,
        string Name,
        string? Description,
        string Type,
        string TypeDisplayName,
        bool IsWritable,
        bool IsRequired,
        bool IsTypeSupported,
        LocationInfo? Location);

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
