using GlamourerBulkEditor.Core.Services;

namespace GlamourerBulkEditor.Core.Tests;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public BackupServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "gbe-backup-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void CreateBackupCopiesFileIntoDatedBackupFolder()
    {
        var sourceFile = Path.Combine(_tempDirectory, "design.json");
        File.WriteAllText(sourceFile, "{\"Name\":\"Original\"}");

        var backupPath = new BackupService().CreateBackup(sourceFile);

        Assert.True(File.Exists(backupPath));
        Assert.Equal("{\"Name\":\"Original\"}", File.ReadAllText(backupPath));
        Assert.Contains(Path.Combine(_tempDirectory, ".backups"), backupPath);
        // The original file must be untouched by taking a backup.
        Assert.True(File.Exists(sourceFile));
    }

    [Fact]
    public void CreateBackupDoesNotOverwritePreviousBackupsOfTheSameFile()
    {
        var sourceFile = Path.Combine(_tempDirectory, "design.json");
        var service = new BackupService();

        File.WriteAllText(sourceFile, "{\"Name\":\"Version1\"}");
        var firstBackup = service.CreateBackup(sourceFile);

        File.WriteAllText(sourceFile, "{\"Name\":\"Version2\"}");
        var secondBackup = service.CreateBackup(sourceFile);

        Assert.NotEqual(firstBackup, secondBackup);
        Assert.Equal("{\"Name\":\"Version1\"}", File.ReadAllText(firstBackup));
        Assert.Equal("{\"Name\":\"Version2\"}", File.ReadAllText(secondBackup));
    }

    [Fact]
    public void CreateBackupThrowsForMissingSourceFile()
    {
        var missingFile = Path.Combine(_tempDirectory, "does-not-exist.json");

        Assert.Throws<FileNotFoundException>(() => new BackupService().CreateBackup(missingFile));
    }
}
