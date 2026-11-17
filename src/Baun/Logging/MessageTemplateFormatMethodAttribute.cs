namespace Baun;

/// <summary>
/// Marks a method whose parameter <see cref="MessageTemplateParameterName"/> is a message template, so IDEs and
/// analyzers that know Serilog's attribute of the same name check the template against its property values.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class MessageTemplateFormatMethodAttribute(string messageTemplateParameterName) : Attribute
{
    public string MessageTemplateParameterName { get; } = messageTemplateParameterName;
}
