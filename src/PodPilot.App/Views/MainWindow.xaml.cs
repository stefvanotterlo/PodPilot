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
    Devices,
    Settings,
    Hotkeys,
    About,
}

public partial class MainWindow : Window
{
    private static readonly (AppTheme Theme, string Label)[] Themes =
    [
        (AppTheme.System, L.T("Zoals Windows")),
        (AppTheme.Light, L.T("Licht")),
        (AppTheme.Dark, L.T("Donker")),
    ];

    // AirPods geven de batterij in stappen van 10%.
    private static readonly int[] Thresholds = [10, 20, 30, 40, 50];

    private readonly AppSettings _settings;

    /// <param name="status">Live status van de AirPods (dezelfde als de pop-up).</param>
    /// <param name="settings">Wordt direct aangepast en opgeslagen bij elke wijziging.</param>
    /// <param name="devices">Gepairde Apple-apparaten om uit te kiezen.</param>
    /// <param name="applyPopupHotkey">Zet de sneltoets voor de pop-up; false = al in gebruik.</param>
    /// <param name="applyConnectHotkey">Zet de sneltoets voor verbinden/verbreken; false = al in gebruik.</param>
    /// <param name="applyOutputHotkey">Zet de sneltoets voor het wisselen van de geluidsuitgang.</param>
    public MainWindow(StatusViewModel status, AppSettings settings, IReadOnlyList<PairedAppleDevice> devices,
        Func<Hotkey?, bool> applyPopupHotkey, Func<Hotkey?, bool> applyConnectHotkey, Func<Hotkey?, bool> applyOutputHotkey)
    {
        InitializeComponent();
        DataContext = status;
        _settings = settings;
        VersionText.Text = L.T("Versie {0}", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3));
        NavOverview.IsChecked = true;
        _deviceRefresh.Tick += async (_, _) => await RefreshDevicesAsync();

        var ctrlAlt = System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Alt;
        _popupHotkeyEditor = new HotkeyEditor(HotkeyToggle, HotkeyRow, HotkeyButton, HotkeyHint,
            new Hotkey(ctrlAlt, System.Windows.Input.Key.P),
            () => _settings.PopupHotkeyEnabled, v => _settings.PopupHotkeyEnabled = v,
            () => _settings.PopupHotkey, v => _settings.PopupHotkey = v, applyPopupHotkey, Changed);
        _connectHotkeyEditor = new HotkeyEditor(ConnectHotkeyToggle, ConnectHotkeyRow, ConnectHotkeyButton, ConnectHotkeyHint,
            new Hotkey(ctrlAlt, System.Windows.Input.Key.C),
            () => _settings.ConnectHotkeyEnabled, v => _settings.ConnectHotkeyEnabled = v,
            () => _settings.ConnectHotkey, v => _settings.ConnectHotkey = v, applyConnectHotkey, Changed);
        _outputHotkeyEditor = new HotkeyEditor(OutputHotkeyToggle, OutputHotkeyRow, OutputHotkeyButton, OutputHotkeyHint,
            new Hotkey(ctrlAlt, System.Windows.Input.Key.O),
            () => _settings.SwitchOutputHotkeyEnabled, v => _settings.SwitchOutputHotkeyEnabled = v,
            () => _settings.SwitchOutputHotkey, v => _settings.SwitchOutputHotkey = v, applyOutputHotkey, Changed);
        SwitchOutputToggle.IsChecked = settings.SwitchOutputOnConnect;
        CheckUpdatesToggle.IsChecked = settings.CheckForUpdates;
        OnToggle(CheckUpdatesToggle, () =>
        {
            _settings.CheckForUpdates = CheckUpdatesToggle.IsChecked == true;
            Changed();
        });

