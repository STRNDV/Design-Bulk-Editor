using System.Text.Json;
using System.Text.Json.Nodes;
using GlamourerBulkEditor.Core.Models;

namespace GlamourerBulkEditor.Core.Services;

/// <summary>
/// Reads and writes Glamourer design JSON files. Every write is preceded by a
/// backup of the previous content and performed atomically (write to a temp
/// file in the same directory, then replace), and a failure on one design
/// never stops the rest of a batch from being attempted.
/// </summary>
public sealed class DesignRepository
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly BackupService _backupService;

    public DesignRepository(BackupService backupService)
        => _backupService = backupService;

    public async Task<IReadOnlyList<GlamourerDesign>> LoadAsync(string configDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectory);

        var designsDirectory = Path.Combine(configDirectory, "designs");
        if (!Directory.Exists(designsDirectory))
            throw new DirectoryNotFoundException($"No 'designs' folder found under '{configDirectory}'.");

        var designs = new List<GlamourerDesign>();
        foreach (var file in Directory.EnumerateFiles(designsDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var stream = File.OpenRead(file);
            if (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) is JsonObject obj)
                designs.Add(new GlamourerDesign(file, obj));
        }

        return designs;
    }

    /// <summary> Saves one design. Never throws for expected failure modes; the result carries the outcome. </summary>
    public async Task<DesignSaveResult> SaveAsync(GlamourerDesign design, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(design.SourceFile);
        if (string.IsNullOrEmpty(directory))
            return DesignSaveResult.Failed(design.SourceFile, $"'{design.SourceFile}' has no parent directory.");

        try
        {
            var snapshot = design.BuildWorkingSnapshot();
            var json = snapshot.ToJsonString(WriteOptions);

            if (File.Exists(design.SourceFile))
                _backupService.CreateBackup(design.SourceFile);

            var tempFile = Path.Combine(directory, $".{Path.GetFileName(design.SourceFile)}.{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(tempFile, json, cancellationToken);

            if (File.Exists(design.SourceFile))
                File.Replace(tempFile, design.SourceFile, destinationBackupFileName: null);
            else
                File.Move(tempFile, design.SourceFile);

            design.AcceptChanges(snapshot);
            return DesignSaveResult.Ok(design.SourceFile);
        }
        catch (Exception ex)
        {
            return DesignSaveResult.Failed(design.SourceFile, ex.Message);
        }
    }

    /// <summary> Saves several designs. A failure on one design never stops the others from being attempted. </summary>
    public async Task<IReadOnlyList<DesignSaveResult>> SaveManyAsync(IEnumerable<GlamourerDesign> designs, CancellationToken cancellationToken = default)
    {
        var results = new List<DesignSaveResult>();
        foreach (var design in designs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await SaveAsync(design, cancellationToken));
        }

        return results;
    }
}
