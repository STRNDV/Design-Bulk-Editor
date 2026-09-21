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
        // Shape verified against a real Glamourer 1.7.1.3 automation.json (structure only -
        // the names and GUIDs below are fictional placeholders, not real player data).
        WriteAutomationJson("""
        {
            "Version": 1,
            "Data": [
                {
                    "Name": "Aeryn Vale",
                    "Identifier": { "Type": "Player", "PlayerName": "Aeryn Vale", "HomeWorld": 67 },
                    "Enabled": true,
                    "Designs": [
                        { "Design": "11111111-1111-1111-1111-111111111111", "Type": 31, "Conditions": { "JobGroup": 1 } }
                    ]
                },
                {
                    "Name": "Bryn Solari",
                    "Identifier": { "Type": "Player", "PlayerName": "Bryn Solari", "HomeWorld": 65535 },
                    "Enabled": true,
                    "Designs": [
                        { "Design": "22222222-2222-2222-2222-222222222222", "Type": 31, "Conditions": { "JobGroup": 1 } }
                    ]
                }
            ]
        }
        """);

        var map = await _reader.ReadDesignCharacterMapAsync(_tempDirectory);

        Assert.Equal(2, map.Count);
        Assert.Equal("Aeryn Vale", map["11111111-1111-1111-1111-111111111111"]);
        Assert.Equal("Bryn Solari", map["22222222-2222-2222-2222-222222222222"]);
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
