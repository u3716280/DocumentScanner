using System.Windows;
using Forms = System.Windows.Forms;
namespace DocumentScanner.App;

/// <summary>
/// The scanner icon in the notification area. Closing the main window hides it here instead of quitting, so the next
/// open, and the next Scan link from ERPNext, is instant. Exit on the menu quits for real.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon image;
    private bool hintShown;

    public TrayIcon(Action open, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        var openItem = new Forms.ToolStripMenuItem(Text.Get("TrayOpen"), null, (_, _) => open()) { Font = new System.Drawing.Font(Forms.Control.DefaultFont, System.Drawing.FontStyle.Bold) };
        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Text.Get("TrayExit"), null, (_, _) => exit());
        image = LoadIcon();
        icon = new Forms.NotifyIcon { Icon = image, Text = Text.Get("Title"), ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) open(); };
        icon.BalloonTipClicked += (_, _) => open();
    }

    /// <summary>Tells the user once per run that closing did not quit the application.</summary>
    public void ShowHiddenHint()
    {
        if (hintShown) return;
        hintShown = true;
        icon.ShowBalloonTip(4000, Text.Get("Title"), Text.Get("TrayHint"), Forms.ToolTipIcon.Info);
    }

    private static System.Drawing.Icon LoadIcon()
    {
        // The .ico holds 16-256 px frames; ask for the system small-icon size so it stays sharp at 125 % and 150 %.
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/scanner.ico"))
            ?? throw new InvalidOperationException("The scanner icon resource is missing.");
        using var stream = resource.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.ContextMenuStrip?.Dispose();
        icon.Dispose();
        image.Dispose();
    }
}
