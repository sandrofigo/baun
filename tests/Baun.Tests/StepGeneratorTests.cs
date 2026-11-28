using System.Collections.Immutable;
using Baun.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Baun.Tests;

public class StepGeneratorTests
{
    [Fact]
    public void Registers_every_step_under_its_method_name_in_declaration_order()
    {
        var steps = ((IBaunBuild)new SampleBuild()).GetSteps();

        Assert.Equal(["Clean", "Restore", "Compile", "Test", "Pack", "All"], steps.Select(step => step.Name));
    }

    [Fact]
    public void Marks_the_entry_step()
    {
        var steps = ((IBaunBuild)new SampleBuild()).GetSteps();

        Assert.Equal(["Test"], steps.Where(step => step.IsEntry).Select(step => step.Name));
    }

    [Theory]
    [InlineData("nameof(Configuration)", "Configuration")]
    [InlineData("nameof(Helper)", "Helper")]
    [InlineData("\"Tset\"", "Tset")]
    public void Reports_an_error_for_an_entry_that_is_not_a_step(string argument, string name)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            [Entry({{argument}})]
            partial class Build
            {
                string Configuration = "Release";

                Step Compile() => _ => _;

                static Step Helper() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains($"'{name}' is not a step", diagnostic.GetMessage());
        Assert.Equal(0, diagnostic.Location.GetLineSpan().StartLinePosition.Line); // reported on the [Entry(...)] line
        Assert.DoesNotContain("IsEntry", Assert.Single(sources)); // still completed, so this is the only error
    }

