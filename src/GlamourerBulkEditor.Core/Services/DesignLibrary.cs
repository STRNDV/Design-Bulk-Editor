using GlamourerBulkEditor.Core.Models;

namespace GlamourerBulkEditor.Core.Services;

/// <summary>
/// Loads and groups the design library, and applies staged property edits to
/// designs' working copies in memory. Staging never touches disk - only
/// <see cref="SaveAsync"/>/<see cref="SaveManyAsync"/> do, and always through
/// <see cref="DesignRepository"/>'s backup-and-atomic-write path.
///
/// Character grouping does not assume every design follows the "(Character)
/// Design" naming convention: each design's <see cref="GlamourerDesign.AssignedCharacter"/>
/// is resolved from a confirmed manual assignment first, falling back to the
/// best available heuristic suggestion (see <see cref="CharacterAssignmentSuggester"/>).
/// </summary>
public sealed class DesignLibrary
{
    private readonly DesignRepository _repository;
    private readonly AutomationConfigReader _automationReader;
    private readonly CharacterAssignmentSuggester _suggester;
    private readonly CharacterAssignmentStore _assignmentStore;

    public DesignLibrary(
        DesignRepository repository,
        AutomationConfigReader automationReader,
        CharacterAssignmentSuggester suggester,
        CharacterAssignmentStore assignmentStore)
    {
        _repository = repository;
        _automationReader = automationReader;
        _suggester = suggester;
        _assignmentStore = assignmentStore;
    }

    public async Task<DesignLibrarySnapshot> LoadAsync(string configDirectory, CancellationToken cancellationToken = default)
    {
        var designs = await _repository.LoadAsync(configDirectory, cancellationToken);

        await _assignmentStore.LoadAsync(cancellationToken);
        var automationMap = await _automationReader.ReadDesignCharacterMapAsync(configDirectory, cancellationToken);

        foreach (var design in designs)
        {
            design.AssignedCharacter = _assignmentStore.Get(design.Identifier)
                ?? _suggester.SuggestBest(design, automationMap)?.CharacterName;
        }

        var groups = designs
            .OrderBy(d => d.DisplayCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.ReconstructedName, StringComparer.OrdinalIgnoreCase)
            .GroupBy(d => d.DisplayCategory, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CharacterDesignGroup(g.Key, g.ToList()))
            .ToList();

        return new DesignLibrarySnapshot(configDirectory, designs, groups);
    }

    /// <summary> Explicitly and durably assigns a design to a character, overriding any heuristic suggestion from now on. </summary>
    public async Task AssignCharacterAsync(GlamourerDesign design, string characterName, CancellationToken cancellationToken = default)
    {
        design.AssignedCharacter = characterName;
        await _assignmentStore.SetAsync(design.Identifier, characterName, cancellationToken);
    }

    /// <summary> Applies every staged edit to every target design's working copy. Pure in-memory, no file I/O. </summary>
    public IReadOnlyList<PropertyEditOutcome> ApplyPendingEdits(
        IEnumerable<GlamourerDesign> targets,
        IEnumerable<PendingPropertyEdit> edits)
    {
        var editList = edits as IReadOnlyList<PendingPropertyEdit> ?? edits.ToList();
        var outcomes = new List<PropertyEditOutcome>();

        foreach (var design in targets)
        {
            foreach (var edit in editList)
            {
                var success = design.TrySetPropertyValue(edit.SectionName, edit.EntryName, edit.PropertyName, edit.NewValueRaw, out var error);
                outcomes.Add(new PropertyEditOutcome(design.SourceFile, edit, success, error));
            }
        }

        return outcomes;
    }

    public Task<DesignSaveResult> SaveAsync(GlamourerDesign design, CancellationToken cancellationToken = default)
        => _repository.SaveAsync(design, cancellationToken);

    public Task<IReadOnlyList<DesignSaveResult>> SaveManyAsync(IEnumerable<GlamourerDesign> designs, CancellationToken cancellationToken = default)
        => _repository.SaveManyAsync(designs, cancellationToken);
}
