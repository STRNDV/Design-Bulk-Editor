using System.Text.Json.Nodes;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Newtonsoft.Json.Linq;

namespace GlamourerBulkEditor.Plugin;

public sealed record GlamourerApiCallResult(bool Success, string? ErrorMessage)
{
    public static GlamourerApiCallResult Ok() => new(true, null);

    public static GlamourerApiCallResult Failed(string message) => new(false, message);
}

/// <summary>
/// Thin wrapper around the official Glamourer.Api IPC (NuGet package
/// "Glamourer.Api", MIT-licensed, maintained by Ottermandias alongside
/// Glamourer itself). Every call is defensive: if Glamourer is not
/// installed, not loaded, or the IPC handshake fails for any reason,
/// <see cref="IsAvailable"/> becomes false and callers get a clear failure
/// result instead of an exception - offline design editing must keep
/// working regardless of whether Glamourer is even running.
///
/// Live preview uses ApplyState rather than ApplyDesign, because ApplyState
/// accepts an arbitrary, not-yet-saved JSON state directly - exactly what is
/// needed to preview a staged, unsaved edit before committing it to disk.
/// It is applied with ApplyFlag.Once so it never gets written into
/// Glamourer's own automation state, and RevertState puts the actor back to
/// its normal state afterwards.
/// </summary>
public sealed class GlamourerApiClient
{
    private readonly IPluginLog _log;
    private readonly ApiVersion _apiVersionSubscriber;
    private readonly ApplyState _applyStateSubscriber;
    private readonly RevertState _revertStateSubscriber;

    public GlamourerApiClient(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        _log = log;
        _apiVersionSubscriber = new ApiVersion(pluginInterface);
        _applyStateSubscriber = new ApplyState(pluginInterface);
        _revertStateSubscriber = new RevertState(pluginInterface);

        Refresh();
    }

    public bool IsAvailable { get; private set; }

    public (int Major, int Minor)? ApiVersionNumber { get; private set; }

    /// <summary> Re-checks whether Glamourer is currently reachable. Safe to call repeatedly, e.g. whenever the UI opens. </summary>
    public void Refresh()
    {
        try
        {
            ApiVersionNumber = _apiVersionSubscriber.Invoke();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            ApiVersionNumber = null;
            IsAvailable = false;
            _log.Information($"Glamourer live preview unavailable: {ex.Message}");
        }
    }

    /// <summary> Pushes a design's current working snapshot onto an actor as a temporary preview. Never touches disk. </summary>
    public GlamourerApiCallResult ApplyPreview(JsonObject workingSnapshot, int objectIndex)
    {
        if (!IsAvailable)
            return GlamourerApiCallResult.Failed("Glamourer is not available.");

        try
        {
            var state = JObject.Parse(workingSnapshot.ToJsonString());
            var result = _applyStateSubscriber.Invoke(
                state,
                objectIndex,
                key: 0,
                flags: ApplyFlag.Once | ApplyFlag.Equipment | ApplyFlag.Customization);

            return result is GlamourerApiEc.Success or GlamourerApiEc.NothingDone
                ? GlamourerApiCallResult.Ok()
                : GlamourerApiCallResult.Failed(DescribeError(result));
        }
        catch (Exception ex)
        {
            return GlamourerApiCallResult.Failed(ex.Message);
        }
    }

    /// <summary> Reverts a previously applied preview back to the actor's normal (game/automation) state. </summary>
    public GlamourerApiCallResult RevertPreview(int objectIndex)
    {
        if (!IsAvailable)
            return GlamourerApiCallResult.Failed("Glamourer is not available.");

        try
        {
            var result = _revertStateSubscriber.Invoke(
                objectIndex,
                key: 0,
                flags: ApplyFlag.Equipment | ApplyFlag.Customization);

            return result is GlamourerApiEc.Success or GlamourerApiEc.NothingDone
                ? GlamourerApiCallResult.Ok()
                : GlamourerApiCallResult.Failed(DescribeError(result));
        }
        catch (Exception ex)
        {
            return GlamourerApiCallResult.Failed(ex.Message);
        }
    }

    private static string DescribeError(GlamourerApiEc code) => code switch
    {
        GlamourerApiEc.ActorNotFound => "The target actor was not found.",
        GlamourerApiEc.ActorNotHuman => "The target actor is not a human character.",
        GlamourerApiEc.InvalidKey => "The state is locked and could not be unlocked.",
        GlamourerApiEc.InvalidState => "Glamourer could not interpret this design as a valid state.",
        _ => code.ToString(),
    };
}
