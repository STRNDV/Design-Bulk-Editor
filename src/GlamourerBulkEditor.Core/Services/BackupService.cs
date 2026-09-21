namespace GlamourerBulkEditor.Core.Services;

/// <summary> Creates a timestamped copy of a design file before it gets overwritten. </summary>
public sealed class BackupService
{
    private const string BackupFolderName = ".backups";

    /// <summary> Copies <paramref name="sourceFile"/> into a dated backup folder next to it and returns the backup's path. </summary>
    public string CreateBackup(string sourceFile)
    {
        if (!File.Exists(sourceFile))
            throw new FileNotFoundException("Cannot back up a file that does not exist.", sourceFile);

        var directory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException($"'{sourceFile}' has no parent directory.");

        var backupDirectory = Path.Combine(directory, BackupFolderName, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(backupDirectory);

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(sourceFile);
        var extension = Path.GetExtension(sourceFile);
        var timestamp = DateTime.UtcNow.ToString("HHmmss.fff");

        var backupPath = Path.Combine(backupDirectory, $"{nameWithoutExtension}.{timestamp}{extension}");
        var attempt = 0;
        while (File.Exists(backupPath))
        {
            attempt++;
            backupPath = Path.Combine(backupDirectory, $"{nameWithoutExtension}.{timestamp}-{attempt}{extension}");
        }

        File.Copy(sourceFile, backupPath, overwrite: false);
        return backupPath;
    }
}
