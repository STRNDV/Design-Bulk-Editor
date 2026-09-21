using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DesignBulkEditor.Core.Models;

namespace DesignBulkEditor.Core.Services;

/// <summary>
/// Classifies a property's current JSON value so the UI can show a checkbox,
/// a number field, or a color picker instead of always a raw text box, and
/// decodes it into a plain, editable form (no surrounding JSON quotes for
/// strings - passing a quoted string back into <see cref="GlamourerDesign.TrySetPropertyValue"/>
/// would otherwise embed literal quote characters into the saved value).
/// </summary>
public static class PropertyValueInspector
{
    private static readonly Regex HexColorPattern = new("^[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$", RegexOptions.Compiled);

    public static PropertyValueKind Classify(string propertyName, string? rawJsonText)
    {
        if (string.IsNullOrEmpty(rawJsonText))
            return PropertyValueKind.Text;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(rawJsonText);
        }
        catch (Exception)
        {
            return PropertyValueKind.Text;
        }

        if (node is not JsonValue value)
            return PropertyValueKind.Text;

        if (value.TryGetValue<bool>(out _))
            return PropertyValueKind.Boolean;

        if (value.TryGetValue<long>(out _))
            return PropertyValueKind.Integer;

        if (value.TryGetValue<double>(out _))
            return PropertyValueKind.Float;

        if (value.TryGetValue<string>(out var text) && LooksLikeHexColor(propertyName, text))
            return PropertyValueKind.HexColor;

        return PropertyValueKind.Text;
    }

    /// <summary> The value in plain, editable form - unquoted for strings, unchanged otherwise. </summary>
    public static string DecodeForEditing(string? rawJsonText)
    {
        if (string.IsNullOrEmpty(rawJsonText))
            return string.Empty;

        try
        {
            var node = JsonNode.Parse(rawJsonText);
            return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : rawJsonText;
        }
        catch (Exception)
        {
            return rawJsonText;
        }
    }

    private static bool LooksLikeHexColor(string propertyName, string value)
        => propertyName.Contains("Color", StringComparison.OrdinalIgnoreCase) && HexColorPattern.IsMatch(value);
}