    [Fact]
    public void Leaves_a_misspelled_nameof_entry_to_the_compiler()
    {
        var (diagnostics, sources) = RunGenerator("""
            [Entry(nameof(Tset))]
            partial class Build
            {
                Step Test() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Single(sources);
    }

    [Fact]
    public void Generates_no_entry_step_without_an_entry_attribute()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain("IsEntry", Assert.Single(sources));
    }

    [Fact]
    public void Registers_steps_from_all_parts_of_a_partial_class()
    {
        var steps = ((IBaunBuild)new SplitBuild()).GetSteps();

        Assert.Equal(["Compile", "Deploy"], steps.Select(step => step.Name));
    }

    [Fact]
    public void Ignores_static_step_methods()
    {
        var steps = ((IBaunBuild)new ForeignDependencyBuild()).GetSteps();

        Assert.Equal(["Main"], steps.Select(step => step.Name));
    }

    [Fact]
    public void Generates_the_build_class_in_the_global_namespace()
    {
        var (diagnostics, sources) = RunGenerator("""
            [Entry(nameof(Test))]
            partial class Build
            {
                Step Compile() => _ => _;

                Step Test() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        var source = Assert.Single(sources);
        Assert.DoesNotContain("namespace", source);
        Assert.Contains("partial class Build : global::Baun.IBaunBuild", source);
        Assert.Contains("new global::Baun.BaunStep(\"Compile\", Compile),", source);
        Assert.Contains("new global::Baun.BaunStep(\"Test\", Test, IsEntry: true),", source);
    }

    [Fact]
    public void Generates_the_build_class_in_its_namespace()
    {
        var (diagnostics, sources) = RunGenerator("""
            namespace My.Builds;

            partial class Build
            {
                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("namespace My.Builds;", Assert.Single(sources));
    }

    [Fact]
    public void Reports_an_error_for_a_build_class_that_is_not_partial()
    {
        var (diagnostics, sources) = RunGenerator("""
            class Build
            {
                Step Compile() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Empty(sources);
    }

    [Fact]
    public void Reports_an_error_for_a_nested_build_class()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Outer
            {
                partial class Build
                {
                    Step Compile() => _ => _;
                }
            }
            """);

        Assert.Equal("BAUN002", Assert.Single(diagnostics).Id);
        Assert.Empty(sources);
    }

    [Fact]
    public void Ignores_methods_returning_another_type_named_step()
    {
        var (diagnostics, sources) = RunGenerator("""
            namespace Other
            {
                delegate void Step();

                class Build
                {
                    Step Compile() => () => { };
                }
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Empty(sources);
    }

    [Fact]
    public void Reports_an_error_for_step_names_that_differ_only_in_case()
    {
        var (diagnostics, _) = RunGenerator("""
            partial class Build
            {
                Step Test() => _ => _;

                Step test() => _ => _;

                Step Compile() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("differ only in case: Test, test.", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("Init")]
    [InlineData("init")]
    public void Warns_about_a_step_with_the_name_of_a_tool_command(string name)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            partial class Build
            {
                Step {{name}}() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN007", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal($"The step '{name}' has the same name as the baun tool's 'init' command, so 'baun init' runs the command. Run the step with 'baun --step {name}'.", diagnostic.GetMessage());
        Assert.Contains(sources, source => source.Contains($"new global::Baun.BaunStep(\"{name}\", {name}),"));
    }

    [Fact]
    public void Adds_an_entry_point_that_runs_the_build_class()
    {
        var (diagnostics, sources) = RunGenerator("""
            namespace My.Builds;

            [Entry(nameof(Test))]
            partial class Build
            {
                Step Test() => _ => _;
            }
            """, OutputKind.ConsoleApplication);

        Assert.Empty(diagnostics);
        var entryPoint = Assert.Single(sources, source => source.Contains("class BaunEntryPoint"));
        Assert.Contains("private static global::System.Threading.Tasks.Task Main() =>", entryPoint);
        Assert.Contains("global::Baun.BaunBuild.RunAsync<global::My.Builds.Build>();", entryPoint);
    }

    [Theory]
    [InlineData("await BaunBuild.RunAsync<Build>();")]
    [InlineData("class Program { static Task Main() => BaunBuild.RunAsync<Build>(); }")]
    public void Adds_no_entry_point_when_the_build_has_one(string entryPoint)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            using System.Threading.Tasks;

            {{entryPoint}}

            partial class Build
            {
                Step Test() => _ => _;
            }
            """, OutputKind.ConsoleApplication);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain(sources, source => source.Contains("class BaunEntryPoint"));
    }

    [Fact]
    public void Adds_no_entry_point_to_a_library()
    {
        var (_, sources) = RunGenerator("""
            partial class Build
            {
                Step Test() => _ => _;
            }
            """);

        Assert.DoesNotContain(sources, source => source.Contains("class BaunEntryPoint"));
    }

    [Fact]
    public void Reports_an_error_for_more_than_one_build_class_without_an_entry_point()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                Step Test() => _ => _;
            }

            partial class Deploy
            {
                Step Release() => _ => _;
            }
            """, OutputKind.ConsoleApplication);

        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Equal("BAUN005", diagnostic.Id));
        Assert.Contains("Build, Deploy", diagnostics[0].GetMessage());
        Assert.DoesNotContain(sources, source => source.Contains("class BaunEntryPoint"));
    }

    [Fact]
    public void Registers_parameters_with_their_names_types_and_descriptions()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                [Parameter("Configuration to build")]
                string Configuration = "Debug";

                [Parameter(Name = "api-key")]
                string? ApiKey { get; set; }

                [Parameter]
                int? Retries { get; private set; }

                [Parameter("Say \"hi\"")]
                string[] Projects = [];

                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        var source = Assert.Single(sources);
        Assert.Contains("new global::Baun.BaunParameter(\"Configuration\", typeof(string), \"Configuration to build\", value => this.Configuration = (string)value!, MemberName: \"Configuration\"),", source);
        Assert.Contains("new global::Baun.BaunParameter(\"api-key\", typeof(string), null, value => this.ApiKey = (string)value!, MemberName: \"ApiKey\"),", source);
        Assert.Contains("new global::Baun.BaunParameter(\"Retries\", typeof(int?), null, value => this.Retries = (int?)value!, MemberName: \"Retries\"),", source);
        Assert.Contains("new global::Baun.BaunParameter(\"Projects\", typeof(string[]), \"Say \\\"hi\\\"\", value => this.Projects = (string[])value!, MemberName: \"Projects\"),", source);
    }

    [Fact]
    public void Generates_no_parameters_for_a_build_without_them()
    {
        var (_, sources) = RunGenerator("""
            partial class Build
            {
                Step Compile() => _ => _;
            }
            """);

        Assert.Contains("GetParameters() =>\n        new global::Baun.BaunParameter[]\n        {\n        };", Assert.Single(sources).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Sets_readonly_fields_through_a_reference()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                [Parameter] readonly string Configuration = "Debug";

                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("value => global::System.Runtime.CompilerServices.Unsafe.AsRef(in this.Configuration) = (string)value!,", Assert.Single(sources));
    }

    [Theory]
    [InlineData("[Parameter] const string Configuration = \"\";")]
    [InlineData("[Parameter] static string Configuration = \"\";")]
    [InlineData("[Parameter] string Configuration => \"\";")]
    [InlineData("[Parameter] string Configuration { get; init; } = \"\";")]
    public void Reports_an_error_for_a_parameter_that_is_not_writable(string member)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            partial class Build
            {
                {{member}}

                Step Compile() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN008", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.DoesNotContain("this.Configuration", Assert.Single(sources));
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<string>")]
    [InlineData("int?[]")]
    [InlineData("string[][]")]
    [InlineData("int[,]")]
    [InlineData("object")]
    [InlineData("System.DateTime")]
    [InlineData("Baun.Tools.DotNet.SolutionFormatException")]
    public void Reports_an_error_for_a_parameter_of_an_unsupported_type(string type)
    {
        var (diagnostics, _) = RunGenerator($$"""
            partial class Build
            {
                [Parameter] {{type}} Value = default!;

                Step Compile() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN009", diagnostic.Id);
        Assert.Contains("The parameter 'Value' has the type", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("char")]
    [InlineData("byte")]
    [InlineData("ulong")]
    [InlineData("decimal")]
    [InlineData("double?")]
    [InlineData("System.Guid")]
    [InlineData("System.Uri")]
    [InlineData("System.TimeSpan?")]
    [InlineData("System.DayOfWeek")]
    [InlineData("System.DayOfWeek[]")]
    [InlineData("int[]")]
    [InlineData("AbsolutePath?")]
    [InlineData("Baun.Tools.DotNet.Solution")]
    [InlineData("Baun.Tools.DotNet.Solution?")]
    [InlineData("Baun.Tools.DotNet.Project")]
    [InlineData("Baun.Tools.DotNet.Project[]")]
    public void Accepts_parameters_of_supported_types(string type)
    {
        var (diagnostics, _) = RunGenerator($$"""
            partial class Build
            {
                [Parameter] {{type}} Value = default!;

                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Warns_about_a_parameter_in_a_class_without_steps()
    {
        var (diagnostics, _) = RunGenerator("""
            class Settings
            {
                [Parameter] string Configuration = "";
            }

            partial class Build
            {
                Step Compile() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN010", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'Configuration' is in 'Settings', which has no steps", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("[Parameter] bool Help;", "the built-in option '--help'")]
    [InlineData("[Parameter(Name = \"v\")] bool Verbose;", "the built-in option '--v'")]
    [InlineData("[Parameter] bool Version;", "the built-in option '--version'")]
    [InlineData("[Parameter] bool BaunPlan;", "the prefix '--baun-' of the built-in options")]
    [InlineData("[Parameter(Name = \"baun-mode\")] string Mode = \"\";", "the prefix '--baun-' of the built-in options")]
    [InlineData("[Parameter] string ApiKey = \"\"; [Parameter] string Api_Key = \"\";", "the parameter 'ApiKey'")]
    [InlineData("[Parameter] string ApiKey = \"\"; [Parameter(Name = \"api-key\")] string Key = \"\";", "the parameter 'ApiKey'")]
    public void Reports_an_error_for_conflicting_parameter_names(string members, string conflict)
    {
        var (diagnostics, _) = RunGenerator($$"""
            partial class Build
            {
                {{members}}

                Step Compile() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN011", diagnostic.Id);
        Assert.Contains($"conflicts with {conflict}.", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("[Parameter] bool Plan;", "plan")]
    [InlineData("[Parameter] string Step = \"\";", "step")]
    [InlineData("[Parameter(Name = \"VERBOSITY\")] string Level = \"\";", "verbosity")]
    public void Reports_a_warning_for_a_parameter_that_takes_over_a_built_in_option(string members, string option)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            partial class Build
            {
                {{members}}

                Step Compile() => _ => _;
            }
            """);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("BAUN012", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains($"takes over the built-in option '--{option}', which stays available as '--baun-{option}'", diagnostic.GetMessage());
        Assert.Contains(sources, source => source.Contains("new global::Baun.BaunParameter(", StringComparison.Ordinal));
    }

    [Fact]
    public void Adds_a_root_directory_property_to_the_build_class()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                Step Compile() => _ => _
                    .Executes(() => System.Console.WriteLine(RootDirectory));
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("private static global::Baun.AbsolutePath RootDirectory => global::Baun.BaunBuild.RootDirectory;", Assert.Single(sources));
    }

    [Theory]
    [InlineData("string RootDirectory = \"repo\";")]
    [InlineData("static string RootDirectory => \"repo\";")]
    [InlineData("void RootDirectory() { }")]
    public void Adds_no_root_directory_property_when_the_build_class_has_one(string member)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            partial class Build
            {
                {{member}}

                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain("RootDirectory", Assert.Single(sources));
    }

    [Fact]
    public void The_root_directory_property_returns_the_root_directory_of_the_build()
    {
        Assert.Equal(BaunBuild.RootDirectory, SplitBuild.Root);
    }

    [Fact]
    public void Adds_a_temp_directory_property_to_the_build_class()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                Step Compile() => _ => _
                    .Executes(() => System.Console.WriteLine(TempDirectory));
            }
            """);

        Assert.Empty(diagnostics);
        Assert.Contains("private static global::Baun.AbsolutePath TempDirectory => global::Baun.BaunBuild.TempDirectory;", Assert.Single(sources));
    }

    [Theory]
    [InlineData("string TempDirectory = \"temp\";")]
    [InlineData("static string TempDirectory => \"temp\";")]
    [InlineData("void TempDirectory() { }")]
    public void Adds_no_temp_directory_property_when_the_build_class_has_one(string member)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            partial class Build
            {
                {{member}}

                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain("TempDirectory", Assert.Single(sources));
    }

    [Fact]
    public void The_temp_directory_property_returns_the_temp_directory_of_the_build()
    {
        Assert.Equal(BaunBuild.TempDirectory, SplitBuild.Temp);
    }

    [Fact]
    public void Adds_local_and_ci_build_properties_to_the_build_class()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                Step Compile() => _ => _
                    .OnlyWhen(() => IsLocalBuild)
                    .Executes(() => System.Console.WriteLine(IsCIBuild));
            }
            """);

        Assert.Empty(diagnostics);
        var source = Assert.Single(sources);
        Assert.Contains("private static bool IsLocalBuild => !global::Baun.CI.CIProvider.IsRunning;", source);
        Assert.Contains("private static bool IsCIBuild => global::Baun.CI.CIProvider.IsRunning;", source);
    }

    [Theory]
    [InlineData("IsLocalBuild")]
    [InlineData("IsCIBuild")]
    public void Adds_no_local_or_ci_build_property_when_the_build_class_has_one(string name)
    {
        var (diagnostics, sources) = RunGenerator($$"""
            partial class Build
            {
                bool {{name}} => true;

                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        Assert.DoesNotContain($" {name} =>", Assert.Single(sources));
    }

    [Fact]
    public void The_local_and_ci_build_properties_return_whether_the_build_runs_on_a_ci_provider()
    {
        Assert.Equal(Baun.CI.CIProvider.IsRunning, SplitBuild.CI);
        Assert.Equal(!Baun.CI.CIProvider.IsRunning, SplitBuild.Local);
    }

    [Fact]
    public void Registers_parameters_from_all_parts_of_a_partial_class()
    {
        var parameters = ((IBaunBuild)new SplitBuild()).GetParameters();

        Assert.Equal(["Configuration", "Environment"], parameters.Select(parameter => parameter.Name));
    }

    [Fact]
    public void Infers_required_parameters_from_nullability()
    {
        var (diagnostics, sources) = RunGenerator("""
            partial class Build
            {
                [Parameter] string Required;
                [Parameter] AbsolutePath RequiredProperty { get; set; }
                [Parameter] string[] RequiredArray;
                [Parameter] string WithInitialValue = "Debug";
                [Parameter] string WithInitialProperty { get; set; } = "Debug";
                [Parameter] string? Nullable;
                [Parameter] int ValueType;
                [Parameter] string Computed { get => ""; set { } }

                Step Compile() => _ => _;
            }
            """);

        Assert.Empty(diagnostics);
        var source = Assert.Single(sources);
        string[] required = ["Required", "RequiredProperty", "RequiredArray"];
        string[] optional = ["WithInitialValue", "WithInitialProperty", "Nullable", "ValueType", "Computed"];
        Assert.All(required, name => Assert.Contains($"IsRequired: true, MemberName: \"{name}\")", source));
        Assert.All(optional, name => Assert.Contains($"value!, MemberName: \"{name}\")", source));
    }

    [Fact]
    public void Infers_no_required_parameters_without_nullable_reference_types()
    {
        var (_, sources) = RunGenerator("""
            partial class Build
            {
                [Parameter] string Configuration;

                Step Compile() => _ => _;
            }
            """,
            nullable: NullableContextOptions.Disable);

        Assert.DoesNotContain("IsRequired", Assert.Single(sources));
    }

    [Fact]
    public async Task Suppresses_the_nullable_warning_for_parameters_without_an_initial_value()
    {
        var diagnostics = await CompileWithSuppressorAsync("""
            partial class Build
            {
                [Parameter] string Configuration;
                [Parameter] AbsolutePath Output { get; set; }
                string NotAParameter;

                Step Compile() => _ => _.Executes(() => System.Console.WriteLine($"{Configuration} {Output} {NotAParameter}"));
            }
            """);

        var warning = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "CS8618");
        Assert.Contains("'NotAParameter'", warning.GetMessage());
    }

    [Fact]
    public async Task Suppresses_the_unassigned_warning_for_readonly_parameters()
    {
        var diagnostics = await CompileWithSuppressorAsync("""
            partial class Build
            {
                [Parameter] readonly string Configuration;
                [Parameter] readonly int Retries;
                readonly string? NotAParameter;

                Step Compile() => _ => _.Executes(() => System.Console.WriteLine($"{Configuration} {Retries} {NotAParameter}"));
            }
            """);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "CS8618");
        var warning = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "CS0649");
        Assert.Contains("'Build.NotAParameter'", warning.GetMessage());
    }

    [Fact]
    public void Registers_the_inferred_required_parameters_of_a_compiled_build()
    {
        var parameters = ((IBaunBuild)new RequiredParameterBuild()).GetParameters();

        Assert.Equal(["Configuration", "Output"], parameters.Where(parameter => parameter.IsRequired).Select(parameter => parameter.Name));
    }

    private static async Task<ImmutableArray<Diagnostic>> CompileWithSuppressorAsync(string source)
    {
        var compilation = CreateCompilation(source, OutputKind.DynamicallyLinkedLibrary, NullableContextOptions.Enable);

        CSharpGeneratorDriver.Create(new StepGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var generated, out _);

        return await generated
            .WithAnalyzers([new ParameterNullabilitySuppressor()])
            .GetAllDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source, OutputKind outputKind, NullableContextOptions nullable)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Step).Assembly.Location));

        return CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText("global using Baun;"), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(outputKind, nullableContextOptions: nullable));
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string[] Sources) RunGenerator(
        string source,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        NullableContextOptions nullable = NullableContextOptions.Enable)
    {
        var compilation = CreateCompilation(source, outputKind, nullable);

        var driver = CSharpGeneratorDriver.Create(new StepGenerator()).RunGenerators(compilation);
        var result = driver.GetRunResult();

        return (result.Diagnostics, result.GeneratedTrees.Select(tree => tree.ToString()).ToArray());
    }
}

internal sealed partial class SplitBuild
{
    [Parameter]
    public string Configuration = "Debug";

    public static AbsolutePath Root => RootDirectory;

    public static AbsolutePath Temp => TempDirectory;

    public static bool Local => IsLocalBuild;

    public static bool CI => IsCIBuild;

    public Step Compile() => _ => _;
}

internal sealed partial class SplitBuild
{
    [Parameter]
    public string Environment = "staging";

    public Step Deploy() => _ => _
        .DependsOn(Compile);
}
