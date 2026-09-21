namespace DesignBulkEditor.Core.Models;

/// <summary> How a property's current value should be edited: a checkbox, a number field, a color picker, or plain text. </summary>
public enum PropertyValueKind
{
    Boolean,
    Integer,
    Float,
    HexColor,
    Text,
}
