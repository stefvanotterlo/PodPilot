using Forms = System.Windows.Forms;

namespace PodPilot.App.Services;

/// <summary>
/// Het icoon in het systeemvak, met een tooltip. Linkerklik = pop-up; rechterklik vraagt de app om
/// het menu te tonen (een WPF-menu in PodPilot-stijl, zie <see cref="TrayMenu"/>).
/// Werkt via WinForms NotifyIcon: dat draait gewoon mee op de WPF-UI-thread.
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private IntPtr _iconHandle;
    private int? _percent;
    private bool _hasRendered;
    private (int? Percent, bool LightTaskbar, TrayIconOptions Options, bool Dimmed) _renderedFor;
    private TrayIconOptions _options = new(TrayIconStyle.Icon, "Segoe UI", TrayFontSize.Normal, "auto", "auto", 40);
    private bool _dimmed;

    public TrayIconService()
    {
        _notifyIcon = new Forms.NotifyIcon { Text = "PodPilot" };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                OpenRequested?.Invoke(this, EventArgs.Empty);
            else if (e.Button == Forms.MouseButtons.Right)
                MenuRequested?.Invoke(this, EventArgs.Empty);
        };

        Update("PodPilot", null);
        _notifyIcon.Visible = true;
    }

    /// <summary>Linkerklik op het icoon.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>Rechterklik op het icoon: de app toont het menu.</summary>
    public event EventHandler? MenuRequested;

    /// <summary>Werkt de tooltip en het icoon bij. Het icoon wordt alleen opnieuw getekend als het verandert.</summary>
    public void Update(string tooltip, int? lowestPercent)
    {
        // NotifyIcon geeft een fout bij meer dan 127 tekens.
        _notifyIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        _percent = lowestPercent;

        var key = (lowestPercent, ThemeManager.IsTaskbarLight(), _options, _dimmed);
        if (_hasRendered && key == _renderedFor)
            return;

        var (icon, handle) = TrayIconRenderer.Render(key.Item1, key.Item2, _options, _dimmed);
        _notifyIcon.Icon = icon;

        // Pas na het wisselen het oude icoon opruimen.
        TrayIconRenderer.Destroy(_iconHandle);
        _iconHandle = handle;
        _renderedFor = key;
        _hasRendered = true;
    }

    /// <summary>Opnieuw tekenen na een themawissel van Windows (lichte/donkere taakbalk).</summary>
    public void RefreshTheme() => Update(_notifyIcon.Text, _percent);

    /// <summary>Stijl uit de instellingen en of het icoon gedimd moet zijn (geluid niet via de AirPods).</summary>
    public void SetAppearance(TrayIconOptions options, bool dimmed)
    {
        _options = options;
        _dimmed = dimmed;
        Update(_notifyIcon.Text, _percent);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        TrayIconRenderer.Destroy(_iconHandle);
        _iconHandle = IntPtr.Zero;
    }
}
