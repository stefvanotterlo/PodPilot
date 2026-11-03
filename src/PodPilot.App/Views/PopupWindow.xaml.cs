using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
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

        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;

        if (activate)
            Activate();
        else
            _autoHideTimer.Start();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, AnimationDuration));
        SlideTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(24, 0, AnimationDuration) { EasingFunction = ease });
    }

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
