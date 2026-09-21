namespace DesignBulkEditor.Core.Models;

public enum RenameTarget
{
    CharacterName,
    BaseName,
    Both,
}

/// <summary> A find-and-replace rule that can be applied across many designs' character/base names at once. </summary>
public sealed record RenamePattern(string Find, string Replace, RenameTarget Target, bool CaseSensitive = false);
