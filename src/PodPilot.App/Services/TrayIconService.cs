using Forms = System.Windows.Forms;

namespace PodPilot.App.Services;

/// <summary>
/// Het icoon in het systeemvak, met een tooltip en een menu onder de rechtermuisknop.
/// Werkt via WinForms NotifyIcon: dat draait gewoon mee op de WPF-UI-thread.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private IntPtr _iconHandle;
    private int? _percent;
    private bool _hasRendered;
    private (int? Percent, bool LightTaskbar) _renderedFor;

    public TrayIconService()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Status tonen", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("PodPilot openen", null, (_, _) => MainWindowRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Instellingen…", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Bluetooth-instellingen", null, (_, _) => WindowsSettings.OpenBluetooth());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _notifyIcon = new Forms.NotifyIcon { Text = "PodPilot", ContextMenuStrip = menu };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke(this, EventArgs.Empty);
        };

        Update("PodPilot", null);
        _notifyIcon.Visible = true;
    }

    /// <summary>Linkerklik op het icoon of "Status tonen" in het menu.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>"PodPilot openen": het hoofdvenster op het Overzicht.</summary>
    public event EventHandler? MainWindowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    /// <summary>Werkt de tooltip en het icoon bij. Het icoon wordt alleen opnieuw getekend als het verandert.</summary>
    public void Update(string tooltip, int? lowestPercent)
    {
        // NotifyIcon geeft een fout bij meer dan 127 tekens.
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _percent = lowestPercent;

        var key = (lowestPercent, ThemeManager.IsTaskbarLight());
        if (_hasRendered && key == _renderedFor)
            return;

        var (icon, handle) = TrayIconRenderer.Render(key.Item1, key.Item2);
        _notifyIcon.Icon = icon;

        // Pas na het wisselen het oude icoon opruimen.
        TrayIconRenderer.Destroy(_iconHandle);
        _iconHandle = handle;
        _renderedFor = key;
        _hasRendered = true;
    }

    /// <summary>Opnieuw tekenen na een themawissel van Windows (lichte/donkere taakbalk).</summary>
    public void RefreshTheme() => Update(_notifyIcon.Text, _percent);

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        TrayIconRenderer.Destroy(_iconHandle);
        _iconHandle = IntPtr.Zero;
    }
}
