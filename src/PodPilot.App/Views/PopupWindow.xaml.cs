using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PodPilot.App.Services;

namespace PodPilot.App.Views;

public partial class PopupWindow : Window
{
    private static readonly Duration AnimationDuration = TimeSpan.FromMilliseconds(180);

    private DateTime _hiddenAt = DateTime.MinValue;

    // Bij automatisch tonen (case geopend) krijgt de pop-up geen focus. Hij sluit dan niet bij een klik
    // elders, maar verdwijnt vanzelf na een paar seconden. Zolang de muis erop staat, blijft hij open.
    private readonly DispatcherTimer _autoHideTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public PopupWindow()
    {
        InitializeComponent();

        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            if (!IsActive && !IsMouseOver)
                HidePopup();
        };
        MouseEnter += (_, _) => _autoHideTimer.Stop();
        MouseLeave += (_, _) =>
        {
            if (IsVisible && !IsActive)
                _autoHideTimer.Start();
        };
        // Klikt de gebruiker in de pop-up, dan gedraagt hij zich als een normaal geopende pop-up.
        Activated += (_, _) => _autoHideTimer.Stop();
    }

    /// <summary>Zet dit op true bij het afsluiten. Anders wordt "sluiten" (Alt+F4) alleen verbergen.</summary>
    public bool AllowClose { get; set; }

    /// <summary>
    /// True als de pop-up net is verborgen. Klik je op het tray-icoon terwijl de pop-up open is,
    /// dan komt eerst Deactivated (verbergen) en daarna de klik. Zonder deze controle zou de klik
    /// de pop-up meteen weer openen.
    /// </summary>
    public bool WasJustHidden => DateTime.Now - _hiddenAt < TimeSpan.FromMilliseconds(400);

    /// <summary>Toont de pop-up rechtsonder in het werkgebied (boven de taakbalk), met een korte animatie.</summary>
    /// <param name="activate">
    /// True bij een klik op het tray-icoon: de pop-up krijgt focus en sluit bij een klik elders.
    /// False bij automatisch tonen: geen focus (je typt gewoon door) en hij verdwijnt vanzelf.
    /// </param>
    public void ShowAtCorner(bool activate = true)
    {
        // Een afgelopen animatie houdt zijn eindwaarde vast; eerst loskoppelen.
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;

        ShowActivated = activate;
        Show();
        UpdateLayout();
        PlaceOnScreen(nearCursor: activate);

        if (activate)
            Activate();
        else
            _autoHideTimer.Start();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, AnimationDuration));
        SlideTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(24, 0, AnimationDuration) { EasingFunction = ease });
    }

    // --- Positie --------------------------------------------------------------------------

    /// <summary>
    /// Zelf gekozen plek (linkerbovenhoek in schermpixels), of null = rechtsonder. Wordt gezet als
    /// je de pop-up versleept.
    /// </summary>
    public System.Drawing.Point? CustomPosition { get; set; }

    /// <summary>De pop-up is versleept naar een nieuwe plek (schermpixels).</summary>
    public event EventHandler<System.Drawing.Point>? PositionChanged;

    /// <summary>
    /// Zet de pop-up op zijn plek, in echte schermpixels (dus goed bij elke schaal en
    /// schermindeling): de zelf gekozen plek als die nog op een scherm ligt, anders rechtsonder in
    /// het werkgebied. Bij een klik (tray, sneltoets) het scherm waar de muis is, anders het hoofdscherm.
    /// </summary>
    private void PlaceOnScreen(bool nearCursor)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        if (CustomPosition is { } p && MonitorFromPoint(new POINT { X = p.X, Y = p.Y }, MonitorDefaultToNull) != IntPtr.Zero)
        {
            SetWindowPos(handle, IntPtr.Zero, p.X, p.Y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
            return;
        }

        POINT anchor = default;
        if (!nearCursor || !GetCursorPos(out anchor))
            anchor = default; // (0,0) ligt altijd op het hoofdscherm
        var monitor = MonitorFromPoint(anchor, MonitorDefaultToPrimary);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        // Twee keer: verhuist het venster naar een scherm met een andere schaal, dan verandert zijn
        // grootte in pixels en moet hij opnieuw tegen de hoek aan.
        for (var i = 0; i < 2; i++)
        {
            GetWindowRect(handle, out var r);
            SetWindowPos(handle, IntPtr.Zero, info.rcWork.Right - (r.Right - r.Left), info.rcWork.Bottom - (r.Bottom - r.Top),
                0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
        }
    }

    /// <summary>Slepen aan de achtergrond verplaatst de pop-up; de nieuwe plek wordt onthouden.</summary>
    private void OnBackgroundMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (!GetWindowRect(handle, out var before))
            return;

        _autoHideTimer.Stop();
        DragMove(); // keert terug als de muis wordt losgelaten

        // Alleen een echte verplaatsing onthouden, niet een gewone klik op de achtergrond.
        if (!GetWindowRect(handle, out var after) || (after.Left == before.Left && after.Top == before.Top))
            return;

        var position = new System.Drawing.Point(after.Left, after.Top);
        CustomPosition = position;
        PositionChanged?.Invoke(this, position);
    }

    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToPrimary = 1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int cx, int cy, uint flags);

    public void HidePopup()
    {
        _autoHideTimer.Stop();
        if (!IsVisible)
            return;

        Hide();
        _hiddenAt = DateTime.Now;
    }

    private void OnDeactivated(object? sender, EventArgs e) => HidePopup();

    private void OnCloseClick(object sender, RoutedEventArgs e) => HidePopup();

    /// <summary>Klik op "Verbinden" / "Verbinding verbreken". De app regelt de rest.</summary>
    public event EventHandler? ConnectToggleRequested;

    private void OnConnectClick(object sender, RoutedEventArgs e) =>
        ConnectToggleRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Klik op het tandwiel.</summary>
    public event EventHandler? SettingsRequested;

    private void OnSettingsClick(object sender, RoutedEventArgs e) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void OnBluetoothSettingsClick(object sender, RoutedEventArgs e)
    {
        HidePopup();
        WindowsSettings.OpenBluetooth();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            HidePopup();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
            return;

        e.Cancel = true;
        HidePopup();
    }
}
