using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DesignBulkEditor.Core.Models;

/// <summary>
/// One Glamourer design JSON file. Keeps the on-disk baseline and an editable
/// working copy separate, so any number of staged edits (name, folder, or
/// arbitrary section/entry/property values) can be previewed, combined, and
/// discarded without ever touching the file on disk.
/// </summary>
public sealed class GlamourerDesign
{
    private static readonly Regex CharacterPrefixPattern = new(@"^\(([^)]+)\)\s*(.*)$", RegexOptions.Compiled);

    private JsonObject _baseline;
    private JsonObject _working;

    private string _characterName;
    private string _baseName;
    private string _fileSystemFolder;

    public GlamourerDesign(string sourceFile, JsonObject document)
    {
        SourceFile = sourceFile;
        _baseline = (JsonObject)document.DeepClone();
        _working = document;

        Identifier = ReadString(_working, "Identifier") ?? string.Empty;
        _fileSystemFolder = ReadString(_working, "FileSystemFolder") ?? string.Empty;
        SortOrderName = ReadString(_working, "SortOrderName");

        (_characterName, _baseName) = SplitName(ReadString(_working, "Name") ?? string.Empty);
    }

    public string SourceFile { get; }

    public string Identifier { get; }

    public string? SortOrderName { get; private set; }

    /// <summary> The name currently persisted on disk, unaffected by staged edits. </summary>
    public string CurrentName => ReadString(_baseline, "Name") ?? string.Empty;

    public string CharacterName
    {
        get => _characterName;
        set => _characterName = value?.Trim() ?? string.Empty;
    }

    public string BaseName
    {
        get => _baseName;
        set => _baseName = value?.Trim() ?? string.Empty;
    }

    public string FileSystemFolder
    {
        get => _fileSystemFolder;
        set => _fileSystemFolder = value?.Trim() ?? string.Empty;
    }

    public string ReconstructedName
        => string.IsNullOrWhiteSpace(_characterName)
            ? _baseName.Trim()
            : $"({_characterName.Trim()}) {_baseName.Trim()}".Trim();

    /// <summary>
    /// Which character this design is grouped under. Independent of <see cref="CharacterName"/>
    /// (which only reflects the "(Character) Design" text in the Name field): resolved by
    /// <c>DesignLibrary</c> from a confirmed manual assignment or a heuristic suggestion,
    /// since not every design follows the name-prefix convention at all. Null until resolved.
    /// </summary>
    public string? AssignedCharacter { get; set; }

    public string DisplayCategory
        => string.IsNullOrWhiteSpace(AssignedCharacter) ? "Unassigned" : AssignedCharacter;

    /// <summary> True if the working copy (name, folder, or any property) differs from the on-disk baseline. </summary>
    public bool HasPendingChanges
        => !JsonNode.DeepEquals(BuildWorkingSnapshot(), _baseline);

    public IReadOnlyList<string> GetEntryNames(string sectionName)
        => GetSection(_working, sectionName)?.Select(kvp => kvp.Key).ToList() ?? [];

    public IReadOnlyList<string> GetPropertyNames(string sectionName, string entryName)
        => GetEntry(_working, sectionName, entryName)?.Select(kvp => kvp.Key).ToList() ?? [];

    public string? GetPropertyValueText(string sectionName, string entryName, string propertyName)
        => GetEntry(_working, sectionName, entryName)?[propertyName]?.ToJsonString();

