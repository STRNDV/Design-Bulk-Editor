using GlamourerBulkEditor.Core.Services;

namespace GlamourerBulkEditor.Core.Tests;

public class AutomationConfigReaderTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly AutomationConfigReader _reader = new();

    public AutomationConfigReaderTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "gbe-automation-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    private void WriteAutomationJson(string content)
        => File.WriteAllText(Path.Combine(_tempDirectory, "automation.json"), content);

    [Fact]
    public async Task ReturnsEmptyMapWhenFileIsMissing()
    {
        var map = await _reader.ReadDesignCharacterMapAsync(_tempDirectory);

        Assert.Empty(map);
    }

    [Fact]
    public async Task ParsesRealisticGlamourerAutomationLayout()
    {
        // Mirrors the actual shape written by Glamourer 1.7.1.3's Automation feature.
        WriteAutomationJson("""
        {
            "Version": 1,
            "Data": [
                {
                    "Name": "Tori Siresa",
                    "Identifier": { "Type": "Player", "PlayerName": "Tori Siresa", "HomeWorld": 67 },
                    "Enabled": true,
                    "Designs": [
                        { "Design": "8d3a7aba-1c05-48c7-a758-90726dd5abe8", "Type": 31, "Conditions": { "JobGroup": 1 } }
                    ]
                },
                {
                    "Name": "Asta Camoa",
                    "Identifier": { "Type": "Player", "PlayerName": "Asta Camoa", "HomeWorld": 65535 },
                    "Enabled": true,
                    "Designs": [
                        { "Design": "4004bee7-d5fd-4e15-a6e3-5c75bf40038e", "Type": 31, "Conditions": { "JobGroup": 1 } }
                    ]
                }
            ]
        }
        """);

        var map = await _reader.ReadDesignCharacterMapAsync(_tempDirectory);

        Assert.Equal(2, map.Count);
        Assert.Equal("Tori Siresa", map["8d3a7aba-1c05-48c7-a758-90726dd5abe8"]);
        Assert.Equal("Asta Camoa", map["4004bee7-d5fd-4e15-a6e3-5c75bf40038e"]);
    }

    [Fact]
    public async Task SkipsNonPlayerIdentifiers()
    {
        WriteAutomationJson("""
        {
            "Version": 1,
            "Data": [
                {
                    "Name": "Some NPC Set",
                    "Identifier": { "Type": "Npc", "PlayerName": "Should Not Be Used" },
                    "Enabled": true,
                    "Designs": [ { "Design": "11111111-1111-1111-1111-111111111111", "Type": 31 } ]
                }
            ]
        }
        """);

        var map = await _reader.ReadDesignCharacterMapAsync(_tempDirectory);

        Assert.Empty(map);
    }

    [Fact]
    public async Task ReturnsEmptyMapForUnrecognisedVersion()
    {
        WriteAutomationJson("""{ "Version": 99, "Data": [] }""");

        var map = await _reader.ReadDesignCharacterMapAsync(_tempDirectory);

        Assert.Empty(map);
    }

    [Fact]
    public async Task ReturnsEmptyMapForMalformedJsonWithoutThrowing()
    {
        WriteAutomationJson("{ this is not valid json ");

        var map = await _reader.ReadDesignCharacterMapAsync(_tempDirectory);

        Assert.Empty(map);
    }

    [Fact]
    public async Task ReturnsEmptyMapWhenVersionFieldIsMissing()
    {
        WriteAutomationJson("""{ "Data": [] }""");

        var map = await _reader.ReadDesignCharacterMapAsync(_tempDirectory);

        Assert.Empty(map);
    }
}
