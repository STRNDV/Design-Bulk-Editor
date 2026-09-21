namespace DesignBulkEditor.Core.Models;

/// <summary>
/// A single property change staged to be applied to one or more target designs.
/// Multiple instances can be held at once, so switching between e.g. hairstyle
/// and skin color never discards the other one.
/// </summary>
public sealed record PendingPropertyEdit(string SectionName, string EntryName, string PropertyName, string NewValueRaw)
{
    public string Label => $"{SectionName} / {EntryName} / {PropertyName}";
}
