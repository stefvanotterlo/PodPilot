using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using PodPilot.App.Services;
using PodPilot.App.ViewModels;
using PodPilot.Core.Devices;
using PodPilot.Core.Models;

namespace PodPilot.App.Views;

/// <summary>De tabbladen van het hoofdvenster.</summary>
public enum MainPage
{
    Overview,
    AirPods,
    Settings,
    About,
}

public partial class MainWindow : Window
{
    private static readonly (AppTheme Theme, string Label)[] Themes =
    [
        (AppTheme.System, "Zoals Windows"),
        (AppTheme.Light, "Licht"),
        (AppTheme.Dark, "Donker"),
    ];

    // AirPods geven de batterij in stappen van 10%.
    private static readonly int[] Thresholds = [10, 20, 30, 40, 50];

    private readonly AppSettings _settings;
    private readonly Func<Hotkey?, bool> _applyHotkey;
    private readonly string _hotkeyHintText;
    private bool _isRecordingHotkey;

    /// <param name="status">Live status van de AirPods (dezelfde als de pop-up).</param>
    /// <param name="settings">Wordt direct aangepast en opgeslagen bij elke wijziging.</param>
    /// <param name="devices">Gepairde Apple-apparaten om uit te kiezen.</param>
    /// <param name="applyHotkey">Zet de sneltoets voor de pop-up; false = al in gebruik door een andere app.</param>
    public MainWindow(StatusViewModel status, AppSettings settings, IReadOnlyList<PairedAppleDevice> devices,
        Func<Hotkey?, bool> applyHotkey)
    {
        InitializeComponent();
        DataContext = status;
        _settings = settings;
        _applyHotkey = applyHotkey;
        _hotkeyHintText = HotkeyHint.Text;
        VersionText.Text = $"Versie {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
        NavOverview.IsChecked = true;

        ShowHotkey();
        HotkeyToggle.IsChecked = settings.PopupHotkeyEnabled;
        HotkeyRow.IsEnabled = settings.PopupHotkeyEnabled;
        StartupToggle.IsChecked = StartupRegistration.IsEnabled();
        AutoPopupToggle.IsChecked = settings.AutoPopupOnLidOpen;
        NotifyToggle.IsChecked = settings.NotifyLowBattery;
        ThresholdSection.IsEnabled = settings.NotifyLowBattery;
        AutoResumeToggle.IsChecked = settings.AutoResumeOnEarInsertion;
        AutoResumeRow.IsEnabled = settings.AutoPauseOnEarRemoval;

        // Checked/Unchecked in plaats van Click: dan werkt het ook met het toetsenbord en een schermlezer.
        // Pas na het instellen van de beginwaarden aanmelden.
        OnToggle(HotkeyToggle, OnHotkeyToggled);
        OnToggle(StartupToggle, OnStartupChanged);
        OnToggle(AutoPopupToggle, OnAutoPopupChanged);
        OnToggle(NotifyToggle, OnNotifyChanged);
        // Deze drie staan zowel op het Overzicht (snelle instellingen) als op het tabblad AirPods.
        LinkToggles(QuickConnectLidToggle, AutoConnectLidToggle,
            () => _settings.AutoConnectOnLidOpen, v => _settings.AutoConnectOnLidOpen = v);
        LinkToggles(QuickConnectEarToggle, AutoConnectEarToggle,
            () => _settings.AutoConnectOnEarInsertion, v => _settings.AutoConnectOnEarInsertion = v);
        LinkToggles(QuickPauseToggle, AutoPauseToggle,
            () => _settings.AutoPauseOnEarRemoval, v =>
            {
                _settings.AutoPauseOnEarRemoval = v;
                AutoResumeRow.IsEnabled = v;
            });
        OnToggle(AutoResumeToggle, () =>
        {
            _settings.AutoResumeOnEarInsertion = AutoResumeToggle.IsChecked == true;
            Changed();
        });

        foreach (var (theme, label) in Themes)
        {
            AddOption(ThemePanel, "Theme", label, settings.Theme == theme, () =>
            {
                _settings.Theme = theme;
                Changed();
            });
            AddOption(PopupThemePanel, "PopupTheme", label, settings.PopupTheme == theme, () =>
            {
                _settings.PopupTheme = theme;
                Changed();
            });
        }

        SetupColorChoice(CustomBackgroundToggle, CustomBackgroundRow, BackgroundSwatches, BackgroundHex, BackgroundPresets,
            () => _settings.UseCustomBackground, v => _settings.UseCustomBackground = v,
            () => _settings.CustomBackground, v => _settings.CustomBackground = v);
        SetupColorChoice(CustomAccentToggle, CustomAccentRow, AccentSwatches, AccentHex, AccentPresets,
            () => _settings.UseCustomAccent, v => _settings.UseCustomAccent = v,
            () => _settings.CustomAccent, v => _settings.CustomAccent = v);
        UpdateThemeSections();

        foreach (var threshold in Thresholds)
        {
            AddOption(ThresholdPanel, "Threshold", $"{threshold}%", settings.LowBatteryThreshold == threshold, () =>
            {
                _settings.LowBatteryThreshold = threshold;
                Changed();
            });
        }

        var hasPreferred = devices.Any(d => d.DeviceId == settings.PreferredDeviceId);
        AddDeviceOption("Automatisch", "Het eerste gepairde AirPods-apparaat", !hasPreferred, null);
        foreach (var device in devices)
        {
            var model = device.ProductId is ushort pid ? AirPodsModels.Lookup(pid).Name : "Model onbekend";
            AddDeviceOption(device.Name, model, device.DeviceId == settings.PreferredDeviceId, device.DeviceId);
        }

        if (devices.Count == 0)
        {
            DevicePanel.Children.Add(new TextBlock
            {
                Text = "Er zijn geen gepairde AirPods gevonden. Koppel ze eerst via de Bluetooth-instellingen van Windows.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary"),
            });
        }