    /// <summary> Stages a property value change on the working copy. Never touches disk. </summary>
    public bool TrySetPropertyValue(string sectionName, string entryName, string propertyName, string rawValue, out string? error)
    {
        error = null;

        if (GetEntry(_working, sectionName, entryName) is not { } entry)
        {
            error = $"Entry '{sectionName}/{entryName}' not found.";
            return false;
        }

        if (entry[propertyName] is not JsonNode existing)
        {
            error = $"Property '{propertyName}' not found.";
            return false;
        }

        try
        {
            entry[propertyName] = ParseForExistingType(existing, rawValue);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Builds the JSON that would be written to disk (or pushed as a live preview)
    /// if the working copy were saved right now. Does not mutate any state.
    /// </summary>
    public JsonObject BuildWorkingSnapshot()
    {
        var clone = (JsonObject)_working.DeepClone();
        clone["Name"] = ReconstructedName;

        if (string.IsNullOrWhiteSpace(_fileSystemFolder))
            clone.Remove("FileSystemFolder");
        else
            clone["FileSystemFolder"] = _fileSystemFolder;

        if (string.IsNullOrWhiteSpace(SortOrderName))
            clone.Remove("SortOrderName");
        else
            clone["SortOrderName"] = SortOrderName;

        return clone;
    }

    /// <summary> Reverts every staged edit (name, folder, and any property value) back to the on-disk baseline. </summary>
    public void DiscardChanges()
    {
        _working = (JsonObject)_baseline.DeepClone();
        (_characterName, _baseName) = SplitName(ReadString(_working, "Name") ?? string.Empty);
        _fileSystemFolder = ReadString(_working, "FileSystemFolder") ?? string.Empty;
        SortOrderName = ReadString(_working, "SortOrderName");
    }

    /// <summary> Called by the repository once <paramref name="writtenSnapshot"/> has actually been written to disk. </summary>
    internal void AcceptChanges(JsonObject writtenSnapshot)
    {
        _baseline = (JsonObject)writtenSnapshot.DeepClone();
        _working = (JsonObject)writtenSnapshot.DeepClone();
    }

    /// <summary> Replaces the working copy wholesale (e.g. with a backup's content) without touching the on-disk baseline until it is actually saved. </summary>
    internal void ReplaceWorkingState(JsonObject newState)
    {
        _working = (JsonObject)newState.DeepClone();
        (_characterName, _baseName) = SplitName(ReadString(_working, "Name") ?? string.Empty);
        _fileSystemFolder = ReadString(_working, "FileSystemFolder") ?? string.Empty;
        SortOrderName = ReadString(_working, "SortOrderName");
    }

    /// <summary> Applies a find-and-replace rule to the character and/or base name. Purely in-memory. Returns whether anything actually changed. </summary>
    public bool TryApplyRename(RenamePattern pattern)
    {
        var comparison = pattern.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var changed = false;

        if (pattern.Target is RenameTarget.CharacterName or RenameTarget.Both && _characterName.Contains(pattern.Find, comparison))
        {
            _characterName = _characterName.Replace(pattern.Find, pattern.Replace, comparison);
            changed = true;
        }

        if (pattern.Target is RenameTarget.BaseName or RenameTarget.Both && _baseName.Contains(pattern.Find, comparison))
        {
            _baseName = _baseName.Replace(pattern.Find, pattern.Replace, comparison);
            changed = true;
        }

        return changed;
    }

    /// <summary> Every value that differs between the on-disk baseline and the current working copy, for review before saving. </summary>
    public IReadOnlyList<PropertyDiff> GetPendingDiffs()
    {
        var diffs = new List<PropertyDiff>();
        DiffObjects(_baseline, BuildWorkingSnapshot(), string.Empty, diffs);
        return diffs;
    }

    private static void DiffObjects(JsonObject? before, JsonObject? after, string path, List<PropertyDiff> diffs)
    {
        var keys = (before?.Select(kv => kv.Key) ?? Enumerable.Empty<string>())
            .Union(after?.Select(kv => kv.Key) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

        foreach (var key in keys)
        {
            var beforeNode = before?[key];
            var afterNode = after?[key];
            var currentPath = path.Length == 0 ? key : $"{path}/{key}";

            if (beforeNode is JsonObject beforeObj && afterNode is JsonObject afterObj)
                DiffObjects(beforeObj, afterObj, currentPath, diffs);
            else if (!JsonNode.DeepEquals(beforeNode, afterNode))
                diffs.Add(new PropertyDiff(currentPath, beforeNode?.ToJsonString(), afterNode?.ToJsonString()));
        }
    }

    private static JsonObject? GetSection(JsonObject document, string sectionName)
        => document[sectionName] as JsonObject;

    private static JsonObject? GetEntry(JsonObject document, string sectionName, string entryName)
        => GetSection(document, sectionName)?[entryName] as JsonObject;

    private static string? ReadString(JsonObject document, string propertyName)
        => document[propertyName]?.GetValue<string>();

    private static JsonNode ParseForExistingType(JsonNode existingNode, string rawValue)
    {
        if (existingNode is JsonValue value)
        {
            if (value.TryGetValue<bool>(out _))
                return JsonValue.Create(bool.Parse(rawValue));
            if (value.TryGetValue<int>(out _))
                return JsonValue.Create(int.Parse(rawValue, CultureInfo.InvariantCulture));
            if (value.TryGetValue<long>(out _))
                return JsonValue.Create(long.Parse(rawValue, CultureInfo.InvariantCulture));
            if (value.TryGetValue<ulong>(out _))
                return JsonValue.Create(ulong.Parse(rawValue, CultureInfo.InvariantCulture));
            if (value.TryGetValue<float>(out _))
                return JsonValue.Create(float.Parse(rawValue, CultureInfo.InvariantCulture));
            if (value.TryGetValue<double>(out _))
                return JsonValue.Create(double.Parse(rawValue, CultureInfo.InvariantCulture));
            if (value.TryGetValue<decimal>(out _))
                return JsonValue.Create(decimal.Parse(rawValue, CultureInfo.InvariantCulture));
            if (value.TryGetValue<string>(out _))
                return JsonValue.Create(rawValue);
        }

        return JsonNode.Parse(rawValue) ?? throw new InvalidOperationException("Value could not be parsed.");
    }

    private static (string CharacterName, string BaseName) SplitName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (string.Empty, string.Empty);

        var match = CharacterPrefixPattern.Match(name);
        if (!match.Success)
            return (string.Empty, name.Trim());

        var character = match.Groups[1].Value.Trim();
        var designName = match.Groups[2].Value.Trim();
        return (character, string.IsNullOrWhiteSpace(designName) ? name.Trim() : designName);
    }
}
