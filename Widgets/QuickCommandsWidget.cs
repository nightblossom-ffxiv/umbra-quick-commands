using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Umbra.Common;
using Umbra.Widgets;

namespace UmbraQuickCommands.Widgets;

/// <summary>
/// Toolbar widget "Quick Commands" — a user-defined dropdown of slash commands.
/// Configure up to 15 slots (Label / Command / Icon) in widget settings and click
/// to fire the command via Dalamud's command manager. Empty slots are skipped
/// (or rendered as separators if the option is on). Each slot's icon supports
/// the same four sources as the toolbar icon: game id, FontAwesome, game glyph
/// and bitmap font icon.
/// </summary>
[ToolbarWidget(
    "QuickCommandsWidget",
    "Quick Commands",
    "Configurable popup of your favourite slash commands. Click an entry to run it. Up to 15 slots, each with its own icon."
)]
public class QuickCommandsWidget(
    WidgetInfo info,
    string? guid = null,
    Dictionary<string, object>? configValues = null
) : StandardToolbarWidget(info, guid, configValues)
{
    private const int SlotCount = 15;

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
            var cat = i.ToString();

            vars.Add(new StringWidgetConfigVariable(
                $"Slot{i}_Label",
                I18N("Label"),
                I18N("Text shown for this entry in the menu. If empty, the command itself is shown."),
                "", 64, false
            ) { Category = cat });

            vars.Add(new StringWidgetConfigVariable(
                $"Slot{i}_Command",
                I18N("Command"),
                I18N("Slash command to run when clicked, e.g. /glamourer. Leading slash is added if missing. Empty disables this slot."),
                "", 256, false
            ) { Category = cat });

            // Pick ONE of the four icon pickers below — whichever you fill in is used.
            // Priority if multiple are filled: Game → FontAwesome → Game glyph → Bitmap.
            vars.Add(new IconIdWidgetConfigVariable(
                $"Slot{i}_IconGame",
                I18N("Icon — Game id"),
                I18N("Game icon ID. Leave at 0 if you want to use one of the other icon pickers below."),
                0u
            ) { Category = cat });

            vars.Add(new FaIconWidgetConfigVariable(
                $"Slot{i}_IconFa",
                I18N("Icon — FontAwesome"),
                I18N("FontAwesome icon. Leave at 'None' to use one of the other icon pickers."),
                FontAwesomeIcon.None
            ) { Category = cat });

            vars.Add(new GameGlyphWidgetConfigVariable(
                $"Slot{i}_IconGlyph",
                I18N("Icon — Game glyph"),
                I18N("In-game glyph (SeIconChar). Leave default to use one of the other icon pickers."),
                default(SeIconChar)
            ) { Category = cat });

            vars.Add(new BitmapIconWidgetConfigVariable(
                $"Slot{i}_IconBitmap",
                I18N("Icon — Bitmap font"),
                I18N("Bitmap font icon. Leave default to use one of the other icon pickers."),
                default(BitmapFontIcon)
            ) { Category = cat });
        }

        return vars;
    }

    // ── Lifecycle ───────────────────────────────────────────────────────

    protected override void OnLoad()
    {
        SetGameIconId(60041);
        SetText("Commands");
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
            var label   = (GetConfigValue<string>($"Slot{i}_Label")   ?? "").Trim();
            var command = (GetConfigValue<string>($"Slot{i}_Command") ?? "").Trim();

            if (string.IsNullOrEmpty(command))
            {
                if (showSeparators && rendered > 0) Popup.Add(new MenuPopup.Separator());
                continue;
            }

            var displayLabel = string.IsNullOrEmpty(label) ? command : label;
            var btn = new MenuPopup.Button(displayLabel)
            {
                OnClick = () => RunCommand(command),
            };

            var icon = ResolveIcon(i);
            if (icon is not null) btn.Icon = icon;

            Popup.Add(btn);
            rendered++;
        }

        if (rendered == 0)
        {
            Popup.Add(new MenuPopup.Header("No commands configured."));
            Popup.Add(new MenuPopup.Header("Open widget settings to add some."));
        }
    }

    private object? ResolveIcon(int slot)
    {
        // Auto-detect: use whichever picker the user filled in. Game id wins
        // if multiple are set, then FontAwesome, then Game glyph, then Bitmap.
        var id = GetConfigValue<uint>($"Slot{slot}_IconGame");
        if (id > 0) return id;

        var fa = GetConfigValue<FontAwesomeIcon>($"Slot{slot}_IconFa");
        if (fa != FontAwesomeIcon.None) return fa;

        var gl = GetConfigValue<SeIconChar>($"Slot{slot}_IconGlyph");
        if (gl != default) return gl;

        var bm = GetConfigValue<BitmapFontIcon>($"Slot{slot}_IconBitmap");
        if (bm != default) return bm;

        return null;
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
            Log.Error(ex, "[QuickCommands] failed to run: {Cmd}", raw);
            ChatGui.PrintError($"[QuickCommands] failed to run '{raw}': {ex.Message}");
        }
    }

    private string ComputeConfigHash()
    {
        var sb = new StringBuilder(SlotCount * 64);
        sb.Append(GetConfigValue<bool>("ShowSeparators")).Append('|');
        for (int i = 1; i <= SlotCount; i++)
        {
            sb.Append(GetConfigValue<string>($"Slot{i}_Label")).Append('\u001f');
            sb.Append(GetConfigValue<string>($"Slot{i}_Command")).Append('\u001f');
            sb.Append(GetConfigValue<uint>($"Slot{i}_IconGame")).Append('\u001f');
            sb.Append((int)GetConfigValue<FontAwesomeIcon>($"Slot{i}_IconFa")).Append('\u001f');
            sb.Append((int)GetConfigValue<SeIconChar>($"Slot{i}_IconGlyph")).Append('\u001f');
            sb.Append((int)GetConfigValue<BitmapFontIcon>($"Slot{i}_IconBitmap")).Append('\u001e');
        }
        return sb.ToString();
    }

    private static string I18N(string key) => key;
}
