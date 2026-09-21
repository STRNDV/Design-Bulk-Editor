using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GlamourerBulkEditor.Core.Services;
using GlamourerBulkEditor.Plugin.Windows;

namespace GlamourerBulkEditor.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/glamourerbulk";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public readonly WindowSystem WindowSystem = new("GlamourerBulkEditor");

    private readonly MainWindow _mainWindow;
    private readonly GlamourerApiClient _apiClient;
    private readonly DesignLibrary _designLibrary;

    public Plugin()
    {
        _apiClient = new GlamourerApiClient(PluginInterface, Log);

        var configDirectory = PluginInterface.GetPluginConfigDirectory();
        var assignmentStorePath = Path.Combine(configDirectory, "character-assignments.json");

        _designLibrary = new DesignLibrary(
            new DesignRepository(new BackupService()),
            new AutomationConfigReader(),
            new CharacterAssignmentSuggester(),
            new CharacterAssignmentStore(assignmentStorePath));

        _mainWindow = new MainWindow(_designLibrary, _apiClient);
        WindowSystem.AddWindow(_mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Glamourer Bulk Editor window.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;

        WindowSystem.RemoveAllWindows();
        _mainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => ToggleMainWindow();

    private void ToggleMainWindow() => _mainWindow.Toggle();
}
