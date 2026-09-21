namespace DesignBulkEditor.Core.Models;

/// <summary> The result of loading a Glamourer <c>designs</c> folder: every design plus how they group by character. </summary>
public sealed record DesignLibrarySnapshot(
    string ConfigDirectory,
    IReadOnlyList<GlamourerDesign> Designs,
    IReadOnlyList<CharacterDesignGroup> Groups);
