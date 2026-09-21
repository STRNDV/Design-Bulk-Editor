using System.Text.Json.Nodes;
using DesignBulkEditor.Core.Models;
using DesignBulkEditor.Core.Services;

namespace DesignBulkEditor.Core.Tests;

public class DesignRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly DesignRepository _repository = new(new BackupService());

    public DesignRepositoryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "gbe-repo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private GlamourerDesign WriteAndLoadDesign(string fileName, string characterAndDesignName = "(Aeryn Vale) Casual")
    {
        var path = Path.Combine(_tempDirectory, fileName);
        var json = new JsonObject
        {
            ["Identifier"] = Guid.NewGuid().ToString(),
            ["Name"] = characterAndDesignName,
        };
        File.WriteAllText(path, json.ToJsonString());
        return new GlamourerDesign(path, (JsonObject)JsonNode.Parse(File.ReadAllText(path))!);
    }

    [Fact]
    public async Task SaveAsyncWritesValidJsonAndClearsPendingChanges()
    {
        var design = WriteAndLoadDesign("a.json");
        design.CharacterName = "Bryn Solari";

        var result = await _repository.SaveAsync(design);

        Assert.True(result.Success);
        Assert.False(design.HasPendingChanges);

        var written = (JsonObject)JsonNode.Parse(await File.ReadAllTextAsync(design.SourceFile))!;
        Assert.Equal("(Bryn Solari) Casual", written["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SaveAsyncCreatesBackupOfPreviousContentBeforeOverwriting()
    {
        var design = WriteAndLoadDesign("a.json");
        var originalContent = await File.ReadAllTextAsync(design.SourceFile);

        design.CharacterName = "Someone New";
        await _repository.SaveAsync(design);

        var backupDir = Path.Combine(_tempDirectory, ".backups");
        Assert.True(Directory.Exists(backupDir));
        var backupFiles = Directory.GetFiles(backupDir, "*.json", SearchOption.AllDirectories);
        Assert.Single(backupFiles);
        Assert.Equal(originalContent, await File.ReadAllTextAsync(backupFiles[0]));
    }

    [Fact]
    public async Task SaveAsyncLeavesNoTemporaryFilesBehindOnSuccess()
    {
        var design = WriteAndLoadDesign("a.json");
        design.CharacterName = "Someone New";

        await _repository.SaveAsync(design);

        var leftoverTempFiles = Directory.GetFiles(_tempDirectory, "*.tmp");
        Assert.Empty(leftoverTempFiles);
    }

    [Fact]
    public async Task SaveManyAsyncContinuesPastAFailingDesign()
    {
        var goodDesignOne = WriteAndLoadDesign("good1.json");
        var goodDesignTwo = WriteAndLoadDesign("good2.json");
        goodDesignOne.CharacterName = "First Fix";
        goodDesignTwo.CharacterName = "Second Fix";

        // Point a third design at a directory that does not exist so its write fails.
        var brokenPath = Path.Combine(_tempDirectory, "missing-subfolder", "broken.json");
        var brokenJson = new JsonObject { ["Identifier"] = Guid.NewGuid().ToString(), ["Name"] = "(X) Y" };
        var brokenDesign = new GlamourerDesign(brokenPath, brokenJson);
        brokenDesign.CharacterName = "Should Fail";

        var results = await _repository.SaveManyAsync([goodDesignOne, brokenDesign, goodDesignTwo]);

        Assert.Equal(3, results.Count);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.NotNull(results[1].ErrorMessage);
        Assert.True(results[2].Success);

        // The two good designs must actually have been persisted despite the failure in between.
        Assert.False(goodDesignOne.HasPendingChanges);
        Assert.False(goodDesignTwo.HasPendingChanges);
    }
}
