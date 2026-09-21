using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using DesignBulkEditor.Core.Models;
using DesignBulkEditor.Core.Services;

namespace DesignBulkEditor.Plugin.Windows;

/// <summary>
/// The plugin's single window, structured as a guided flow instead of one
/// flat screen: "Bulk Edit" walks through Select -> Edit -> Review &amp; Save
/// one step at a time, and everything that only ever applies to a single
/// design (rename, character assignment, live preview, backups, share code)
/// lives in a separate "Single Design" tab so it never clutters the bulk
/// workflow.
///
/// Selection model within Bulk Edit: there is exactly ONE selection. A
/// checkbox adds/removes a design without disturbing the rest (for building
/// up a batch); clicking a design's name selects just that one; Ctrl+click
/// adds/removes without disturbing the rest, same as the checkbox. Every
/// action always operates on this one selection - there is no separate,
/// independently-tracked "focused" design that edits can silently miss.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private enum BulkStep { Select, Edit, Review }

    private static readonly string[] Sections = ["Customize", "Equipment", "Parameters"];
    private static readonly string[] RenameTargetLabels = ["Character name", "Design name", "Both"];

    private readonly DesignLibrary _designLibrary;
    private readonly GlamourerApiClient _apiClient;
    private readonly FileDialogManager _fileDialogManager = new();

    private string _configDirectory = @"%AppData%\XIVLauncher\pluginConfigs\Glamourer";
    private DesignLibrarySnapshot? _snapshot;
    private string _statusMessage = string.Empty;

    private BulkStep _step = BulkStep.Select;

    private CharacterDesignGroup? _selectedGroup;
    private string _searchText = string.Empty;

    private readonly HashSet<string> _selectedIdentifiers = [];
    private string? _primaryIdentifier;

    private readonly List<PendingPropertyEdit> _stagedEdits = [];
    private int _newEditSectionIndex;
    private string? _newEditEntry;
    private string? _newEditProperty;
    private string _newEditValue = string.Empty;

    private string _renameFind = string.Empty;
    private string _renameReplace = string.Empty;
    private int _renameTargetIndex;
    private bool _renameCaseSensitive;

    private List<(GlamourerDesign Design, IReadOnlyList<PropertyDiff> Diffs)> _reviewItems = [];
    private string? _saveOutcomeMessage;

    // "Single Design" tab has its own, independent single-item selection -
    // deliberately unrelated to the Bulk Edit tab's multi-selection.
    private string? _singleDesignIdentifier;
    private string _manualCharacterAssignment = string.Empty;
    private string _importCode = string.Empty;
    private string _importName = string.Empty;

    public MainWindow(DesignLibrary designLibrary, GlamourerApiClient apiClient)
        : base("Design Bulk Editor##DesignBulkEditorMain")
    {
        _designLibrary = designLibrary;
        _apiClient = apiClient;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(860, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Dispose()
    {
    }

    private IReadOnlyList<GlamourerDesign> SelectedDesigns()
        => _snapshot is null ? [] : _snapshot.Designs.Where(d => _selectedIdentifiers.Contains(d.Identifier)).ToList();

    private GlamourerDesign? PrimaryDesign
        => _snapshot?.Designs.FirstOrDefault(d => d.Identifier == _primaryIdentifier);

    private GlamourerDesign? SingleToolDesign
        => _snapshot?.Designs.FirstOrDefault(d => d.Identifier == _singleDesignIdentifier);

    public override void Draw()
    {
        _fileDialogManager.Draw();

        DrawHeader();
        ImGui.Separator();

        if (_snapshot is null)
        {
            ImGui.TextWrapped("Load a design library to get started.");
            return;
        }

        DrawPendingChangesSummary();
        ImGui.Separator();

        if (ImGui.BeginTabBar("MainTabs"))
        {
            if (ImGui.BeginTabItem("Bulk Edit"))
            {
                DrawBulkEditTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Single Design Tools"))
            {
                DrawSingleDesignTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
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

    private void DrawPendingChangesSummary()
    {
        var pending = _snapshot!.Designs.Where(d => d.HasPendingChanges).ToList();
        if (pending.Count == 0)
        {
            ImGui.TextDisabled("No unsaved edits anywhere in the loaded library.");
            return;
        }

        ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.2f, 1f),
            $"{pending.Count} design(s) in the library have unsaved edits (marked with * below).");
        ImGui.SameLine();
        if (ImGui.SmallButton("Select all unsaved and go to Edit"))
        {
            _selectedIdentifiers.Clear();
            foreach (var d in pending)
                _selectedIdentifiers.Add(d.Identifier);
            _primaryIdentifier = pending[0].Identifier;
            _step = BulkStep.Edit;
        }
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

    // ---------------------------------------------------------------
    // Bulk Edit tab: a guided Select -> Edit -> Review & Save flow.
    // ---------------------------------------------------------------

    private void DrawBulkEditTab()
    {
        DrawStepIndicator();
        ImGui.Separator();
        ImGui.Spacing();

        switch (_step)
        {
            case BulkStep.Select:
                DrawSelectStep();
                break;
            case BulkStep.Edit:
                DrawEditStep();
                break;
            case BulkStep.Review:
                DrawReviewStep();
                break;
        }
    }

    private void DrawStepIndicator()
    {
        DrawStepLabel("1. Select", BulkStep.Select, SelectedDesigns().Count > 0 || _step != BulkStep.Select);
        ImGui.SameLine();
        ImGui.TextDisabled("->");
        ImGui.SameLine();
        DrawStepLabel("2. Edit", BulkStep.Edit, _step == BulkStep.Edit || _step == BulkStep.Review);
        ImGui.SameLine();
        ImGui.TextDisabled("->");
        ImGui.SameLine();
        DrawStepLabel("3. Review and Save", BulkStep.Review, _step == BulkStep.Review);
    }

    private void DrawStepLabel(string label, BulkStep step, bool reachable)
    {
        var isCurrent = _step == step;
        if (isCurrent)
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), label);
        else if (reachable)
        {
            if (ImGui.Button(label))
                _step = step;
        }
        else
        {
            ImGui.TextDisabled(label);
        }
    }

    private void DrawSelectStep()
    {
        ImGui.TextWrapped("Pick which designs you want to work on. Checkbox adds to the selection without " +
                           "losing the rest; clicking a name selects only that one; Ctrl+click adds/removes.");
        ImGui.Spacing();

        var contentHeight = ImGui.GetContentRegionAvail().Y - 40;

        ImGui.BeginChild("CharacterList", new Vector2(200, contentHeight), true);
        DrawCharacterList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("DesignList", Vector2.Zero, true);
        DrawDesignList();
        ImGui.EndChild();

        ImGui.Spacing();
        var selectedCount = _selectedIdentifiers.Count;
        ImGui.BeginDisabled(selectedCount == 0);
        if (ImGui.Button($"Continue with {selectedCount} selected design(s) ->"))
            _step = BulkStep.Edit;
        ImGui.EndDisabled();
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
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search designs, characters, folders", ref _searchText, 256);

        var visible = VisibleDesigns().ToList();

        if (ImGui.SmallButton("Select all visible"))
            foreach (var d in visible)
                SelectAdditionally(d);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear selection"))
        {
            _selectedIdentifiers.Clear();
            _primaryIdentifier = null;
        }

        ImGui.Text($"{_selectedIdentifiers.Count} design(s) selected");
        ImGui.Separator();

        foreach (var design in visible)
        {
            ImGui.PushID(design.Identifier);

            var isSelected = _selectedIdentifiers.Contains(design.Identifier);
            if (ImGui.Checkbox("##select", ref isSelected))
            {
                if (isSelected)
                    SelectAdditionally(design);
                else
                    Deselect(design);
            }

            ImGui.SameLine();
            var label = design.ReconstructedName;
            if (design.HasPendingChanges)
                label += " *";

            if (ImGui.Selectable(label, isSelected))
            {
                if (ImGui.GetIO().KeyCtrl)
                {
                    if (_selectedIdentifiers.Contains(design.Identifier))
                        Deselect(design);
                    else
                        SelectAdditionally(design);
                }
                else
                {
                    SelectOnly(design);
                }
            }

            ImGui.PopID();
        }
    }

    private void SelectOnly(GlamourerDesign design)
    {
        _selectedIdentifiers.Clear();
        _selectedIdentifiers.Add(design.Identifier);
        _primaryIdentifier = design.Identifier;
        ResetStagingInputs();
    }

    private void SelectAdditionally(GlamourerDesign design)
    {
        _selectedIdentifiers.Add(design.Identifier);
        _primaryIdentifier ??= design.Identifier;
    }

    private void Deselect(GlamourerDesign design)
    {
        _selectedIdentifiers.Remove(design.Identifier);
        if (_primaryIdentifier == design.Identifier)
        {
            _primaryIdentifier = _selectedIdentifiers.FirstOrDefault();
            ResetStagingInputs();
        }
    }

    private void ResetStagingInputs()
    {
        _newEditEntry = null;
        _newEditProperty = null;
        _newEditValue = string.Empty;
    }

    private void DrawEditStep()
    {
        var selected = SelectedDesigns();
        if (ImGui.Button("<- Back to selection"))
        {
            _step = BulkStep.Select;
            return;
        }

        ImGui.SameLine();
        ImGui.TextUnformatted($"Editing {selected.Count} design(s).");

        if (selected.Count == 0)
        {
            ImGui.TextWrapped("Nothing selected - go back and pick at least one design.");
            return;
        }

        ImGui.Spacing();

        if (ImGui.BeginTabBar("EditModeTabs"))
        {
            if (ImGui.BeginTabItem("Change a property"))
            {
                DrawStagedEditsSection(PrimaryDesign);
                ImGui.Spacing();

                ImGui.BeginDisabled(_stagedEdits.Count == 0);
                if (ImGui.Button($"Apply {_stagedEdits.Count} staged edit(s) to {selected.Count} design(s), then review ->"))
                    ApplyStagedEditsAndGoToReview(selected);
                ImGui.EndDisabled();

                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Batch rename"))
            {
                DrawBatchRenameSection(selected);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        var pendingInSelection = selected.Count(d => d.HasPendingChanges);
        if (pendingInSelection > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.2f, 1f), $"{pendingInSelection} of the selected design(s) already have unsaved edits.");
            if (ImGui.Button("Review pending changes and save now ->"))
                OpenReviewStep(selected);
            ImGui.SameLine();
            if (ImGui.Button("Discard all unsaved edits on selection"))
                DiscardChangesOnSelection(selected);
        }
    }

    private void DrawStagedEditsSection(GlamourerDesign? reference)
    {
        ImGui.TextWrapped("Stage as many properties as you like here, then apply them to your whole selection in one step.");

        if (reference is not null)
        {
            ImGui.TextDisabled($"Browsing properties from: {reference.ReconstructedName}");

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

        if (_stagedEdits.Count == 0)
        {
            ImGui.TextDisabled("No staged edits yet.");
            return;
        }

        ImGui.Spacing();
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

    private void DrawBatchRenameSection(IReadOnlyList<GlamourerDesign> selected)
    {
        ImGui.TextWrapped("Find-and-replace across every selected design's character/design name.");

        ImGui.SetNextItemWidth(160);
        ImGui.InputText("Find", ref _renameFind, 128);
        ImGui.SetNextItemWidth(160);
        ImGui.InputText("Replace with", ref _renameReplace, 128);
        ImGui.SetNextItemWidth(160);
        ImGui.Combo("Field", ref _renameTargetIndex, RenameTargetLabels, RenameTargetLabels.Length);
        ImGui.Checkbox("Case sensitive", ref _renameCaseSensitive);

        ImGui.BeginDisabled(string.IsNullOrEmpty(_renameFind));
        if (ImGui.Button($"Apply rename to {selected.Count} design(s), then review ->"))
            ApplyBatchRenameAndGoToReview(selected);
        ImGui.EndDisabled();
    }

    private void StageEdit(string section, string entry, string property, string value)
    {
        _stagedEdits.RemoveAll(e => e.SectionName == section && e.EntryName == entry && e.PropertyName == property);
        _stagedEdits.Add(new PendingPropertyEdit(section, entry, property, value));
    }

    private void ApplyStagedEditsAndGoToReview(IReadOnlyList<GlamourerDesign> targets)
    {
        var outcomes = _designLibrary.ApplyPendingEdits(targets, _stagedEdits);
        var failures = outcomes.Where(o => !o.Success).ToList();
        if (failures.Count > 0)
            _statusMessage = $"{failures.Count} propert(y/ies) could not be applied: {string.Join("; ", failures.Select(f => f.ErrorMessage))}";

        OpenReviewStep(targets);
    }

    private void ApplyBatchRenameAndGoToReview(IReadOnlyList<GlamourerDesign> targets)
    {
        var target = (RenameTarget)_renameTargetIndex;
        var pattern = new RenamePattern(_renameFind, _renameReplace, target, _renameCaseSensitive);
        var changed = _designLibrary.ApplyRenamePattern(targets, pattern);

        if (changed.Count == 0)
        {
            _statusMessage = "No selected design matched the find text.";
            return;
        }

        OpenReviewStep(targets);
    }

    private void DiscardChangesOnSelection(IReadOnlyList<GlamourerDesign> targets)
    {
        foreach (var design in targets)
            design.DiscardChanges();

        _statusMessage = "Reverted all unsaved edits on the selected designs back to what is saved on disk.";
    }

    private void OpenReviewStep(IReadOnlyList<GlamourerDesign> targets)
    {
        _reviewItems = targets
            .Select(d => (Design: d, Diffs: d.GetPendingDiffs()))
            .Where(t => t.Diffs.Count > 0)
            .ToList();

        _saveOutcomeMessage = null;

        if (_reviewItems.Count == 0)
        {
            _statusMessage = "No pending changes on the selection to review.";
            return;
        }

        _step = BulkStep.Review;
    }

    private void DrawReviewStep()
    {
        if (ImGui.Button("<- Back to edit"))
        {
            _step = BulkStep.Edit;
            return;
        }

        ImGui.Spacing();

        if (_saveOutcomeMessage is not null)
        {
            ImGui.TextWrapped(_saveOutcomeMessage);
            ImGui.Spacing();
            if (ImGui.Button("Start over ->"))
            {
                _selectedIdentifiers.Clear();
                _primaryIdentifier = null;
                _stagedEdits.Clear();
                _saveOutcomeMessage = null;
                _step = BulkStep.Select;
            }

            return;
        }

        ImGui.TextWrapped($"{_reviewItems.Count} design(s) have pending changes. Nothing is written until you confirm.");
        ImGui.Separator();

        ImGui.BeginChild("ReviewList", new Vector2(0, ImGui.GetContentRegionAvail().Y - 50), true);
        foreach (var (design, diffs) in _reviewItems)
        {
            ImGui.TextUnformatted(design.ReconstructedName);
            foreach (var diff in diffs)
                ImGui.BulletText($"{diff.Path}: {diff.OldValue ?? "(none)"} -> {diff.NewValue ?? "(none)"}");
            ImGui.Spacing();
        }

        ImGui.EndChild();

        if (ImGui.Button("Confirm and save to disk"))
            _ = ConfirmReviewAndSaveAsync();
        ImGui.SameLine();
        if (ImGui.Button("Cancel (keep editing)"))
            _step = BulkStep.Edit;
    }

    private async Task ConfirmReviewAndSaveAsync()
    {
        var targets = _reviewItems.Select(i => i.Design).ToList();
        var results = await _designLibrary.SaveManyAsync(targets);
        var failures = results.Where(r => !r.Success).ToList();

        _saveOutcomeMessage = failures.Count == 0
            ? $"Saved {results.Count} design(s) to disk, with a backup of each previous version."
            : $"Saved with {failures.Count} failure(s): {string.Join("; ", failures.Select(f => $"{f.SourceFile}: {f.ErrorMessage}"))}";
    }

    // ---------------------------------------------------------------
    // Single Design Tools tab: rename/assignment/preview/backups/share code
    // for exactly one design at a time, kept out of the bulk-edit flow.
    // ---------------------------------------------------------------

    private void DrawSingleDesignTab()
    {
        ImGui.TextWrapped("Pick one design to inspect or manage individually.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(400);
        var currentLabel = SingleToolDesign?.ReconstructedName ?? "(choose a design)";
        if (ImGui.BeginCombo("Design", currentLabel))
        {
            foreach (var design in _snapshot!.Designs)
            {
                var isSelected = design.Identifier == _singleDesignIdentifier;
                var label = design.ReconstructedName + (design.HasPendingChanges ? " *" : "");
                if (ImGui.Selectable(label, isSelected))
                {
                    _singleDesignIdentifier = design.Identifier;
                    _manualCharacterAssignment = design.AssignedCharacter ?? string.Empty;
                }
            }

            ImGui.EndCombo();
        }

        var design2 = SingleToolDesign;
        if (design2 is null)
        {
            ImGui.TextDisabled("No design selected.");
            return;
        }

        ImGui.TextDisabled(design2.SourceFile);
        if (design2.HasPendingChanges)
            ImGui.TextColored(new Vector4(0.95f, 0.75f, 0.2f, 1f), "This design has unsaved edits.");

        ImGui.Spacing();

        if (ImGui.CollapsingHeader("Rename", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var characterName = design2.CharacterName;
            if (ImGui.InputText("Character name", ref characterName, 128))
                design2.CharacterName = characterName;

            var baseName = design2.BaseName;
            if (ImGui.InputText("Design name", ref baseName, 128))
                design2.BaseName = baseName;

            var folder = design2.FileSystemFolder;
            if (ImGui.InputText("Folder", ref folder, 256))
                design2.FileSystemFolder = folder;

            if (design2.HasPendingChanges)
            {
                if (ImGui.Button("Save this design"))
                    _ = SaveSingleAsync(design2);
                ImGui.SameLine();
                if (ImGui.Button("Discard changes"))
                {
                    design2.DiscardChanges();
                    _statusMessage = "Reverted to what is saved on disk.";
                }
            }
        }

        if (ImGui.CollapsingHeader("Character assignment"))
        {
            ImGui.TextWrapped("Not every design follows a naming convention Design Bulk Editor can guess. " +
                               "Confirm or correct which character this design belongs to; your choice is remembered.");

            ImGui.SetNextItemWidth(260);
            ImGui.InputText("##manualAssignment", ref _manualCharacterAssignment, 128);
            ImGui.SameLine();
            if (ImGui.Button("Assign") && !string.IsNullOrWhiteSpace(_manualCharacterAssignment))
                _ = AssignCharacterAsync(design2, _manualCharacterAssignment);
        }

        if (ImGui.CollapsingHeader("Live preview"))
        {
            if (ImGui.Button("Preview on my character") && _apiClient.IsAvailable)
                PreviewDesign(design2);
            ImGui.SameLine();
            if (ImGui.Button("Revert preview"))
                RevertPreview();
        }

        if (ImGui.CollapsingHeader("Backups"))
            DrawBackupsSection(design2);

        if (ImGui.CollapsingHeader("Share code"))
            DrawShareCodeSection(design2);
    }

    private void DrawBackupsSection(GlamourerDesign design)
    {
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

    private void PreviewDesign(GlamourerDesign design)
    {
        var objectIndex = Plugin.ObjectTable.LocalPlayer?.ObjectIndex;
        if (objectIndex is null)
        {
            _statusMessage = "No local player found - log in to preview.";
            return;
        }

        var result = _apiClient.ApplyPreview(design.BuildWorkingSnapshot(), objectIndex.Value);
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

    private async Task SaveSingleAsync(GlamourerDesign design)
    {
        var result = await _designLibrary.SaveAsync(design);
        _statusMessage = result.Success
            ? "Saved to disk, with a backup of the previous version."
            : $"Save failed: {result.ErrorMessage}";
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
            _selectedIdentifiers.Clear();
            _primaryIdentifier = null;
            _singleDesignIdentifier = null;
            _step = BulkStep.Select;
            _statusMessage = $"Loaded {_snapshot.Designs.Count} design(s) across {_snapshot.Groups.Count} character group(s).";
            _apiClient.Refresh();
        }
        catch (Exception ex)
        {
            _statusMessage = $"Load failed: {ex.Message}";
        }
    }
}
