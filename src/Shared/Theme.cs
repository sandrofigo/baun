using Spectre.Console;

namespace Baun;

/// <summary>
/// The colors, symbols and line styles of baun's console output.
/// </summary>
/// <remarks>
/// Highlighted text, such as step names, commands and the selected prompt choice, uses the primary color. Secondary
/// text is faint. Status lines start with a symbol.
/// </remarks>
internal static class Theme
{
    /// <summary>
    /// The primary color, as markup.
    /// </summary>
    public const string PrimaryMarkup = "#6b81ff";

    public static readonly Color Primary = Color.FromHex(PrimaryMarkup);

    public static readonly Style PrimaryStyle = new(Primary);

    public static readonly Style FaintStyle = new(Color.Grey);

    /// <summary>
    /// Returns <paramref name="text"/> as markup in the primary color.
    /// </summary>
    public static string Accent(string text) => $"[{PrimaryMarkup}]{Markup.Escape(text)}[/]";

    /// <summary>
    /// Returns <paramref name="text"/> as faint markup.
    /// </summary>
    public static string Faint(string text) => $"[grey]{Markup.Escape(text)}[/]";

    public static string Check(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "✓" : "+";

    public static string Cross(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "✗" : "x";

    public static string Circle(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "○" : "-";

    public static string Chevron(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "›" : ">";

    public static string Marker(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "▸" : ">";

    /// <summary>
    /// Writes a line that reports something done, such as a created file.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="markup">The message, as markup.</param>
    public static void WriteSuccess(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"[green]{console.Check()}[/] {markup}");

    /// <summary>
    /// Writes an error line.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="markup">The message, as markup.</param>
    public static void WriteError(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"[red]{console.Cross()} error:[/] {markup}");

    /// <summary>
    /// Writes a warning line.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="markup">The message, as markup.</param>
    public static void WriteWarning(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"[yellow]! warning:[/] {markup}");

    /// <summary>
    /// The settings for writing exceptions, in the theme's colors.
    /// </summary>
    /// <remarks>
    /// Spectre.Console turns these styles into markup, so each one needs a color: <see cref="Style.Plain"/>
    /// and <see cref="Color.Default"/> would become empty markup and fail.
    /// </remarks>
    public static ExceptionSettings ExceptionSettings(ExceptionFormats format = ExceptionFormats.Default) => new()
    {
        Format = format,
        Style = new ExceptionStyle
        {
            Exception = new Style(Color.Red),
            Message = new Style(Color.Red),
            Method = PrimaryStyle,
            ParameterType = PrimaryStyle,
            ParameterName = FaintStyle,
            Parenthesis = FaintStyle,
            Path = FaintStyle,
            LineNumber = PrimaryStyle,
            NonEmphasized = FaintStyle,
            Dimmed = FaintStyle,
        },
    };
}
