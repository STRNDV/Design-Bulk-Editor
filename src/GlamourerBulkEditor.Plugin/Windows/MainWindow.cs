using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using GlamourerBulkEditor.Core.Models;
using GlamourerBulkEditor.Core.Services;

namespace GlamourerBulkEditor.Plugin.Windows;

/// <summary>
/// The plugin's single window. Everything the user does lives entirely in
/// memory for as long as the window is open - there is no save/reload cycle
/// anywhere in this flow, which is the direct fix for the old app's core
/// bug: staged property edits are a persistent list, independent from
/// whichever design is currently focused, so switching between e.g.
/// Hairstyle and SkinColor never discards anything. Section/Entry/Property
/// options are recomputed fresh from the focused design on every single
/// frame instead of being cached, so there is no stale-reference class of
/// bug to begin with.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private static readonly string[] Sections = ["Customize", "Equipment", "Parameters"];

    private readonly DesignLibrary _designLibrary;
    private readonly GlamourerApiClient _apiClient;

    private string _configDirectory = @"%AppData%\XIVLauncher\pluginConfigs\Glamourer";
    private DesignLibrarySnapshot? _snapshot;
    private string _statusMessage = string.Empty;

    private CharacterDesignGroup? _selectedGroup;
    private string _searchText = string.Empty;
    private readonly HashSet<string> _targetedIdentifiers = [];
    private GlamourerDesign? _focusedDesign;

    private readonly List<PendingPropertyEdit> _stagedEdits = [];
    private int _newEditSectionIndex;
    private string? _newEditEntry;
    private string? _newEditProperty;
    private string _newEditValue = string.Empty;

    private string _manualCharacterAssignment = string.Empty;

    public MainWindow(DesignLibrary designLibrary, GlamourerApiClient apiClient)
        : base("Glamourer Bulk Editor##GlamourerBulkEditorMain")
    {
        _designLibrary = designLibrary;
        _apiClient = apiClient;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        DrawHeader();
        ImGui.Separator();

        if (_snapshot is null)
        {
            ImGui.TextWrapped("Load a design library to get started.");
            return;
        }

        var contentHeight = ImGui.GetContentRegionAvail().Y;

        ImGui.BeginChild("CharacterList", new Vector2(200, contentHeight), true);
        DrawCharacterList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("DesignList", new Vector2(320, contentHeight), true);
        DrawDesignList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("DesignDetails", Vector2.Zero, true);
        DrawFocusedDesignPanel();
        ImGui.EndChild();
    }

    private void DrawHeader()
    {
        ImGui.SetNextItemWidth(420);
        ImGui.InputText("Config directory", ref _configDirectory, 512);
        ImGui.SameLine();
        if (ImGui.Button("Load"))
            LoadLibrary();

        var glamourerStatus = _apiClient.IsAvailable
            ? $"Glamourer live preview: available (API {_apiClient.ApiVersionNumber?.Major}.{_apiClient.ApiVersionNumber?.Minor})"
            : "Glamourer live preview: unavailable (offline editing still works)";
        ImGui.TextDisabled(glamourerStatus);

        if (!string.IsNullOrEmpty(_statusMessage))
            ImGui.TextWrapped(_statusMessage);
    }

    private void DrawCharacterList()
    {
        ImGui.TextDisabled("Characters");
        ImGui.Separator();

        if (ImGui.Selectable("All characters", _selectedGroup is null))
            _selectedGroup = null;

        foreach (var group in _snapshot!.Groups)
        {
            var label = $"{group.CharacterName} ({group.Count})";
            if (ImGui.Selectable(label, ReferenceEquals(_selectedGroup, group)))
                _selectedGroup = group;
        }
    }

    private IEnumerable<GlamourerDesign> VisibleDesigns()
    {
        IEnumerable<GlamourerDesign> query = _selectedGroup?.Designs ?? _snapshot!.Designs;

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            query = query.Where(d =>
                d.ReconstructedName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || d.DisplayCategory.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || d.FileSystemFolder.Contains(_searchText, StringComparison.OrdinalIgnoreCase));
        }

        return query;
    }

    private void DrawDesignList()
    {
        ImGui.TextDisabled("Designs");
        ImGui.Separator();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search designs, characters, folders", ref _searchText, 256);

        var visible = VisibleDesigns().ToList();

        if (ImGui.SmallButton("Target all visible"))
            foreach (var d in visible)
                _targetedIdentifiers.Add(d.Identifier);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear targets"))
            _targetedIdentifiers.Clear();

        ImGui.Text($"{_targetedIdentifiers.Count} design(s) targeted");
        ImGui.Separator();

        foreach (var design in visible)
        {
            ImGui.PushID(design.Identifier);

            var isTargeted = _targetedIdentifiers.Contains(design.Identifier);
            if (ImGui.Checkbox("##target", ref isTargeted))
            {
                if (isTargeted)
                    _targetedIdentifiers.Add(design.Identifier);
                else
                    _targetedIdentifiers.Remove(design.Identifier);
            }

            ImGui.SameLine();
            var label = design.ReconstructedName;
            if (design.HasPendingChanges)
                label += " *";

            if (ImGui.Selectable(label, ReferenceEquals(_focusedDesign, design)))
                FocusDesign(design);

            ImGui.PopID();
        }
    }

    private void FocusDesign(GlamourerDesign design)
    {
        _focusedDesign = design;
        _manualCharacterAssignment = design.AssignedCharacter ?? string.Empty;
        _newEditEntry = null;
        _newEditProperty = null;
        _newEditValue = string.Empty;
    }

    private void DrawFocusedDesignPanel()
    {
        if (_focusedDesign is null)
        {
            ImGui.TextWrapped("Select a design from the list to inspect and edit it.");
            DrawStagedEditsSection(null);
            return;
        }

        var design = _focusedDesign;
        ImGui.TextWrapped($"Focused: {design.ReconstructedName}");
        ImGui.TextDisabled(design.SourceFile);
        if (design.HasPendingChanges)
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.2f, 1f), "Unsaved changes");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Rename");

        var characterName = design.CharacterName;
        if (ImGui.InputText("Character name", ref characterName, 128))
            design.CharacterName = characterName;

        var baseName = design.BaseName;
        if (ImGui.InputText("Design name", ref baseName, 128))
            design.BaseName = baseName;

        var folder = design.FileSystemFolder;
        if (ImGui.InputText("Folder", ref folder, 256))
            design.FileSystemFolder = folder;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Character assignment");
        ImGui.TextWrapped("Not every design follows a naming convention Glamourer Bulk Editor can guess. " +
                           "Confirm or correct which character this design belongs to; your choice is remembered.");

        ImGui.SetNextItemWidth(260);
        ImGui.InputText("##manualAssignment", ref _manualCharacterAssignment, 128);
        ImGui.SameLine();
        if (ImGui.Button("Assign") && !string.IsNullOrWhiteSpace(_manualCharacterAssignment))
            _ = AssignCharacterAsync(design, _manualCharacterAssignment);

        DrawStagedEditsSection(design);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Actions");

        if (ImGui.Button("Apply staged edits to targets"))
            ApplyStagedEditsToTargets();

        ImGui.SameLine();
        if (ImGui.Button("Discard changes on targets"))
            DiscardChangesOnTargets();

        if (ImGui.Button("Preview on my character") && _apiClient.IsAvailable)
            PreviewFocusedDesign();
        ImGui.SameLine();
        if (ImGui.Button("Revert preview"))
            RevertPreview();

        ImGui.SameLine();
        if (ImGui.Button("Save targets"))
            _ = SaveTargetsAsync();
    }

    private void DrawStagedEditsSection(GlamourerDesign? reference)
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Staged property edits");
        ImGui.TextWrapped("Stage as many properties as you like - switching between them never discards the others.");

        if (reference is not null)
        {
            ImGui.SetNextItemWidth(160);
            ImGui.Combo("Section", ref _newEditSectionIndex, Sections, Sections.Length);
            var section = Sections[_newEditSectionIndex];

            var entries = reference.GetEntryNames(section).ToArray();
            var entryIndex = Array.IndexOf(entries, _newEditEntry);
            if (entryIndex < 0)
                entryIndex = 0;

            if (entries.Length > 0)
            {
                ImGui.SetNextItemWidth(200);
                if (ImGui.Combo("Entry", ref entryIndex, entries, entries.Length))
                    _newEditProperty = null;
                _newEditEntry = entries[entryIndex];

                var properties = reference.GetPropertyNames(section, _newEditEntry).ToArray();
                var propertyIndex = Array.IndexOf(properties, _newEditProperty);
                if (propertyIndex < 0)
                    propertyIndex = 0;

                if (properties.Length > 0)
                {
                    ImGui.SetNextItemWidth(200);
                    if (ImGui.Combo("Property", ref propertyIndex, properties, properties.Length))
                    {
                        _newEditProperty = properties[propertyIndex];
                        _newEditValue = reference.GetPropertyValueText(section, _newEditEntry, _newEditProperty) ?? string.Empty;
                    }

                    _newEditProperty ??= properties[propertyIndex];

                    ImGui.SetNextItemWidth(200);
                    ImGui.InputText("New value", ref _newEditValue, 256);

                    if (ImGui.Button("Stage this edit") && _newEditEntry is not null && _newEditProperty is not null)
                        StageEdit(section, _newEditEntry, _newEditProperty, _newEditValue);
                }
            }
            else
            {
                ImGui.TextDisabled("This design has no entries under this section.");
            }
        }

        if (_stagedEdits.Count == 0)
        {
            ImGui.TextDisabled("No staged edits yet.");
            return;
        }

        for (var i = _stagedEdits.Count - 1; i >= 0; i--)
        {
            var edit = _stagedEdits[i];
            ImGui.PushID(i);
            ImGui.Text($"{edit.Label} -> {edit.NewValueRaw}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove"))
                _stagedEdits.RemoveAt(i);
            ImGui.PopID();
        }

        if (ImGui.SmallButton("Clear all staged edits"))
            _stagedEdits.Clear();
    }

    private void StageEdit(string section, string entry, string property, string value)
    {
        _stagedEdits.RemoveAll(e => e.SectionName == section && e.EntryName == entry && e.PropertyName == property);
        _stagedEdits.Add(new PendingPropertyEdit(section, entry, property, value));
    }

    private IReadOnlyList<GlamourerDesign> ResolveTargets()
    {
        if (_targetedIdentifiers.Count == 0)
            return _focusedDesign is null ? [] : [_focusedDesign];

        return _snapshot!.Designs.Where(d => _targetedIdentifiers.Contains(d.Identifier)).ToList();
    }

    private void ApplyStagedEditsToTargets()
    {
        var targets = ResolveTargets();
        if (targets.Count == 0 || _stagedEdits.Count == 0)
        {
            _statusMessage = "Nothing to apply: stage at least one edit and select at least one target.";
            return;
        }

        var outcomes = _designLibrary.ApplyPendingEdits(targets, _stagedEdits);
        var failures = outcomes.Where(o => !o.Success).ToList();

        _statusMessage = failures.Count == 0
            ? $"Applied {_stagedEdits.Count} staged edit(s) to {targets.Count} design(s) in memory. Nothing written to disk yet."
            : $"Applied with {failures.Count} failure(s): {string.Join("; ", failures.Select(f => $"{f.SourceFile}: {f.ErrorMessage}"))}";
    }

    private void DiscardChangesOnTargets()
    {
        foreach (var design in ResolveTargets())
            design.DiscardChanges();

        _statusMessage = "Reverted all staged edits on the current targets back to what is saved on disk.";
    }

    private void PreviewFocusedDesign()
    {
        if (_focusedDesign is null)
            return;

        var objectIndex = Plugin.ObjectTable.LocalPlayer?.ObjectIndex;
        if (objectIndex is null)
        {
            _statusMessage = "No local player found - log in to preview.";
            return;
        }

        var result = _apiClient.ApplyPreview(_focusedDesign.BuildWorkingSnapshot(), objectIndex.Value);
        _statusMessage = result.Success
            ? "Preview applied to your character."
            : $"Preview failed: {result.ErrorMessage}";
    }

    private void RevertPreview()
    {
        var objectIndex = Plugin.ObjectTable.LocalPlayer?.ObjectIndex;
        if (objectIndex is null)
            return;

        var result = _apiClient.RevertPreview(objectIndex.Value);
        _statusMessage = result.Success
            ? "Preview reverted."
            : $"Revert failed: {result.ErrorMessage}";
    }

    private async Task SaveTargetsAsync()
    {
        var targets = ResolveTargets();
        if (targets.Count == 0)
        {
            _statusMessage = "No targets selected to save.";
            return;
        }

        var results = await _designLibrary.SaveManyAsync(targets);
        var failures = results.Where(r => !r.Success).ToList();

        _statusMessage = failures.Count == 0
            ? $"Saved {results.Count} design(s), with a backup of each previous version."
            : $"Saved with {failures.Count} failure(s): {string.Join("; ", failures.Select(f => $"{f.SourceFile}: {f.ErrorMessage}"))}";
    }

    private async Task AssignCharacterAsync(GlamourerDesign design, string characterName)
    {
        await _designLibrary.AssignCharacterAsync(design, characterName);
        _statusMessage = $"Assigned '{design.ReconstructedName}' to '{characterName}'.";
    }

    private void LoadLibrary()
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(_configDirectory);
            _snapshot = _designLibrary.LoadAsync(expanded).GetAwaiter().GetResult();
            _targetedIdentifiers.Clear();
            _focusedDesign = null;
            _statusMessage = $"Loaded {_snapshot.Designs.Count} design(s) across {_snapshot.Groups.Count} character group(s).";
            _apiClient.Refresh();
        }
        catch (Exception ex)
        {
            _statusMessage = $"Load failed: {ex.Message}";
        }
    }
}
