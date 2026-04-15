using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using Umbra.Common;
using Umbra.Widgets;

namespace UmbraQuickCommands.Widgets;

/// <summary>
/// Toolbar widget "Quick Commands" — a user-defined dropdown of slash commands.
/// Configure up to 16 slots (Label / Command / Icon) in widget settings and click
/// to fire the command via Dalamud's command manager. Empty slots are skipped
/// (or rendered as separators if the option is on).
/// </summary>
[ToolbarWidget(
    "QuickCommandsWidget",
    "Quick Commands",
    "Configurable popup of your favourite slash commands. Click an entry to run it. Configure up to 16 slots in the widget settings."
)]
public class QuickCommandsWidget(
    WidgetInfo info,
    string? guid = null,
    Dictionary<string, object>? configValues = null
) : StandardToolbarWidget(info, guid, configValues)
{
    private const int SlotCount = 16;

    private static ICommandManager CommandManager => Framework.Service<ICommandManager>();
    private static IChatGui ChatGui => Framework.Service<IChatGui>();
    private static IPluginLog Log => Framework.Service<IPluginLog>();

    protected override StandardWidgetFeatures Features =>
        StandardWidgetFeatures.Text |
        StandardWidgetFeatures.Icon |
        StandardWidgetFeatures.CustomizableIcon;

    public override MenuPopup Popup { get; } = new();

    private string _lastConfigHash = "";

    // ── Config variables ────────────────────────────────────────────────

    protected override IEnumerable<IWidgetConfigVariable> GetConfigVariables()
    {
        var vars = new List<IWidgetConfigVariable>(base.GetConfigVariables());

        vars.Add(new BooleanWidgetConfigVariable(
            "ShowSeparators",
            I18N("Show empty slots as separators"),
            I18N("If on, empty slots act as visual dividers in the menu. If off, they are skipped entirely."),
            false
        ) { Category = "General" });

        for (int i = 1; i <= SlotCount; i++)
        {
            var cat = $"Slot {i:D2}";
            vars.Add(new StringWidgetConfigVariable(
                $"Slot{i}_Label",
                I18N("Label"),
                I18N("Text shown for this entry in the menu. If empty, the command itself is shown."),
                "",
                64,
                false
            ) { Category = cat });

            vars.Add(new StringWidgetConfigVariable(
                $"Slot{i}_Command",
                I18N("Command"),
                I18N("Slash command to run when clicked, e.g. /glamourer. Leading slash is added if missing. Empty disables this slot."),
                "",
                256,
                false
            ) { Category = cat });

            vars.Add(new IconIdWidgetConfigVariable(
                $"Slot{i}_Icon",
                I18N("Icon"),
                I18N("Game icon ID shown next to the label. 0 = no icon."),
                0u
            ) { Category = cat });
        }

        return vars;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────

    protected override void OnLoad()
    {
        SetGameIconId(60041);
        SetText("Cmds");
    }

    protected override void OnDraw()
    {
        var hash = ComputeConfigHash();
        if (hash == _lastConfigHash) return;

        RebuildPopup();
        _lastConfigHash = hash;
    }

    protected override void OnUnload() { }

    // ── Menu construction ───────────────────────────────────────────────

    private void RebuildPopup()
    {
        Popup.Clear();

        var showSeparators = GetConfigValue<bool>("ShowSeparators");
        var rendered = 0;

        for (int i = 1; i <= SlotCount; i++)
        {
            var label   = (GetConfigValue<string>($"Slot{i}_Label") ?? "").Trim();
            var command = (GetConfigValue<string>($"Slot{i}_Command") ?? "").Trim();
            var iconId  = GetConfigValue<uint>($"Slot{i}_Icon");

            if (string.IsNullOrEmpty(command))
            {
                // Only emit a separator between actual entries — no leading/trailing dividers.
                if (showSeparators && rendered > 0) Popup.Add(new MenuPopup.Separator());
                continue;
            }

            var displayLabel = string.IsNullOrEmpty(label) ? command : label;
            var btn = new MenuPopup.Button(displayLabel)
            {
                OnClick = () => RunCommand(command),
            };
            if (iconId > 0) btn.Icon = iconId;

            Popup.Add(btn);
            rendered++;
        }

        if (rendered == 0)
        {
            Popup.Add(new MenuPopup.Header("No commands configured."));
            Popup.Add(new MenuPopup.Header("Open widget settings to add some."));
        }

        SetText(rendered > 0 ? $"Cmds ({rendered})" : "Cmds");
    }

    private void RunCommand(string raw)
    {
        try
        {
            var cmd = raw.StartsWith('/') ? raw : "/" + raw;
            CommandManager.ProcessCommand(cmd);
        }
        catch (Exception ex)
        {
            // Bad command shouldn't crash the game — surface to chat and log.
            Log.Error(ex, "[QuickCommands] failed to run: {Cmd}", raw);
            ChatGui.PrintError($"[QuickCommands] failed to run '{raw}': {ex.Message}");
        }
    }

    private string ComputeConfigHash()
    {
        var sb = new StringBuilder(SlotCount * 32);
        sb.Append(GetConfigValue<bool>("ShowSeparators")).Append('|');
        for (int i = 1; i <= SlotCount; i++)
        {
            sb.Append(GetConfigValue<string>($"Slot{i}_Label")).Append('\u001f');
            sb.Append(GetConfigValue<string>($"Slot{i}_Command")).Append('\u001f');
            sb.Append(GetConfigValue<uint>($"Slot{i}_Icon")).Append('\u001e');
        }
        return sb.ToString();
    }

    private static string I18N(string key) => key;
}
