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

    public static readonly Style SuccessStyle = new(Color.Green);

    public static readonly Style ErrorStyle = new(Color.Red);

    public static readonly Style WarningStyle = new(Color.Yellow);

    /// <summary>
    /// Returns <paramref name="text"/> as markup in the primary color.
    /// </summary>
    public static string Accent(string text) => Paint(PrimaryStyle, text);

    /// <summary>
    /// Returns <paramref name="text"/> as faint markup.
    /// </summary>
    public static string Faint(string text) => Paint(FaintStyle, text);

    /// <summary>
    /// Returns <paramref name="text"/> as markup in the color of success.
    /// </summary>
    public static string Success(string text) => Paint(SuccessStyle, text);

    /// <summary>
    /// Returns <paramref name="text"/> as markup in the color of errors.
    /// </summary>
    public static string Error(string text) => Paint(ErrorStyle, text);

    /// <summary>
    /// Returns <paramref name="text"/> as markup in the color of warnings.
    /// </summary>
    public static string Warning(string text) => Paint(WarningStyle, text);

    private static string Paint(Style style, string text) => $"[{style.ToMarkup()}]{Markup.Escape(text)}[/]";

    public static string Check(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "✓" : "+";

    public static string Cross(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "✗" : "x";

    public static string Chevron(this IAnsiConsole console) => console.Profile.Capabilities.Unicode ? "›" : ">";

    /// <summary>
    /// Returns a line to write under <paramref name="text"/>, as long as the text but at least
    /// <paramref name="minWidth"/> characters.
    /// </summary>
    public static string Underline(this IAnsiConsole console, string text, int minWidth = 20) =>
        new(console.Profile.Capabilities.Unicode ? '─' : '-', Math.Max(text.GetCellWidth(), minWidth));

    /// <summary>
    /// Writes a line that reports something done, such as a created file.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="markup">The message, as markup.</param>
    public static void WriteSuccess(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"{Success(console.Check())} {markup}");

    /// <summary>
    /// Writes an error line.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="markup">The message, as markup.</param>
    public static void WriteError(this IAnsiConsole console, string markup) =>
        console.MarkupLine($"{Error($"{console.Cross()} error:")} {markup}");

    /// <summary>
    /// Writes a warning line.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="markup">The message, as markup.</param>
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
            Exception = ErrorStyle,
            Message = ErrorStyle,
            Method = PrimaryStyle,
            ParameterType = FaintStyle,
            ParameterName = FaintStyle,
            Parenthesis = FaintStyle,
            Path = FaintStyle,
            LineNumber = PrimaryStyle,
            NonEmphasized = FaintStyle,
            Dimmed = FaintStyle,
        },
    };
}
