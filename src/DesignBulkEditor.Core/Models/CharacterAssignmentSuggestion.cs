namespace DesignBulkEditor.Core.Models;

/// <summary> A design-to-character assignment suggested by some heuristic. Never applied as fact without confirmation. </summary>
public sealed record CharacterAssignmentSuggestion(string DesignIdentifier, string CharacterName, string Source)
{
    /// <summary> From Glamourer's own Automation config - the user explicitly set this up in Glamourer itself. </summary>
    public const string SourceGlamourerAutomation = "GlamourerAutomation";

    /// <summary> From the "(Character) Design" naming convention in the design's Name field. </summary>
    public const string SourceNamePrefix = "NamePrefix";

    /// <summary> From the top-level segment of the design's FileSystemFolder. </summary>
    public const string SourceFileSystemFolder = "FileSystemFolder";
}