        // De titelbalk kan pas een donker thema krijgen als het venster een handle heeft.
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    }

    /// <summary>Er is iets gewijzigd (en opgeslagen). De app past het meteen toe.</summary>
    public event EventHandler? SettingsChanged;

    private void Changed()
    {
        _settings.Save();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- Navigatie ------------------------------------------------------------------------

    /// <summary>Gaat naar een tabblad (bijv. Instellingen via het tandwiel in de pop-up).</summary>
    public void ShowPage(MainPage page)
    {
        var nav = page switch
        {
            MainPage.AirPods => NavAirPods,
            MainPage.Settings => NavSettings,
            MainPage.About => NavAbout,
            _ => NavOverview,
        };
        nav.IsChecked = true;
    }

    private void OnNavChecked(object sender, RoutedEventArgs e)
    {
        // Checked vuurt al tijdens InitializeComponent, als de tabbladen nog niet bestaan.
        if (OverviewPage is null)
            return;

        OverviewPage.Visibility = Visible(NavOverview);
        AirPodsPage.Visibility = Visible(NavAirPods);
        SettingsPage.Visibility = Visible(NavSettings);
        AboutPage.Visibility = Visible(NavAbout);

        static Visibility Visible(RadioButton nav) => nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Overzicht ------------------------------------------------------------------------

    /// <summary>Klik op "Verbinden" / "Verbinding verbreken". De app regelt de rest.</summary>
    public event EventHandler? ConnectToggleRequested;

    private void OnConnectClick(object sender, RoutedEventArgs e) =>
        ConnectToggleRequested?.Invoke(this, EventArgs.Empty);

    // --- Over -----------------------------------------------------------------------------

    // Verborgen: 10x achter elkaar op spatie op het tabblad Over. Een andere toets of meer dan
    // 2 seconden pauze begint opnieuw.
    private const int EasterEggPresses = 10;
    private static readonly TimeSpan EasterEggMaxPause = TimeSpan.FromSeconds(2);
    private int _spacePresses;
    private DateTime _lastSpace = DateTime.MinValue;

    /// <summary>De verborgen achtergrond is gevonden (10x spatie op het tabblad Over).</summary>
    public event EventHandler? EasterEggTriggered;

    /// <summary>Verbergt de inhoud van het tabblad Over, zodat alleen de achtergrondfoto te zien is.</summary>
    public void HideAboutContent() => AboutContent.Visibility = Visibility.Collapsed;

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (AboutPage.Visibility != Visibility.Visible || e.IsRepeat)
            return;

        // Lock- en modifiertoetsen tellen niet mee (sommige hulpprogramma's sturen die tussendoor).
        if (Hotkey.IsModifierKey(e.Key) || e.Key is System.Windows.Input.Key.NumLock
                or System.Windows.Input.Key.CapsLock or System.Windows.Input.Key.Scroll)
            return;

        if (e.Key != System.Windows.Input.Key.Space)
        {
            _spacePresses = 0;
            return;
        }

        // Op dit tabblad doet spatie niets anders: niet ook een knop met focus laten indrukken.
        e.Handled = true;
        var now = DateTime.Now;
        _spacePresses = now - _lastSpace > EasterEggMaxPause ? 1 : _spacePresses + 1;
        _lastSpace = now;

        if (_spacePresses < EasterEggPresses)
            return;

        _spacePresses = 0;
        EasterEggTriggered?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenLogClick(object sender, RoutedEventArgs e) => Open(DiagnosticLog.FilePath);

    private void OnOpenSettingsFolderClick(object sender, RoutedEventArgs e) =>
        Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PodPilot"));

    private void OnBluetoothSettingsClick(object sender, RoutedEventArgs e) => WindowsSettings.OpenBluetooth();

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Bestand of map bestaat (nog) niet; niets aan de hand.
        }
    }

    // --- Instellingen ---------------------------------------------------------------------

    private static void AddOption(Panel panel, string group, string label, bool isChecked, Action onChecked)
    {
        var option = new RadioButton
        {
            Content = label,
            GroupName = group,
            IsChecked = isChecked,
            Style = (Style)Application.Current.FindResource("SegmentButton"),
            Margin = new Thickness(0, 0, 6, 6),
        };
        // Pas na het instellen van IsChecked aanmelden, anders vuurt het event al bij het opbouwen.
        option.Checked += (_, _) => onChecked();
        panel.Children.Add(option);
    }

    private void AddDeviceOption(string title, string subtitle, bool isChecked, string? deviceId)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = subtitle, FontSize = 12, Opacity = 0.75 });

        var option = new RadioButton
        {
            Content = content,
            GroupName = "Device",
            IsChecked = isChecked,
            Style = (Style)Application.Current.FindResource("SegmentButton"),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
        };
        option.Checked += (_, _) =>
        {
            _settings.PreferredDeviceId = deviceId;
            Changed();
        };
        DevicePanel.Children.Add(option);
    }

    private static void OnToggle(CheckBox toggle, Action handler)
    {
        toggle.Checked += (_, _) => handler();
        toggle.Unchecked += (_, _) => handler();
    }

    /// <summary>
    /// Twee schakelaars voor dezelfde instelling: ze volgen elkaar en de instelling wordt één keer
    /// opgeslagen (de tweede ziet dat de waarde al klopt).
    /// </summary>
    private void LinkToggles(CheckBox first, CheckBox second, Func<bool> get, Action<bool> set)
    {
        first.IsChecked = second.IsChecked = get();
        foreach (var (toggle, other) in new[] { (first, second), (second, first) })
        {
            OnToggle(toggle, () =>
            {
                var value = toggle.IsChecked == true;
                if (value == get())
                    return;

                set(value);
                other.IsChecked = value;
                Changed();
            });
        }
    }

    private void OnStartupChanged()
    {
        // De Run-sleutel in de registry is de bron; settings.json hoeft niet te veranderen.
        StartupRegistration.Set(StartupToggle.IsChecked == true);
    }

    private void OnAutoPopupChanged()
    {
        _settings.AutoPopupOnLidOpen = AutoPopupToggle.IsChecked == true;
        Changed();
    }

    private void OnNotifyChanged()
    {
        _settings.NotifyLowBattery = NotifyToggle.IsChecked == true;
        ThresholdSection.IsEnabled = _settings.NotifyLowBattery;
        Changed();
    }

    // --- Eigen kleuren -------------------------------------------------------------------

    // Voorbeeldkleuren: donkere en lichte achtergronden, en accenten die op beide goed staan.
    private static readonly string[] BackgroundPresets =
        ["#1B1C1F", "#0F172A", "#1E1B2E", "#10231C", "#2A1E1A", "#F0F1F4", "#F5EFE6", "#EAF2F8", "#F3EEF8", "#EEF5EE"];

    private static readonly string[] AccentPresets =
        ["#2F7DF6", "#8B5CF6", "#EC4899", "#8C1D40", "#E5484D", "#F59E0B", "#22A06B", "#0EA5A4"];

    /// <summary>
    /// Eén kleurkeuze: schakelaar aan/uit, voorbeeldvlakjes en een hexvak. De gekozen kleur blijft
    /// bewaard als de schakelaar uit staat.
    /// </summary>
    private void SetupColorChoice(CheckBox toggle, FrameworkElement row, Panel swatches, TextBox hexBox, string[] presets,
        Func<bool> getUse, Action<bool> setUse, Func<string> getColor, Action<string> setColor)
    {
        toggle.IsChecked = getUse();
        row.IsEnabled = getUse();
        hexBox.Text = getColor();

        var options = new List<(RadioButton Swatch, string Hex)>();
        foreach (var preset in presets)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                GroupName = hexBox.Name,
                Background = new System.Windows.Media.SolidColorBrush(ThemeManager.ParseColor(preset)!.Value),
                ToolTip = preset,
                IsChecked = SameColor(preset, getColor()),
            };
            swatch.Checked += (_, _) =>
            {
                if (!SameColor(preset, getColor()))
                    Apply(preset);
            };
            options.Add((swatch, preset));
            swatches.Children.Add(swatch);
        }

        void Apply(string hex)
        {
            ColorHint.Visibility = Visibility.Collapsed;
            setColor(hex);
            hexBox.Text = hex;
            foreach (var (swatch, preset) in options)
                swatch.IsChecked = SameColor(preset, hex);
            Changed();
        }

        // Eigen hexcode: overnemen bij Enter of als je het vak verlaat.
        void ApplyTyped()
        {
            if (ThemeManager.ParseColor(hexBox.Text) is not { } color)
            {
                ColorHint.Text = $"\"{hexBox.Text}\" is geen geldige kleur. Gebruik zes tekens, bijv. #8B5CF6.";
                ColorHint.Visibility = Visibility.Visible;
                hexBox.Text = getColor();
                return;
            }

            var hex = ThemeManager.ToHex(color);
            if (!SameColor(hex, getColor()))
                Apply(hex);
            else
                hexBox.Text = hex;
        }

        hexBox.LostKeyboardFocus += (_, _) => ApplyTyped();
        hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                ApplyTyped();
        };

        OnToggle(toggle, () =>
        {
            setUse(toggle.IsChecked == true);
            row.IsEnabled = getUse();
            UpdateThemeSections();
            Changed();
        });

        static bool SameColor(string a, string b) => string.Equals(a.TrimStart('#'), b.TrimStart('#'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Met een eigen achtergrondkleur doet licht/donker niets meer: die keuzes grijs maken.</summary>
    private void UpdateThemeSections()
    {
        ThemeSection.IsEnabled = !_settings.UseCustomBackground;
        PopupThemeSection.IsEnabled = !_settings.UseCustomBackground;
    }

    // --- Sneltoets ------------------------------------------------------------------------

    private static readonly Hotkey DefaultHotkey = new(
        System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt, System.Windows.Input.Key.P);

    private Hotkey SavedHotkey => Hotkey.Parse(_settings.PopupHotkey) ?? DefaultHotkey;

    private void ShowHotkey() => HotkeyButton.Content = SavedHotkey.ToString();

    /// <summary>Schakelaar aan of uit. De combinatie zelf blijft bewaard.</summary>
    private void OnHotkeyToggled()
    {
        _settings.PopupHotkeyEnabled = HotkeyToggle.IsChecked == true;
        _settings.PopupHotkey ??= DefaultHotkey.ToString();
        HotkeyRow.IsEnabled = _settings.PopupHotkeyEnabled;
        Changed();

        ResetHotkeyHint();
        if (_settings.PopupHotkeyEnabled)
        {
            if (!_applyHotkey(SavedHotkey))
                ShowHotkeyInUse(SavedHotkey);
        }
        else
        {
            _applyHotkey(null);
        }
    }

    private void OnHotkeyClick(object sender, RoutedEventArgs e)
    {
        if (_isRecordingHotkey)
            return;

        // Tijdens het opnemen de huidige sneltoets uitzetten, anders vangt Windows hem weg.
        _isRecordingHotkey = true;
        _applyHotkey(null);
        HotkeyButton.Content = "Druk een combinatie…";
        ResetHotkeyHint();
        HotkeyHint.Text = "Esc = annuleren.";
    }

    private void OnHotkeyKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingHotkey)
            return;

        // Met Alt erbij meldt WPF de toets als Key.System; de echte toets staat dan in SystemKey.
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        e.Handled = true;

        if (Hotkey.IsModifierKey(key))
            return; // wacht op de echte toets

        if (key == System.Windows.Input.Key.Escape && modifiers == 0)
        {
            StopRecording(null);
            return;
        }

        var hotkey = new Hotkey(modifiers, key);
        if (!hotkey.IsValid)
        {
            HotkeyHint.Text = "Gebruik Ctrl, Alt of Win erbij (of een F-toets).";
            return;
        }

        StopRecording(hotkey);
    }

    /// <summary>Klik je ergens anders tijdens het opnemen, dan blijft de oude combinatie staan.</summary>
    private void OnHotkeyLostFocus(object sender, RoutedEventArgs e)
    {
        if (_isRecordingHotkey)
            StopRecording(null);
    }

    /// <param name="newHotkey">De nieuwe combinatie, of null om de bewaarde te houden (annuleren).</param>
    private void StopRecording(Hotkey? newHotkey)
    {
        _isRecordingHotkey = false;
        ResetHotkeyHint();

        if (newHotkey is { } hotkey)
        {
            if (!_applyHotkey(hotkey))
            {
                // Bezet: de bewaarde combinatie terugzetten.
                _applyHotkey(SavedHotkey);
                ShowHotkeyInUse(hotkey);
            }
            else if (_settings.PopupHotkey != hotkey.ToString())
            {
                _settings.PopupHotkey = hotkey.ToString();
                Changed();
            }
        }
        else
        {
            _applyHotkey(SavedHotkey);
        }

        ShowHotkey();
    }

    private void ResetHotkeyHint()
    {
        HotkeyHint.Text = _hotkeyHintText;
        HotkeyHint.ClearValue(TextBlock.ForegroundProperty);
    }

    private void ShowHotkeyInUse(Hotkey hotkey)
    {
        HotkeyHint.Text = $"{hotkey} is al in gebruik door een andere app. Kies een andere combinatie.";
        HotkeyHint.Foreground = (System.Windows.Media.Brush)FindResource("LowBrush");
    }

    protected override void OnClosed(EventArgs e)
    {
        // Venster dicht tijdens het opnemen: de bewaarde combinatie weer aanzetten.
        if (_isRecordingHotkey)
            StopRecording(null);
        base.OnClosed(e);
    }
}