        StartupToggle.IsChecked = StartupRegistration.IsEnabled();
        AutoPopupToggle.IsChecked = settings.AutoPopupOnLidOpen;
        RememberPopupToggle.IsChecked = settings.RememberPopupPosition;
        PopupMainScreenToggle.IsChecked = settings.PopupOnMainScreen;
        RefreshPopupPosition();
        NotifyToggle.IsChecked = settings.NotifyLowBattery;
        ThresholdSection.IsEnabled = settings.NotifyLowBattery;
        AutoResumeToggle.IsChecked = settings.AutoResumeOnEarInsertion;
        AutoPlayToggle.IsChecked = settings.AutoPlayOnConnect;
        AutoPauseOnDisconnectToggle.IsChecked = settings.AutoPauseOnDisconnect;
        AutoResumeRow.IsEnabled = settings.AutoPauseOnEarRemoval;

        // Checked/Unchecked in plaats van Click: dan werkt het ook met het toetsenbord en een schermlezer.
        // Pas na het instellen van de beginwaarden aanmelden.
        OnToggle(StartupToggle, OnStartupChanged);
        OnToggle(AutoPopupToggle, OnAutoPopupChanged);
        OnToggle(RememberPopupToggle, () =>
        {
            _settings.RememberPopupPosition = RememberPopupToggle.IsChecked == true;
            RefreshPopupPosition();
            Changed();
        });
        OnToggle(PopupMainScreenToggle, () =>
        {
            _settings.PopupOnMainScreen = PopupMainScreenToggle.IsChecked == true;
            Changed();
        });
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
        OnToggle(SwitchOutputToggle, () =>
        {
            _settings.SwitchOutputOnConnect = SwitchOutputToggle.IsChecked == true;
            Changed();
        });
        OnToggle(AutoPlayToggle, () =>
        {
            _settings.AutoPlayOnConnect = AutoPlayToggle.IsChecked == true;
            Changed();
        });
        OnToggle(AutoPauseOnDisconnectToggle, () =>
        {
            _settings.AutoPauseOnDisconnect = AutoPauseOnDisconnectToggle.IsChecked == true;
            Changed();
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

        SetupTrayIconSettings();

        // Taal: de keuze werkt pas na herstarten (dan worden alle teksten opnieuw opgebouwd).
        foreach (var (language, label) in new[]
                 {
                     (AppLanguage.System, L.T("Zoals Windows")), (AppLanguage.Dutch, "Nederlands"), (AppLanguage.English, "English"),
                 })
        {
            AddOption(LanguagePanel, "Language", label, _settings.Language == language, () =>
            {
                _settings.Language = language;
                LanguageHint.Text = L.T("Herstart PodPilot om de nieuwe taal te zien.");
                LanguageHint.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
                Changed();
            });
        }

        foreach (var threshold in Thresholds)
        {
            AddOption(ThresholdPanel, "Threshold", $"{threshold}%", settings.LowBatteryThreshold == threshold, () =>
            {
                _settings.LowBatteryThreshold = threshold;
                Changed();
            });
        }

        var hasPreferred = devices.Any(d => d.DeviceId == settings.PreferredDeviceId);
        AddDeviceOption(L.T("Automatisch"), L.T("Het eerste gepairde AirPods-apparaat"), !hasPreferred, null);
        foreach (var device in devices)
        {
            var model = device.ProductId is ushort pid ? L.T(AirPodsModels.Lookup(pid).Name) : L.T("Model onbekend");
            AddDeviceOption(device.Name, model, device.DeviceId == settings.PreferredDeviceId, device.DeviceId);
        }

        if (devices.Count == 0)
        {
            DevicePanel.Children.Add(new TextBlock
            {
                Text = L.T("Er zijn geen gepairde AirPods gevonden. Koppel ze eerst via de Bluetooth-instellingen van Windows."),
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
            MainPage.Devices => NavDevices,
            MainPage.Settings => NavSettings,
            MainPage.Hotkeys => NavHotkeys,
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
        DevicesPage.Visibility = Visible(NavDevices);
        // Lijst verversen zolang het tabblad open is (apparaten kunnen ook via Windows verbinden).
        if (NavDevices.IsChecked == true)
        {
            _ = RefreshDevicesAsync();
            _deviceRefresh.Start();
        }
        else
        {
            _deviceRefresh.Stop();
        }
        SettingsPage.Visibility = Visible(NavSettings);
        HotkeysPage.Visibility = Visible(NavHotkeys);
        AboutPage.Visibility = Visible(NavAbout);

        static Visibility Visible(RadioButton nav) => nav.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // --- Overzicht ------------------------------------------------------------------------

    /// <summary>Klik op "Verbinden" / "Verbinding verbreken". De app regelt de rest.</summary>
    public event EventHandler? ConnectToggleRequested;

    private void OnConnectClick(object sender, RoutedEventArgs e) =>
        ConnectToggleRequested?.Invoke(this, EventArgs.Empty);

    // --- Apparaten -----------------------------------------------------------------------

    private readonly System.Collections.ObjectModel.ObservableCollection<DeviceCardViewModel> _devices = [];
    private readonly System.Windows.Threading.DispatcherTimer _deviceRefresh = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _refreshingDevices;

    /// <summary>Container-ID van de AirPods die PodPilot gebruikt (label "PodPilot" op die kaart).</summary>
    public Guid? PodPilotContainerId { get; set; }

    /// <summary>
    /// "Verbinden"/"Verbreken" op een kaart. De app voert het uit en geeft true terug als het lukte.
    /// </summary>
    public Func<AudioDevice, bool, Task<bool>>? DeviceToggleHandler { get; set; }

    /// <summary>Haalt de apparaten opnieuw op en werkt de kaarten bij (zonder de lijst te laten verspringen).</summary>
    public async Task RefreshDevicesAsync()
    {
        if (_refreshingDevices)
            return;
        _refreshingDevices = true;
        try
        {
            var found = await AudioDevices.GetAsync();
            DeviceList.ItemsSource ??= _devices;

            foreach (var device in found)
            {
                var card = _devices.FirstOrDefault(c => c.Device.ContainerId == device.ContainerId);
                if (card is null)
                    _devices.Add(card = new DeviceCardViewModel(device, device.ContainerId == PodPilotContainerId));
                else
                    card.Update(device);
                card.BatteryText = BatteryTextFor(card);
            }

            foreach (var gone in _devices.Where(c => found.All(d => d.ContainerId != c.Device.ContainerId)).ToList())
                _devices.Remove(gone);

            NoDevicesText.Visibility = _devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            await RefreshStreamSourcesAsync();
        }
        catch (Exception)
        {
            // Bluetooth niet beschikbaar: de lijst blijft zoals hij was.
        }
        finally
        {
            _refreshingDevices = false;
        }
    }

    /// <summary>Voor de PodPilot-AirPods de eigen meting (links/rechts/case), anders wat Windows weet.</summary>
    private string BatteryTextFor(DeviceCardViewModel card)
    {
        if (card.IsPodPilotDevice && DataContext is StatusViewModel { IsKnown: true } status)
            return status.Case.IsVisible
                ? L.T("Links {0} · Rechts {1} · Case {2}", status.Left.PercentText, status.Right.PercentText, status.Case.PercentText)
                : L.T("Batterij {0}", status.Left.PercentText);
        return card.Device.BatteryPercent is int percent ? L.T("Batterij {0}%", percent) : "";
    }

    private async void OnDeviceToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceCardViewModel card || DeviceToggleHandler is null)
            return;

        var connect = !card.IsConnected;
        card.SetBusy(true, connect ? L.T("Verbinden…") : L.T("Verbinding verbreken…"));
        var ok = await DeviceToggleHandler(card.Device, connect);
        card.SetBusy(false, ok ? null
            : connect ? L.T("Verbinden lukt niet. Staat het apparaat aan en in de buurt?")
            : L.T("Verbreken lukt niet"));
        await RefreshDevicesAsync();
    }

    // --- Pc als speaker ------------------------------------------------------------------

    /// <summary>De app regelt het streamen; het venster toont en kiest alleen.</summary>
    internal StreamingService? Streaming { get; set; }

    private List<StreamSource> _streamSources = [];
    private bool _streamBusy;

    /// <summary>Werkt de keuzelijst met telefoons en de knop bij.</summary>
    private async Task RefreshStreamSourcesAsync()
    {
        var sources = (await StreamingService.GetSourcesAsync()).ToList();
        if (!sources.Select(s => s.Id).SequenceEqual(_streamSources.Select(s => s.Id)))
        {
            _streamSources = sources;
            StreamSourcePanel.Children.Clear();
            var selected = Streaming?.Active?.Id ?? _settings.StreamSourceId ?? sources.FirstOrDefault()?.Id;
            foreach (var source in sources)
            {
                AddOption(StreamSourcePanel, "StreamSource", source.Name, source.Id == selected, () =>
                {
                    _settings.StreamSourceId = source.Id;
                    Changed();
                });
            }
            if (_settings.StreamSourceId is null && sources.FirstOrDefault() is { } first)
                _settings.StreamSourceId = first.Id;
        }

        NoStreamSourcesText.Visibility = sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowStreamState();
    }

    /// <summary>Status en knop: "Speelt af via deze pc: iPhone" + Verbreken, of Verbinden.</summary>
    public void ShowStreamState()
    {
        if (_streamBusy)
            return;

        var active = Streaming?.Active;
        StreamStatusText.Text = active is not null ? L.T("Speelt af via deze pc: {0}", active.Name) : L.T("Niet verbonden");
        StreamButton.Content = active is not null ? L.T("Verbreken") : L.T("Verbinden");
        StreamButton.IsEnabled = active is not null || _streamSources.Count > 0;
    }

    private async void OnStreamClick(object sender, RoutedEventArgs e)
    {
        if (Streaming is null)
            return;

        if (Streaming.Active is not null)
        {
            Streaming.Disconnect();
            ShowStreamState();
            return;
        }

        var source = _streamSources.FirstOrDefault(s => s.Id == _settings.StreamSourceId) ?? _streamSources.FirstOrDefault();
        if (source is null)
            return;

        _streamBusy = true;
        StreamButton.IsEnabled = false;
        StreamStatusText.Text = L.T("Verbinden met {0}…", source.Name);
        var ok = await Streaming.ConnectAsync(source);
        _streamBusy = false;
        ShowStreamState();
        if (!ok)
            StreamStatusText.Text = L.T("Verbinden met {0} lukt niet. Staat Bluetooth op de telefoon aan?", source.Name);
    }

    // --- Updates en standaardinstellingen ------------------------------------------------

    /// <summary>"Nu controleren" op het tabblad Over.</summary>
    public event EventHandler? CheckUpdatesRequested;

    /// <summary>"Bijwerken" op het tabblad Over.</summary>
    public event EventHandler? InstallUpdateRequested;

    /// <summary>"Terugzetten" (na bevestiging): alle instellingen naar de standaard.</summary>
    public event EventHandler? ResetRequested;

    /// <summary>Toont de stand van de updates op het tabblad Over.</summary>
    public void SetUpdateState(string text, bool canInstall, bool busy)
    {
        UpdateStatusText.Text = text;
        InstallUpdateButton.Visibility = canInstall ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdatesButton.IsEnabled = !busy;
    }

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e) => CheckUpdatesRequested?.Invoke(this, EventArgs.Empty);

    private void OnInstallUpdateClick(object sender, RoutedEventArgs e) => InstallUpdateRequested?.Invoke(this, EventArgs.Empty);

    private DateTime _resetArmedAt = DateTime.MinValue;

    /// <summary>
    /// Eerste klik: om bevestiging vragen (de knop verandert in "Ja, terugzetten"). Tweede klik binnen
    /// 5 seconden: echt terugzetten.
    /// </summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (DateTime.Now - _resetArmedAt < TimeSpan.FromSeconds(5))
        {
            ResetRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _resetArmedAt = DateTime.Now;
        var originalHint = ResetHint.Text;
        ResetButton.Content = L.T("Ja, terugzetten");
        ResetButton.Style = (Style)FindResource("AccentButton");
        ResetHint.Text = L.T("Weet je het zeker? Klik nog een keer om alles terug te zetten.");

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ResetButton.Content = L.T("Terugzetten");
            ResetButton.Style = (Style)FindResource("PillButton");
            ResetHint.Text = originalHint;
        };
        timer.Start();
    }

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

    /// <summary>Toont of de pop-up een eigen plek heeft; de knop werkt alleen dan.</summary>
    public void RefreshPopupPosition()
    {
        var custom = _settings.RememberPopupPosition && _settings.PopupX is not null && _settings.PopupY is not null;
        PopupPositionHint.Text = custom
            ? L.T("Je hebt de pop-up zelf verplaatst. PodPilot onthoudt die plek.")
            : _settings.RememberPopupPosition
                ? L.T("Rechtsonder. Sleep de pop-up aan de achtergrond om hem ergens anders neer te zetten.")
                : L.T("Altijd rechtsonder. Slepen kan, maar alleen voor die keer.");
        ResetPopupPositionButton.IsEnabled = custom;
    }

    private void OnResetPopupPositionClick(object sender, RoutedEventArgs e)
    {
        _settings.PopupX = null;
        _settings.PopupY = null;
        RefreshPopupPosition();
        Changed();
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

    // --- Systeemvak-icoon -----------------------------------------------------------------

    private static readonly string[] TrayFonts = ["Segoe UI", "Bahnschrift", "Arial", "Consolas"];

    private static readonly (TrayFontSize Size, string Label)[] TrayFontSizes =
        [(TrayFontSize.Small, L.T("Klein")), (TrayFontSize.Normal, L.T("Normaal")), (TrayFontSize.Large, L.T("Groot"))];

    // Kleuren voor het getal: "auto" (zwart of wit), "level" (batterijkleur) en een paar vaste kleuren
    // die op de betreffende taakbalk goed leesbaar zijn.
    private static readonly string[] TrayColorsLight = ["auto", "level", "#0067C0", "#8A5300", "#C42B1C"];
    private static readonly string[] TrayColorsDark = ["auto", "level", "#60CDFF", "#F7D046", "#FF99A4"];

    private void SetupTrayIconSettings()
    {
        AddOption(TrayStylePanel, "TrayStyle", L.T("Oordop met stip"), _settings.TrayStyle == TrayIconStyle.Icon,
            () => SetTray(() => _settings.TrayStyle = TrayIconStyle.Icon));
        AddOption(TrayStylePanel, "TrayStyle", L.T("Oordop met getal"), _settings.TrayStyle == TrayIconStyle.BudWithNumber,
            () => SetTray(() => _settings.TrayStyle = TrayIconStyle.BudWithNumber));
        AddOption(TrayStylePanel, "TrayStyle", L.T("Getal"), _settings.TrayStyle == TrayIconStyle.Number,
            () => SetTray(() => _settings.TrayStyle = TrayIconStyle.Number));

        foreach (var font in TrayFonts)
            AddOption(TrayFontPanel, "TrayFont", font, _settings.TrayFont == font,
                () => SetTray(() => _settings.TrayFont = font));

        foreach (var (size, label) in TrayFontSizes)
            AddOption(TrayFontSizePanel, "TrayFontSize", label, _settings.TrayFontSize == size,
                () => SetTray(() => _settings.TrayFontSize = size));

        AddTrayColors(TrayColorLightPanel, "TrayColorLight", TrayColorsLight, light: true,
            () => _settings.TrayColorLight, v => _settings.TrayColorLight = v);
        AddTrayColors(TrayColorDarkPanel, "TrayColorDark", TrayColorsDark, light: false,
            () => _settings.TrayColorDark, v => _settings.TrayColorDark = v);

        TrayDimToggle.IsChecked = _settings.TrayDimWhenNotAirPods;
        TrayDimSlider.Value = Math.Clamp(_settings.TrayDimOpacity, 10, 90);
        TrayDimValue.Text = $"{TrayDimSlider.Value:0}%";
        OnToggle(TrayDimToggle, () => SetTray(() => _settings.TrayDimWhenNotAirPods = TrayDimToggle.IsChecked == true));
        TrayDimSlider.ValueChanged += (_, _) =>
        {
            TrayDimValue.Text = $"{TrayDimSlider.Value:0}%";
            SetTray(() => _settings.TrayDimOpacity = (int)TrayDimSlider.Value);
        };

        RefreshTraySection();
    }

    private void AddTrayColors(Panel panel, string group, string[] values, bool light, Func<string> get, Action<string> set)
    {
        foreach (var value in values)
        {
            System.Windows.Media.Brush fill = value switch
            {
                "auto" => new System.Windows.Media.SolidColorBrush(light
                    ? System.Windows.Media.Color.FromRgb(32, 32, 32)
                    : System.Windows.Media.Color.FromRgb(240, 240, 240)),
                "level" => new System.Windows.Media.LinearGradientBrush(new System.Windows.Media.GradientStopCollection
                {
                    new(System.Windows.Media.Color.FromRgb(46, 173, 91), 0),
                    new(System.Windows.Media.Color.FromRgb(224, 161, 0), 0.5),
                    new(System.Windows.Media.Color.FromRgb(229, 72, 77), 1),
                }, 45),
                _ => new System.Windows.Media.SolidColorBrush(ThemeManager.ParseColor(value)!.Value),
            };

            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ColorSwatch"),
                GroupName = group,
                Background = fill,
                ToolTip = value switch { "auto" => L.T("Automatisch (zwart of wit)"), "level" => L.T("Batterijkleur (groen, oranje, rood)"), _ => value },
                IsChecked = get() == value,
            };
            swatch.Checked += (_, _) => SetTray(() => set(value));
            panel.Children.Add(swatch);
        }
    }

    /// <summary>Wijziging aan het tray-icoon: opslaan, toepassen en het voorbeeld bijwerken.</summary>
    private void SetTray(Action change)
    {
        change();
        RefreshTraySection();
        Changed();
    }

    private void RefreshTraySection()
    {
        TrayNumberSection.IsEnabled = _settings.TrayStyle != TrayIconStyle.Icon;
        TrayDimRow.IsEnabled = _settings.TrayDimWhenNotAirPods;

        // Op de echte grootte van het systeemvak tekenen en scherp vergroten (zie XAML): zo zie je
        // hier precies hoe leesbaar het in de taakbalk is.
        var options = TrayIconOptions.From(_settings);
        var size = TrayIconRenderer.TraySize;
        TrayPreviewLight.Source = ToImageSource(TrayIconRenderer.RenderBitmap(size, 85, lightTaskbar: true, options, dimmed: false));
        TrayPreviewDark.Source = ToImageSource(TrayIconRenderer.RenderBitmap(size, 85, lightTaskbar: false, options, dimmed: false));
    }

    private static System.Windows.Media.ImageSource ToImageSource(System.Drawing.Bitmap bitmap)
    {
        using (bitmap)
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
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
                ColorHint.Text = L.T("\"{0}\" is geen geldige kleur. Gebruik zes tekens, bijv. #8B5CF6.", hexBox.Text);
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

    // --- Sneltoetsen ---------------------------------------------------------------------

    private HotkeyEditor? _popupHotkeyEditor;
    private HotkeyEditor? _connectHotkeyEditor;
    private HotkeyEditor? _outputHotkeyEditor;

    protected override void OnClosed(EventArgs e)
    {
        // Venster dicht tijdens het opnemen: de bewaarde combinaties weer aanzetten.
        _popupHotkeyEditor?.Cancel();
        _connectHotkeyEditor?.Cancel();
        _outputHotkeyEditor?.Cancel();
        _deviceRefresh.Stop();
        base.OnClosed(e);
    }
}
