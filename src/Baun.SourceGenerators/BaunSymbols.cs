using Microsoft.CodeAnalysis;

namespace Baun.SourceGenerators;

/// <summary>
/// Recognizes the types of the Baun library.
/// </summary>
internal static class BaunSymbols
{
    /// <summary>
    /// Returns whether <paramref name="type"/> is in the <c>Baun</c> namespace of the <c>Baun</c> assembly, so a type of
    /// the same name in the build or another library isn't mistaken for it.
    /// </summary>
    public static bool IsInBaunNamespace(INamedTypeSymbol type) =>
        type.ContainingNamespace is { Name: "Baun", ContainingNamespace.IsGlobalNamespace: true }
        && type.ContainingAssembly?.Name == "Baun";
}
