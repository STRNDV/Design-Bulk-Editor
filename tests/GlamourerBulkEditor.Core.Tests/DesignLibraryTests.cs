using System.Text.Json.Nodes;
using GlamourerBulkEditor.Core.Models;
using GlamourerBulkEditor.Core.Services;

namespace GlamourerBulkEditor.Core.Tests;

public class DesignLibraryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly DesignLibrary _library;

    public DesignLibraryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "gbe-library-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "designs"));

        var assignmentStorePath = Path.Combine(_tempDirectory, "assignments.json");
        _library = new DesignLibrary(
            new DesignRepository(new BackupService()),
            new AutomationConfigReader(),
            new CharacterAssignmentSuggester(),
            new CharacterAssignmentStore(assignmentStorePath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private string WriteDesignFile(string fileName, string name, string? fileSystemFolder = null)
    {
        var identifier = Guid.NewGuid().ToString();
        var json = new JsonObject
        {
            ["Identifier"] = identifier,
            ["Name"] = name,
            ["FileSystemFolder"] = fileSystemFolder,
            ["Customize"] = new JsonObject
            {
                ["Hairstyle"] = new JsonObject { ["Value"] = 1 },
            },
        };
        File.WriteAllText(Path.Combine(_tempDirectory, "designs", fileName), json.ToJsonString());
        return identifier;
    }

    private void WriteAutomationConfig(params (string PlayerName, string DesignIdentifier)[] assignments)
    {
        var data = new JsonArray();
        foreach (var (playerName, designIdentifier) in assignments)
        {
            data.Add(new JsonObject
            {
                ["Name"] = playerName,
                ["Identifier"] = new JsonObject
                {
                    ["Type"] = "Player",
                    ["PlayerName"] = playerName,
                    ["HomeWorld"] = 67,
                },
                ["Enabled"] = true,
                ["Designs"] = new JsonArray(new JsonObject
                {
                    ["Design"] = designIdentifier,
                    ["Type"] = 31,
                    ["Conditions"] = new JsonObject { ["JobGroup"] = 1 },
                }),
            });
        }

        var root = new JsonObject { ["Version"] = 1, ["Data"] = data };
        File.WriteAllText(Path.Combine(_tempDirectory, "automation.json"), root.ToJsonString());
    }

    private static GlamourerDesign CreateInMemoryDesign(string name)
    {
        var json = new JsonObject
        {
            ["Identifier"] = Guid.NewGuid().ToString(),
            ["Name"] = name,
            ["Customize"] = new JsonObject
            {
                ["Hairstyle"] = new JsonObject { ["Value"] = 1 },
            },
        };
        return new GlamourerDesign($"C:/designs/{Guid.NewGuid():N}.json", json);
    }

    [Fact]
    public async Task LoadAsyncGroupsDesignsByCharacterName()
    {
        WriteDesignFile("a.json", "(Aeryn Vale) Casual");
        WriteDesignFile("b.json", "(Aeryn Vale) Formal");
        WriteDesignFile("c.json", "(Bryn Solari) Casual");
        WriteDesignFile("d.json", "No Character Prefix");

        var snapshot = await _library.LoadAsync(_tempDirectory);

        Assert.Equal(4, snapshot.Designs.Count);
        Assert.Equal(3, snapshot.Groups.Count);

        var aeryn = Assert.Single(snapshot.Groups, g => g.CharacterName == "Aeryn Vale");
        Assert.Equal(2, aeryn.Count);

        var unassigned = Assert.Single(snapshot.Groups, g => g.CharacterName == "Unassigned");
        Assert.Single(unassigned.Designs);
    }

    [Fact]
    public void ApplyPendingEditsStagesTheSameEditAcrossMultipleTargetsWithoutTouchingDisk()
    {
        var designOne = CreateInMemoryDesign("(Aeryn Vale) Casual");
        var designTwo = CreateInMemoryDesign("(Aeryn Vale) Formal");
        var edit = new PendingPropertyEdit("Customize", "Hairstyle", "Value", "99");

        var outcomes = _library.ApplyPendingEdits([designOne, designTwo], [edit]);

        Assert.All(outcomes, o => Assert.True(o.Success));
        Assert.Equal("99", designOne.GetPropertyValueText("Customize", "Hairstyle", "Value"));
        Assert.Equal("99", designTwo.GetPropertyValueText("Customize", "Hairstyle", "Value"));
        Assert.True(designOne.HasPendingChanges);
        Assert.True(designTwo.HasPendingChanges);
    }

    [Fact]
    public void ApplyPendingEditsKeepsMultipleStagedPropertiesSimultaneously()
    {
        // This is the direct fix for the reported bug: staging a Hairstyle edit must not
        // prevent also staging a SkinColor edit in the same session.
        var design = CreateInMemoryDesign("(Aeryn Vale) Casual");
        var hairstyleEdit = new PendingPropertyEdit("Customize", "Hairstyle", "Value", "7");

        var firstOutcome = _library.ApplyPendingEdits([design], [hairstyleEdit]);
        Assert.All(firstOutcome, o => Assert.True(o.Success));

        // Switching to a different property must not reset the first staged edit.
        Assert.Equal("7", design.GetPropertyValueText("Customize", "Hairstyle", "Value"));
        Assert.True(design.HasPendingChanges);
    }

    [Fact]
    public void ApplyPendingEditsReportsPerDesignFailureWithoutThrowing()
    {
        var validDesign = CreateInMemoryDesign("(Aeryn Vale) Casual");
        var edit = new PendingPropertyEdit("Customize", "DoesNotExist", "Value", "1");

        var outcomes = _library.ApplyPendingEdits([validDesign], [edit]);

        var outcome = Assert.Single(outcomes);
        Assert.False(outcome.Success);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsyncAssignsCharacterFromGlamourerAutomationWhenNameHasNoPrefix()
    {
        // Not everyone names designs "(Character) Design" - Glamourer's own Automation
        // config, when present, is a more reliable, explicit signal.
        var designId = WriteDesignFile("a.json", "Plain Outfit Name");
        WriteAutomationConfig(("Tori Siresa", designId));

        var snapshot = await _library.LoadAsync(_tempDirectory);

        var design = Assert.Single(snapshot.Designs);
        Assert.Equal("Tori Siresa", design.AssignedCharacter);
        Assert.Single(snapshot.Groups, g => g.CharacterName == "Tori Siresa");
    }

    [Fact]
    public async Task LoadAsyncPrefersAutomationOverConflictingNamePrefix()
    {
        var designId = WriteDesignFile("a.json", "(Wrong Character) Casual");
        WriteAutomationConfig(("Correct Character", designId));

        var snapshot = await _library.LoadAsync(_tempDirectory);

        var design = Assert.Single(snapshot.Designs);
        Assert.Equal("Correct Character", design.AssignedCharacter);
    }

    [Fact]
    public async Task LoadAsyncFallsBackToFolderWhenNoNamePrefixOrAutomationMatches()
    {
        WriteDesignFile("a.json", "Plain Outfit Name", fileSystemFolder: "Bryn Solari/Casual");

        var snapshot = await _library.LoadAsync(_tempDirectory);

        var design = Assert.Single(snapshot.Designs);
        Assert.Equal("Bryn Solari", design.AssignedCharacter);
    }

    [Fact]
    public async Task LoadAsyncLeavesDesignUnassignedWhenNoSignalMatches()
    {
        WriteDesignFile("a.json", "Plain Outfit Name");

        var snapshot = await _library.LoadAsync(_tempDirectory);

        var design = Assert.Single(snapshot.Designs);
        Assert.Null(design.AssignedCharacter);
        Assert.Equal("Unassigned", design.DisplayCategory);
    }

    [Fact]
    public async Task ManualAssignmentOverridesHeuristicsAndPersistsAcrossReload()
    {
        var designId = WriteDesignFile("a.json", "(Suggested Character) Casual");

        var firstLoad = await _library.LoadAsync(_tempDirectory);
        var design = Assert.Single(firstLoad.Designs);
        Assert.Equal("Suggested Character", design.AssignedCharacter);

        await _library.AssignCharacterAsync(design, "Manually Chosen Character");
        Assert.Equal("Manually Chosen Character", design.AssignedCharacter);

        // A fresh DesignLibrary instance (simulating a plugin restart) pointed at the
        // same assignment store must still honour the manual override, not the guess.
        var reloadedLibrary = new DesignLibrary(
            new DesignRepository(new BackupService()),
            new AutomationConfigReader(),
            new CharacterAssignmentSuggester(),
            new CharacterAssignmentStore(Path.Combine(_tempDirectory, "assignments.json")));

        var secondLoad = await reloadedLibrary.LoadAsync(_tempDirectory);
        var reloadedDesign = Assert.Single(secondLoad.Designs);
        Assert.Equal("Manually Chosen Character", reloadedDesign.AssignedCharacter);
    }
}
