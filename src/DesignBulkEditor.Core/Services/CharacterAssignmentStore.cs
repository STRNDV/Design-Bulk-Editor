using System.Text.Json;

namespace DesignBulkEditor.Core.Services;

/// <summary>
/// Persists the user's own, explicit design-to-character assignments in a
/// small local file the plugin owns - independent of any naming convention,
/// folder structure, or Glamourer's Automation config. An assignment made
/// here always takes priority over a heuristic suggestion.
/// </summary>
public sealed class CharacterAssignmentStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private Dictionary<string, string> _assignments = new(StringComparer.OrdinalIgnoreCase);

    public CharacterAssignmentStore(string filePath)
        => _filePath = filePath;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            _assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken);
            _assignments = loaded is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // A corrupted assignment file must never block loading the design library.
            _assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? Get(string designIdentifier)
        => _assignments.TryGetValue(designIdentifier, out var character) ? character : null;

    public async Task SetAsync(string designIdentifier, string characterName, CancellationToken cancellationToken = default)
    {
        _assignments[designIdentifier] = characterName;
        await SaveAsync(cancellationToken);
    }

    public async Task ClearAsync(string designIdentifier, CancellationToken cancellationToken = default)
    {
        _assignments.Remove(designIdentifier);
        await SaveAsync(cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempFile = Path.Combine(directory ?? ".", $".{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(_assignments, WriteOptions);
        await File.WriteAllTextAsync(tempFile, json, cancellationToken);

        if (File.Exists(_filePath))
            File.Replace(tempFile, _filePath, destinationBackupFileName: null);
        else
            File.Move(tempFile, _filePath);
    }
}
