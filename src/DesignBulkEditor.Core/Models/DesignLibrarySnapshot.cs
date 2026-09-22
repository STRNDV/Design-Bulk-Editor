namespace DesignBulkEditor.Core.Models;

/// <summary>
/// The result of loading a Glamourer <c>designs</c> folder: every design that
/// loaded successfully, how they group by character, and any files that
/// couldn't be read (e.g. malformed JSON) - one bad file never blocks the
/// rest of the library from loading.
/// </summary>
public sealed record DesignLibrarySnapshot(
    string ConfigDirectory,
    IReadOnlyList<GlamourerDesign> Designs,
    IReadOnlyList<CharacterDesignGroup> Groups,
    IReadOnlyList<DesignLoadIssue> Issues);
