using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using DesignBulkEditor.Core.Models;
using DesignBulkEditor.Core.Services;

namespace DesignBulkEditor.Plugin.Windows;

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
    private static readonly string[] RenameTargetLabels = ["Character name", "Design name", "Both"];

    private readonly DesignLibrary _designLibrary;
    private readonly GlamourerApiClient _apiClient;
    private readonly FileDialogManager _fileDialogManager = new();

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

    private string _renameFind = string.Empty;
    private string _renameReplace = string.Empty;
    private int _renameTargetIndex;
    private bool _renameCaseSensitive;

    private string _importCode = string.Empty;
    private string _importName = string.Empty;

    private List<(GlamourerDesign Design, IReadOnlyList<PropertyDiff> Diffs)> _reviewItems = [];

    public MainWindow(DesignLibrary designLibrary, GlamourerApiClient apiClient)
        : base("Design Bulk Editor##DesignBulkEditorMain")
    {
        _designLibrary = designLibrary;
        _apiClient = apiClient;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(820, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        _fileDialogManager.Draw();
        DrawReviewModal();

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
        ImGui.SetNextItemWidth(380);
        ImGui.InputText("Config directory", ref _configDirectory, 512);
        ImGui.SameLine();
        if (ImGui.Button("Browse"))
            BrowseForConfigDirectory();
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

    private void BrowseForConfigDirectory()
    {
        var startDirectory = Directory.Exists(_configDirectory)
            ? _configDirectory
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        _fileDialogManager.OpenFolderDialog("Select Glamourer config folder", (success, path) =>
        {
            if (success)
                _configDirectory = path;
        }, startDirectory);
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
        DrawBatchRenameSection();
        DrawStagedEditsSection(_focusedDesign);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Actions on current targets");

        if (ImGui.Button("Apply staged edits to targets"))
            ApplyStagedEditsToTargets();
        ImGui.SameLine();
        if (ImGui.Button("Discard changes on targets"))
            DiscardChangesOnTargets();
        ImGui.SameLine();
        if (ImGui.Button("Review & save targets"))
            OpenReviewModal();

        if (_focusedDesign is null)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Select a design from the list to inspect it, edit its name/folder, review its backups, or export/import a share code.");
            return;
        }

        var design = _focusedDesign;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextWrapped($"Focused: {design.ReconstructedName}");
        ImGui.TextDisabled(design.SourceFile);
        if (design.HasPendingChanges)
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.2f, 1f), "Unsaved changes");

        if (ImGui.CollapsingHeader("Rename", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var characterName = design.CharacterName;
            if (ImGui.InputText("Character name", ref characterName, 128))
                design.CharacterName = characterName;

            var baseName = design.BaseName;
            if (ImGui.InputText("Design name", ref baseName, 128))
                design.BaseName = baseName;

            var folder = design.FileSystemFolder;
            if (ImGui.InputText("Folder", ref folder, 256))
                design.FileSystemFolder = folder;
        }

        if (ImGui.CollapsingHeader("Character assignment", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextWrapped("Not every design follows a naming convention Design Bulk Editor can guess. " +
                               "Confirm or correct which character this design belongs to; your choice is remembered.");

            ImGui.SetNextItemWidth(260);
            ImGui.InputText("##manualAssignment", ref _manualCharacterAssignment, 128);
            ImGui.SameLine();
            if (ImGui.Button("Assign") && !string.IsNullOrWhiteSpace(_manualCharacterAssignment))
                _ = AssignCharacterAsync(design, _manualCharacterAssignment);
        }

        if (ImGui.CollapsingHeader("Live preview"))
        {
            if (ImGui.Button("Preview on my character") && _apiClient.IsAvailable)
                PreviewFocusedDesign();
            ImGui.SameLine();
            if (ImGui.Button("Revert preview"))
                RevertPreview();
        }

        DrawBackupsSection(design);
        DrawShareCodeSection(design);
    }

    private void DrawBatchRenameSection()
    {
        if (!ImGui.CollapsingHeader("Batch rename targets"))
            return;

        ImGui.SetNextItemWidth(160);
        ImGui.InputText("Find", ref _renameFind, 128);
        ImGui.SetNextItemWidth(160);
        ImGui.InputText("Replace with", ref _renameReplace, 128);
        ImGui.SetNextItemWidth(160);
        ImGui.Combo("Field", ref _renameTargetIndex, RenameTargetLabels, RenameTargetLabels.Length);
        ImGui.Checkbox("Case sensitive", ref _renameCaseSensitive);

        if (ImGui.Button("Apply rename to targets") && !string.IsNullOrEmpty(_renameFind))
            ApplyBatchRename();
    }

    private void DrawStagedEditsSection(GlamourerDesign? reference)
    {
        if (!ImGui.CollapsingHeader("Staged property edits", ImGuiTreeNodeFlags.DefaultOpen))
            return;

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
                    var rawCurrentValue = reference.GetPropertyValueText(section, _newEditEntry, properties[propertyIndex]);
                    if (ImGui.Combo("Property", ref propertyIndex, properties, properties.Length))
                    {
                        _newEditProperty = properties[propertyIndex];
                        rawCurrentValue = reference.GetPropertyValueText(section, _newEditEntry, _newEditProperty);
                        _newEditValue = PropertyValueInspector.DecodeForEditing(rawCurrentValue);
                    }

                    _newEditProperty ??= properties[propertyIndex];

                    var kind = PropertyValueInspector.Classify(_newEditProperty, rawCurrentValue);
                    DrawValueInput("New value", kind);

                    if (ImGui.Button("Stage this edit"))
                        StageEdit(section, _newEditEntry, _newEditProperty, _newEditValue);
                }
            }
            else
            {
                ImGui.TextDisabled("This design has no entries under this section.");
            }
        }
        else
        {
            ImGui.TextDisabled("Select a design to browse its properties, or edit staged entries below.");
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

    private void DrawValueInput(string label, PropertyValueKind kind)
    {
        switch (kind)
        {
            case PropertyValueKind.Boolean:
                var boolValue = string.Equals(_newEditValue, "true", StringComparison.OrdinalIgnoreCase);
                if (ImGui.Checkbox(label, ref boolValue))
                    _newEditValue = boolValue ? "true" : "false";
                break;

            case PropertyValueKind.Integer:
                var intValue = int.TryParse(_newEditValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv) ? iv : 0;
                ImGui.SetNextItemWidth(160);
                if (ImGui.InputInt(label, ref intValue))
                    _newEditValue = intValue.ToString(CultureInfo.InvariantCulture);
                break;

            case PropertyValueKind.Float:
                var floatValue = float.TryParse(_newEditValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var fv) ? fv : 0f;
                ImGui.SetNextItemWidth(160);
                if (ImGui.InputFloat(label, ref floatValue))
                    _newEditValue = floatValue.ToString(CultureInfo.InvariantCulture);
                break;

            case PropertyValueKind.HexColor:
                var color = HexToColor(_newEditValue);
                ImGui.SetNextItemWidth(200);
                if (ImGui.ColorEdit3(label, ref color))
                    _newEditValue = ColorToHex(color);
                break;

            default:
                ImGui.SetNextItemWidth(200);
                ImGui.InputText(label, ref _newEditValue, 256);
                break;
        }
    }

    private static Vector3 HexToColor(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length < 6
            || !byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return Vector3.Zero;

        return new Vector3(r / 255f, g / 255f, b / 255f);
    }

    private static string ColorToHex(Vector3 color)
    {
        var r = (byte)Math.Clamp(color.X * 255f, 0, 255);
        var g = (byte)Math.Clamp(color.Y * 255f, 0, 255);
        var b = (byte)Math.Clamp(color.Z * 255f, 0, 255);
        return $"{r:x2}{g:x2}{b:x2}";
    }

    private void DrawBackupsSection(GlamourerDesign design)
    {
        if (!ImGui.CollapsingHeader("Backups"))
            return;

        var backups = _designLibrary.ListBackups(design);
        if (backups.Count == 0)
        {
            ImGui.TextDisabled("No backups yet - one is made automatically the first time this design is saved.");
            return;
        }

        foreach (var backup in backups)
        {
            ImGui.PushID(backup.FilePath);
            ImGui.Text(backup.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
            ImGui.SameLine();
            if (ImGui.SmallButton("Restore"))
                _ = RestoreBackupAsync(design, backup.FilePath);
            ImGui.PopID();
        }
    }

    private void DrawShareCodeSection(GlamourerDesign design)
    {
        if (!ImGui.CollapsingHeader("Share code"))
            return;

        if (!_apiClient.IsAvailable)
        {
            ImGui.TextDisabled("Requires Glamourer to be available.");
            return;
        }

        if (ImGui.Button("Copy share code to clipboard"))
            ExportDesignCode(design);

        ImGui.Spacing();
        ImGui.TextDisabled("Import a shared code as a new design in Glamourer's library:");
        ImGui.SetNextItemWidth(400);
        ImGui.InputTextMultiline("##importCode", ref _importCode, 8192, new Vector2(400, 60));
        ImGui.SetNextItemWidth(240);
        ImGui.InputText("New design name", ref _importName, 128);
        if (ImGui.Button("Import as new design") && !string.IsNullOrWhiteSpace(_importCode) && !string.IsNullOrWhiteSpace(_importName))
            ImportDesignCode();
    }

    private void ExportDesignCode(GlamourerDesign design)
    {
        var result = _apiClient.ExportDesignAsCode(design.Identifier);
        _statusMessage = result.Success && result.Value is not null
            ? SetClipboardAndDescribe(result.Value)
            : $"Export failed: {result.ErrorMessage}";
    }

    private static string SetClipboardAndDescribe(string code)
    {
        ImGui.SetClipboardText(code);
        return "Share code copied to clipboard.";
    }

    private void ImportDesignCode()
    {
        var result = _apiClient.ImportDesignFromCode(_importCode, _importName);
        _statusMessage = result.Success
            ? $"Imported new design '{_importName}'. Reload the library to see it."
            : $"Import failed: {result.ErrorMessage}";
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

    private void ApplyBatchRename()
    {
        var targets = ResolveTargets();
        if (targets.Count == 0)
        {
            _statusMessage = "No targets selected for rename.";
            return;
        }

        var target = (RenameTarget)_renameTargetIndex;
        var pattern = new RenamePattern(_renameFind, _renameReplace, target, _renameCaseSensitive);
        var changed = _designLibrary.ApplyRenamePattern(targets, pattern);

        _statusMessage = changed.Count == 0
            ? "No target matched the find text."
            : $"Renamed {changed.Count} design(s) in memory. Not saved to disk yet.";
    }

    private void OpenReviewModal()
    {
        var targets = ResolveTargets();
        _reviewItems = targets
            .Select(d => (Design: d, Diffs: d.GetPendingDiffs()))
            .Where(t => t.Diffs.Count > 0)
            .ToList();

        if (_reviewItems.Count == 0)
        {
            _statusMessage = "No pending changes on the current targets to review.";
            return;
        }

        ImGui.OpenPopup("Review changes##ReviewModal");
    }

    private void DrawReviewModal()
    {
        var open = true;
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 200), new Vector2(700, 600));
        if (!ImGui.BeginPopupModal("Review changes##ReviewModal", ref open, ImGuiWindowFlags.None))
            return;

        ImGui.TextWrapped($"{_reviewItems.Count} design(s) have pending changes. Nothing is written until you confirm.");
        ImGui.Separator();

        foreach (var (design, diffs) in _reviewItems)
        {
            ImGui.TextUnformatted(design.ReconstructedName);
            foreach (var diff in diffs)
                ImGui.BulletText($"{diff.Path}: {diff.OldValue ?? "(none)"} -> {diff.NewValue ?? "(none)"}");
            ImGui.Spacing();
        }

        ImGui.Separator();
        if (ImGui.Button("Confirm and save"))
        {
            _ = ConfirmReviewAndSaveAsync();
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        ImGui.EndPopup();
    }

    private async Task ConfirmReviewAndSaveAsync()
    {
        var targets = _reviewItems.Select(i => i.Design).ToList();
        var results = await _designLibrary.SaveManyAsync(targets);
        var failures = results.Where(r => !r.Success).ToList();

        _statusMessage = failures.Count == 0
            ? $"Saved {results.Count} design(s), with a backup of each previous version."
            : $"Saved with {failures.Count} failure(s): {string.Join("; ", failures.Select(f => $"{f.SourceFile}: {f.ErrorMessage}"))}";
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

    private async Task RestoreBackupAsync(GlamourerDesign design, string backupPath)
    {
        var result = await _designLibrary.RestoreBackupAsync(design, backupPath);
        _statusMessage = result.Success
            ? "Restored from backup (the pre-restore state was itself backed up)."
            : $"Restore failed: {result.ErrorMessage}";
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
