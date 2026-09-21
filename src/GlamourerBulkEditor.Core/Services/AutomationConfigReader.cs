using System.Text.Json.Nodes;

namespace GlamourerBulkEditor.Core.Services;

/// <summary>
/// Best-effort reader for Glamourer's own <c>automation.json</c>. This is an
/// undocumented, private file of Glamourer's - not part of the official
/// Glamourer.Api - but it is the one place Glamourer itself records an
/// explicit character-to-design association (via its "Automation" feature),
/// rather than a naming convention we have to guess at.
///
/// Because the format is undocumented and can change without notice between
/// Glamourer versions, every read is defensive: a missing file, an
/// unrecognised "Version", or any malformed entry is treated as "no data
/// available", never as an error the caller has to handle. This is a
/// best-effort enrichment, not a dependency anything else relies on.
/// </summary>
public sealed class AutomationConfigReader
{
    private const int SupportedVersion = 1;

    /// <summary> Maps a design's GUID (as it appears in a design file's "Identifier") to the player character name it is wired to via Glamourer's Automation feature. </summary>
    public async Task<IReadOnlyDictionary<string, string>> ReadDesignCharacterMapAsync(string configDirectory, CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var path = Path.Combine(configDirectory, "automation.json");
            if (!File.Exists(path))
                return map;

            await using var stream = File.OpenRead(path);
            if (await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) is not JsonObject root)
                return map;

            if (root["Version"] is not { } versionNode || versionNode.GetValue<int>() != SupportedVersion)
                return map;

            if (root["Data"] is not JsonArray sets)
                return map;

            foreach (var setNode in sets)
            {
                if (setNode is not JsonObject set)
                    continue;

                if (set["Identifier"] is not JsonObject identifier)
                    continue;

                if (identifier["Type"]?.GetValue<string>() != "Player")
                    continue;

                var playerName = identifier["PlayerName"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(playerName))
                    continue;

                if (set["Designs"] is not JsonArray designs)
                    continue;

                foreach (var designNode in designs)
                {
                    var designId = (designNode as JsonObject)?["Design"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(designId))
                        map.TryAdd(designId, playerName);
                }
            }

            return map;
        }
        catch
        {
            // Any parsing surprise means we simply have no automation-derived suggestions -
            // an undocumented third-party file must never be able to crash design loading.
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
