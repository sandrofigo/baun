using System.Collections.Immutable;
using Baun.SourceGenerators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

    private static (ImmutableArray<Diagnostic> Diagnostics, string[] Sources) RunGenerator(
        string source,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Step).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            [CSharpSyntaxTree.ParseText("global using Baun;"), CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(outputKind));

        var driver = CSharpGeneratorDriver.Create(new StepGenerator()).RunGenerators(compilation);
        var result = driver.GetRunResult();

        return (result.Diagnostics, result.GeneratedTrees.Select(tree => tree.ToString()).ToArray());
    }
}

internal sealed partial class SplitBuild
{
    public Step Compile() => _ => _;
}

internal sealed partial class SplitBuild
{
    public Step Deploy() => _ => _
        .DependsOn(Compile);
}
