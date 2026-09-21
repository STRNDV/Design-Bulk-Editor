using GlamourerBulkEditor.Core.Models;

namespace GlamourerBulkEditor.Core.Services;

/// <summary>
/// Suggests which character a design belongs to from several independent
/// signals, since not every user names their designs "(Character) Design".
/// Suggestions are never applied as fact - <c>DesignLibrary</c> pre-fills
/// them, but a confirmed manual assignment in <see cref="CharacterAssignmentStore"/>
/// always takes priority.
///
/// Priority when several signals disagree: Glamourer's own Automation config
/// first (the user explicitly configured that in Glamourer itself), then the
/// "(Character) Design" name-prefix convention, then the design's
/// FileSystemFolder (the weakest signal - folders are often organised by
/// outfit type rather than character).
/// </summary>
public sealed class CharacterAssignmentSuggester
{
    public IReadOnlyList<CharacterAssignmentSuggestion> SuggestAll(GlamourerDesign design, IReadOnlyDictionary<string, string> automationMap)
    {
        var suggestions = new List<CharacterAssignmentSuggestion>();

        if (automationMap.TryGetValue(design.Identifier, out var automationCharacter) && !string.IsNullOrWhiteSpace(automationCharacter))
            suggestions.Add(new CharacterAssignmentSuggestion(design.Identifier, automationCharacter, CharacterAssignmentSuggestion.SourceGlamourerAutomation));

        if (!string.IsNullOrWhiteSpace(design.CharacterName))
            suggestions.Add(new CharacterAssignmentSuggestion(design.Identifier, design.CharacterName, CharacterAssignmentSuggestion.SourceNamePrefix));

        if (FirstFolderSegment(design.FileSystemFolder) is { } folderCharacter)
            suggestions.Add(new CharacterAssignmentSuggestion(design.Identifier, folderCharacter, CharacterAssignmentSuggestion.SourceFileSystemFolder));

        return suggestions;
    }

    public CharacterAssignmentSuggestion? SuggestBest(GlamourerDesign design, IReadOnlyDictionary<string, string> automationMap)
        => SuggestAll(design, automationMap).FirstOrDefault();

    private static string? FirstFolderSegment(string fileSystemFolder)
    {
        if (string.IsNullOrWhiteSpace(fileSystemFolder))
            return null;

        var segment = fileSystemFolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(segment) ? null : segment.Trim();
    }
}
