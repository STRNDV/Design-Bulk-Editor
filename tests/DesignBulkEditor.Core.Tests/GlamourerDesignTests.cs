using System.Text.Json.Nodes;
using DesignBulkEditor.Core.Models;

namespace DesignBulkEditor.Core.Tests;

public class GlamourerDesignTests
{
    private static GlamourerDesign CreateDesign(string name = "(Aeryn Vale) Casual", string? fileSystemFolder = "Outfits")
    {
        var json = new JsonObject
        {
            ["Identifier"] = Guid.NewGuid().ToString(),
            ["Name"] = name,
            ["FileSystemFolder"] = fileSystemFolder,
            ["Customize"] = new JsonObject
            {
                ["Hairstyle"] = new JsonObject
                {
                    ["Value"] = 5,
                    ["Enabled"] = true,
                },
                ["SkinColor"] = new JsonObject
                {
                    ["Value"] = "ff00ff",
                    ["Enabled"] = false,
                },
            },
        };

        return new GlamourerDesign("C:/designs/design.json", json);
    }

    [Theory]
    [InlineData("(Aeryn Vale) Casual", "Aeryn Vale", "Casual")]
    [InlineData("(Aeryn Vale)   Casual  ", "Aeryn Vale", "Casual")]
    [InlineData("Casual Only", "", "Casual Only")]
    [InlineData("", "", "")]
    [InlineData("(Just A Character)", "Just A Character", "(Just A Character)")]
    public void SplitsCharacterPrefixFromDesignName(string rawName, string expectedCharacter, string expectedBaseName)
    {
        var design = CreateDesign(rawName);

        Assert.Equal(expectedCharacter, design.CharacterName);
        Assert.Equal(expectedBaseName, design.BaseName);
    }

    [Fact]
    public void ReconstructedNameRoundTripsAfterEditingParts()
    {
        var design = CreateDesign("(Aeryn Vale) Casual");

        design.CharacterName = "Bryn Solari";
        design.BaseName = "Formal";

        Assert.Equal("(Bryn Solari) Formal", design.ReconstructedName);
    }

    [Fact]
    public void DesignWithoutCharacterPrefixReconstructsWithoutParentheses()
    {
        var design = CreateDesign("Casual Only");

        Assert.Equal("Casual Only", design.ReconstructedName);
        Assert.Equal("Unassigned", design.DisplayCategory);
    }

    [Fact]
    public void HasPendingChangesIsFalseRightAfterLoad()
    {
        var design = CreateDesign();

        Assert.False(design.HasPendingChanges);
    }

    [Fact]
    public void HasPendingChangesBecomesTrueAfterRenamingCharacter()
    {
        var design = CreateDesign();

        design.CharacterName = "Someone Else";

        Assert.True(design.HasPendingChanges);
    }

    [Fact]
    public void HasPendingChangesBecomesTrueAfterStagingAPropertyEdit()
    {
        // Regression test for the original app's bug: TrySetPropertyValue mutated the
        // live document directly, so comparing the rebuilt snapshot against that same
        // mutated document made HasPendingChanges silently report false.
        var design = CreateDesign();

        var staged = design.TrySetPropertyValue("Customize", "Hairstyle", "Value", "42", out var error);

        Assert.True(staged);
        Assert.Null(error);
        Assert.True(design.HasPendingChanges);
    }

    [Fact]
    public void DiscardChangesRevertsStagedPropertyEditAndRename()
    {
        var design = CreateDesign();

        design.CharacterName = "Someone Else";
        design.TrySetPropertyValue("Customize", "Hairstyle", "Value", "42", out _);
        Assert.True(design.HasPendingChanges);

        design.DiscardChanges();

        Assert.False(design.HasPendingChanges);
        Assert.Equal("Aeryn Vale", design.CharacterName);
        Assert.Equal("5", design.GetPropertyValueText("Customize", "Hairstyle", "Value"));
    }

    [Theory]
    [InlineData("Customize", "Hairstyle", "Value", "42", "42")]
    [InlineData("Customize", "Hairstyle", "Enabled", "false", "false")]
    [InlineData("Customize", "SkinColor", "Value", "00ff00", "\"00ff00\"")]
    public void TrySetPropertyValuePreservesOriginalJsonType(string section, string entry, string property, string rawValue, string expectedJson)
    {
        var design = CreateDesign();

        var success = design.TrySetPropertyValue(section, entry, property, rawValue, out var error);

        Assert.True(success);
        Assert.Null(error);
        Assert.Equal(expectedJson, design.GetPropertyValueText(section, entry, property));
    }

    [Fact]
    public void TrySetPropertyValueFailsForUnknownEntryWithoutThrowing()
    {
        var design = CreateDesign();

        var success = design.TrySetPropertyValue("Customize", "DoesNotExist", "Value", "1", out var error);

        Assert.False(success);
        Assert.NotNull(error);
    }

    [Fact]
    public void TrySetPropertyValueFailsForUnparsableNumberWithoutThrowing()
    {
        var design = CreateDesign();

        var success = design.TrySetPropertyValue("Customize", "Hairstyle", "Value", "not-a-number", out var error);

        Assert.False(success);
        Assert.NotNull(error);
        // The original, unedited value must still be intact.
        Assert.Equal("5", design.GetPropertyValueText("Customize", "Hairstyle", "Value"));
    }

    [Fact]
    public void BuildWorkingSnapshotOmitsBlankFileSystemFolder()
    {
        var design = CreateDesign(fileSystemFolder: "Outfits");

        design.FileSystemFolder = "   ";
        var snapshot = design.BuildWorkingSnapshot();

        Assert.False(snapshot.ContainsKey("FileSystemFolder"));
    }
}
