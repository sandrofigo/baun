using System.Globalization;
using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Baun.Logging;

/// <summary>
/// Writes log events to a console, one line each, as <c>HH:mm:ss LVL message</c> in the theme's colors.
/// </summary>
/// <remarks>
/// Property values are written in the primary color. Strings are written without quotes, and values are formatted in the
/// invariant culture. Further lines of a message are indented to the column the message starts in.
/// </remarks>
internal sealed class AnsiConsoleSink(IAnsiConsole console) : ILogEventSink
{
    /// <summary>
    /// The width of <c>HH:mm:ss LVL </c>, the prefix of each line.
    /// </summary>
    private const int PrefixWidth = 13;

    private static readonly string Indent = new(' ', PrefixWidth);

    private readonly Lock gate = new();

    public void Emit(LogEvent logEvent)
    {
        string timestamp = logEvent.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        string line = $"{Theme.Faint(timestamp)} {Level(logEvent.Level)} {RenderMessage(logEvent)}";

        lock (gate)
        {
            console.Write(new Unwrapped(new Markup(line.ReplaceLineEndings("\n" + Indent))));
            console.WriteLine();

            if (logEvent.Exception is { } exception)
            {
                console.WriteException(exception, Theme.ExceptionSettings(ExceptionFormats.ShortenPaths));
            }
        }
    }

    private static string Level(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => Theme.Faint("VRB"),
        LogEventLevel.Debug => Theme.Faint("DBG"),
        LogEventLevel.Information => Theme.Accent("INF"),
        LogEventLevel.Warning => "[yellow]WRN[/]",
        LogEventLevel.Error => "[red]ERR[/]",
        _ => "[red]FTL[/]",
    };

    /// <summary>
    /// Returns the message of <paramref name="logEvent"/> as markup.
    /// </summary>
    private string RenderMessage(LogEvent logEvent)
    {
        var markup = new StringBuilder();

        foreach (MessageTemplateToken token in logEvent.MessageTemplate.Tokens)
        {
            if (token is PropertyToken property)
            {
                markup.Append(logEvent.Properties.TryGetValue(property.PropertyName, out LogEventPropertyValue? value)
                    ? value is ScalarValue { Value: CommandLine command }
                        ? $"{Theme.Faint(console.Chevron())} {Theme.Accent(command.Text)}"
                        : Theme.Accent(Align(Render(value, property.Format), property.Alignment))
                    : Markup.Escape(property.ToString() ?? string.Empty));
            }
            else
            {
                markup.Append(Markup.Escape(token is TextToken text ? text.Text : token.ToString() ?? string.Empty));
            }
        }

        return markup.ToString();
    }

    private static string Render(LogEventPropertyValue value, string? format) => value switch
    {
        ScalarValue { Value: string text } => text,
        ScalarValue { Value: IFormattable formattable } => formattable.ToString(format, CultureInfo.InvariantCulture),
        ScalarValue { Value: null } => "null",
        ScalarValue scalar => scalar.Value.ToString() ?? string.Empty,
        _ => value.ToString(format, CultureInfo.InvariantCulture),
    };

    private static string Align(string text, Alignment? alignment) =>
        alignment is not { } value ? text
        : value.Direction == AlignmentDirection.Left ? text.PadRight(value.Width)
        : text.PadLeft(value.Width);

    /// <summary>
    /// Renders <paramref name="inner"/> without wrapping it at the console width, so a long line, such as a path in a
    /// tool's output, stays one line. Terminals wrap it themselves, and CI logs, whose width is a guess, don't break it.
    /// </summary>
    private sealed class Unwrapped(IRenderable inner) : IRenderable
    {
        private const int Unlimited = 1_000_000;

        public Measurement Measure(RenderOptions options, int maxWidth) => inner.Measure(options, Unlimited);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => inner.Render(options, Unlimited);
    }
}
