using Spectre.Console;

namespace Baun;

/// <summary>
/// The colors, symbols and line styles of baun's console output.
/// </summary>
/// <remarks>
/// Highlighted text, such as step names, commands and the selected prompt choice, uses the primary color. Secondary
/// text is faint. Status lines start with a symbol, which falls back to ASCII on consoles without Unicode.
/// </remarks>
internal static class Theme
{
    public static readonly Style PrimaryStyle = new(Color.FromHex("#6b81ff"));
    public static readonly Style FaintStyle = new(Color.Grey);
    private static readonly Style _successStyle = new(Color.Green);
    private static readonly Style _errorStyle = new(Color.Red);
    private static readonly Style _warningStyle = new(Color.Yellow);

    public static string Accent(string text) => Paint(PrimaryStyle, text);
    public static string Faint(string text) => Paint(FaintStyle, text);
    public static string Success(string text) => Paint(_successStyle, text);
    public static string Error(string text) => Paint(_errorStyle, text);
    public static string Warning(string text) => Paint(_warningStyle, text);

    public static string Check(this IAnsiConsole console) => console.SupportsUnicode() ? "✓" : "+";
    public static string Cross(this IAnsiConsole console) => console.SupportsUnicode() ? "✗" : "x";
    public static string Chevron(this IAnsiConsole console) => console.SupportsUnicode() ? "›" : ">";

    /// <summary>
    /// Returns a line to write under <paramref name="text"/>, as long as the text but at least
    /// <paramref name="minWidth"/> characters.
    /// </summary>
    public static string Underline(this IAnsiConsole console, string text, int minWidth = 20) =>
        new(console.SupportsUnicode() ? '─' : '-', Math.Max(text.GetCellWidth(), minWidth));

    public static void WriteSuccess(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"{Success(console.Check())} {markup}");

    public static void WriteError(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"{Error($"{console.Cross()} error:")} {markup}");

    public static void WriteWarning(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"{Warning("! warning:")} {markup}");

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
            Exception = _errorStyle,
            Message = _errorStyle,
            Method = PrimaryStyle,
            LineNumber = PrimaryStyle,
            ParameterType = FaintStyle,
            ParameterName = FaintStyle,
            Parenthesis = FaintStyle,
            Path = FaintStyle,
            NonEmphasized = FaintStyle,
            Dimmed = FaintStyle
        }
    };

    private static string Paint(Style style, string text) => $"[{style.ToMarkup()}]{Markup.Escape(text)}[/]";

    private static bool SupportsUnicode(this IAnsiConsole console) => console.Profile.Capabilities.Unicode;
}
