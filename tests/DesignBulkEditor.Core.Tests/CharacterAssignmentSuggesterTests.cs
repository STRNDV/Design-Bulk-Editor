using System.Text.Json.Nodes;
using DesignBulkEditor.Core.Models;
using DesignBulkEditor.Core.Services;

namespace DesignBulkEditor.Core.Tests;

public class CharacterAssignmentSuggesterTests
{
    private readonly CharacterAssignmentSuggester _suggester = new();

    private static GlamourerDesign CreateDesign(string name, string? fileSystemFolder = null, string? identifier = null)
    {
        var json = new JsonObject
        {
            ["Identifier"] = identifier ?? Guid.NewGuid().ToString(),
            ["Name"] = name,
            ["FileSystemFolder"] = fileSystemFolder,
        };
        return new GlamourerDesign("C:/designs/design.json", json);
    }

    [Fact]
    public void SuggestBestPrefersAutomationOverNamePrefixAndFolder()
    {
        var design = CreateDesign("(Name Prefix Character) Casual", fileSystemFolder: "Folder Character/Casual", identifier: "design-1");
        var automationMap = new Dictionary<string, string> { ["design-1"] = "Automation Character" };

        var best = _suggester.SuggestBest(design, automationMap);

        Assert.NotNull(best);
        Assert.Equal("Automation Character", best.CharacterName);
        Assert.Equal(CharacterAssignmentSuggestion.SourceGlamourerAutomation, best.Source);
    }

    [Fact]
    public void SuggestBestPrefersNamePrefixOverFolderWhenNoAutomationMatch()
    {
        var design = CreateDesign("(Name Prefix Character) Casual", fileSystemFolder: "Folder Character/Casual");

        var best = _suggester.SuggestBest(design, automationMap: new Dictionary<string, string>());

        Assert.NotNull(best);
        Assert.Equal("Name Prefix Character", best.CharacterName);
        Assert.Equal(CharacterAssignmentSuggestion.SourceNamePrefix, best.Source);
    }

    [Fact]
    public void SuggestBestFallsBackToFolderWhenNoOtherSignalExists()
    {
        var design = CreateDesign("Plain Name", fileSystemFolder: "Folder Character/Casual");

        var best = _suggester.SuggestBest(design, automationMap: new Dictionary<string, string>());

        Assert.NotNull(best);
        Assert.Equal("Folder Character", best.CharacterName);
        Assert.Equal(CharacterAssignmentSuggestion.SourceFileSystemFolder, best.Source);
    }

    [Fact]
    public void SuggestBestReturnsNullWhenNoSignalMatches()
    {
        var design = CreateDesign("Plain Name");

        var best = _suggester.SuggestBest(design, automationMap: new Dictionary<string, string>());

        Assert.Null(best);
    }

    [Fact]
    public void SuggestAllReturnsEverySignalInPriorityOrder()
    {
        var design = CreateDesign("(Name Prefix Character) Casual", fileSystemFolder: "Folder Character/Casual", identifier: "design-1");
        var automationMap = new Dictionary<string, string> { ["design-1"] = "Automation Character" };

        var all = _suggester.SuggestAll(design, automationMap);

        Assert.Equal(3, all.Count);
        Assert.Equal(CharacterAssignmentSuggestion.SourceGlamourerAutomation, all[0].Source);
        Assert.Equal(CharacterAssignmentSuggestion.SourceNamePrefix, all[1].Source);
        Assert.Equal(CharacterAssignmentSuggestion.SourceFileSystemFolder, all[2].Source);
    }
}
