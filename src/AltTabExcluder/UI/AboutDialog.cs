using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;
using AltTabExcluder.Services;

namespace AltTabExcluder.UI;

/// <summary>
/// A small modal dialog showing app info, how it works, data location, the
/// current hotkey, and developer credit.
/// </summary>
public sealed class AboutDialog : Form
{
    public AboutDialog(string hotkeyLabel)
    {
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";

        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AltTabExcluder");

        Text = "About AltTabExcluder";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 440;
        Height = 460;

        // ─── Header: icon + title + version ──────────────────────────────

        var headerPanel = new Panel
        {
            Left = 16,
            Top = 16,
            Width = 400,
            Height = 56,
        };

        var icon = LoadAppIcon();
        if (icon is not null)
        {
            var iconBox = new PictureBox
            {
                Image = icon.ToBitmap(),
                Left = 0,
                Top = 0,
                Width = 32,
                Height = 32,
                SizeMode = PictureBoxSizeMode.StretchImage,
            };
            headerPanel.Controls.Add(iconBox);
        }

        var titleLabel = new Label
        {
            Text = "AltTabExcluder",
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold),
            Left = 44,
            Top = 0,
            Width = 340,
            Height = 24,
        };
        headerPanel.Controls.Add(titleLabel);

        var versionLabel = new Label
        {
            Text = $"Version {version}",
            ForeColor = SystemColors.GrayText,
            Left = 44,
            Top = 26,
            Width = 340,
            Height = 18,
        };
        headerPanel.Controls.Add(versionLabel);

        Controls.Add(headerPanel);

        // ─── Description ─────────────────────────────────────────────────

        var descLabel = new Label
        {
            Text = "Exclude specific windows from the Alt+Tab switcher.",
            Left = 16,
            Top = 80,
            Width = 400,
            Height = 20,
        };
        Controls.Add(descLabel);

        // ─── How it works ────────────────────────────────────────────────

        var howItWorks = new Label
        {
            Text =
                "How it works:\r\n" +
                "\r\n" +
                "  •  Hotkey — toggle the Alt+Tab visibility of the focused window.\r\n" +
                "  •  Quick Exclude — one-off toggle for any open window.\r\n" +
                "  •  Always Exclude — persist a per-process rule; future windows\r\n" +
                "     of that process are auto-excluded on launch.\r\n" +
                "\r\n" +
                "The app toggles the WS_EX_TOOLWINDOW / WS_EX_APPWINDOW extended\r\n" +
                "window styles on target window handles.",
            Left = 16,
            Top = 108,
            Width = 400,
            Height = 130,
        };
        Controls.Add(howItWorks);

        // ─── Current hotkey ──────────────────────────────────────────────

        var currentHotkeyLabel = new Label
        {
            Text = $"Current hotkey: {hotkeyLabel}",
            Left = 16,
            Top = 248,
            Width = 400,
            Height = 20,
        };
        Controls.Add(currentHotkeyLabel);

        // ─── Data location ───────────────────────────────────────────────

        var dataLabel = new Label
        {
            Text = $"Settings & rules: {dataDir}",
            ForeColor = SystemColors.GrayText,
            Left = 16,
            Top = 274,
            Width = 400,
            Height = 20,
        };
        Controls.Add(dataLabel);

        // ─── Developer credit ────────────────────────────────────────────

        var devLabel = new LinkLabel
        {
            Text = "Developed by Marko  —  github.com/markoshaq",
            Left = 16,
            Top = 300,
            Width = 400,
            Height = 20,
        };
        devLabel.LinkArea = new LinkArea("Developed by Marko  —  ".Length, "github.com/markoshaq".Length);
        devLabel.LinkClicked += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("https://github.com/markoshaq") { UseShellExecute = true }); }
            catch (Exception ex) { AppLogger.LogWarning(ex, "Failed to open GitHub link"); }
        };
        Controls.Add(devLabel);

        // ─── Close button ────────────────────────────────────────────────

        var closeButton = new Button
        {
            Text = "Close",
            Left = 336,
            Top = 388,
            Width = 80,
            DialogResult = DialogResult.OK,
        };
        Controls.Add(closeButton);
        AcceptButton = closeButton;

        if (icon is not null)
            FormClosed += (_, _) => icon.Dispose();
    }

    private static Icon? LoadAppIcon()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("AltTabExcluder.assets.app.ico");
            if (stream is not null)
                return new Icon(stream);
        }
        catch (Exception ex) { AppLogger.LogDebug($"Failed to load embedded app icon: {ex.Message}"); }

        // Fallback: load from disk (development / non-embedded scenario).
        string path = Path.Combine(AppContext.BaseDirectory, "assets", "app.ico");
        if (File.Exists(path))
        {
            try { return new Icon(path); }
            catch (Exception ex) { AppLogger.LogDebug($"Failed to load app icon from disk: {ex.Message}"); }
        }
        return null;
    }
}
