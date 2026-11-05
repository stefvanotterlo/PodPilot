using System.IO;
using System.Windows;
using Microsoft.Win32;
using PodPilot.App.Services;
using PodPilot.App.ViewModels;
using PodPilot.App.Views;
using PodPilot.Core.Devices;
using PodPilot.Core.Models;
using PodPilot.Core.Scanning;
using Windows.Devices.Bluetooth;

namespace PodPilot.App;

/// <summary>
/// Startpunt van de app: koppelt de scanner (Core) aan het tray-icoon en de pop-up.
/// </summary>
public partial class App : Application
{
    private static string SearchingText => L.T("Zoeken… open de case of doe de AirPods in");
    private static string UnknownText => L.T("Geen signaal: status onbekend");

    private Mutex? _singleInstance;
    private AirPodsScanner? _scanner;
    private TrayIconService? _tray;
    private GlobalHotkey? _popupHotkey;
    private GlobalHotkey? _connectHotkey;
    private GlobalHotkey? _outputHotkey;
    private readonly StreamingService _streaming = new();
    private PopupWindow? _popup;
    private PairedAppleDevice? _device;
    private DeviceConnectionMonitor? _connection;
    // Pas gemaakt na het kiezen van de taal (OnStartup), anders blijven "Links/Rechts" Nederlands.
    private StatusViewModel _status = null!;
    private readonly LidOpenDetector _lidDetector = new();
    private readonly LowBatteryMonitor _lowBattery = new();
    private readonly EarDetectionMonitor _ears = new();
    private readonly MediaController _media = new();
    private NowPlayingService? _nowPlaying;
    private BatteryHistory? _history;
    private int _inEarBeforePause;
    private string? _lastRawInEar;
    // Na zelf verbreken even geen automatisch verbinden (anders verbindt hij meteen weer).
    private DateTimeOffset _autoConnectBlockedUntil = DateTimeOffset.MinValue;
    private Task? _wake;          // lopende wekstap (SDP-query), gedeeld door vooraf wekken en verbinden
    private bool _preWoken;       // al vooraf gewekt sinds de AirPods in beeld kwamen
    private bool _autoConnecting; // er loopt een automatische verbindpoging
    private DateTimeOffset _autoConnectEnded = DateTimeOffset.MinValue;
    private AppSettings _settings = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Maar één exemplaar tegelijk, anders komen er twee icoontjes in het systeemvak.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\PodPilot.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DiagnosticLog.Start();
        _settings = AppSettings.Load();
        // Taal vóór alle vensters kiezen; wisselen werkt na herstarten.
        L.Init(_settings.Language);
        _status = new StatusViewModel();
        try
        {
            _history = new BatteryHistory(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "PodPilot", "battery-history.csv"), DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            // Zonder geschiedenis werkt alles, alleen zonder schatting van de resterende tijd.
            DiagnosticLog.Write($"batterijgeschiedenis niet te laden: {ex.Message}");
        }
        ApplySettings();
        StartupRegistration.RefreshPathIfEnabled();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // Vaste app-ID + Startmenu-snelkoppeling: nodig voor meldingsbanners zonder installer.
        StartMenuShortcut.SetProcessAppId();
        StartMenuShortcut.EnsureExists();

        // Klik op een melding: pop-up openen.
        NotificationService.Clicked += (_, _) => OnUiThread(() => _popup?.ShowAtCorner());

        _status.MarkUnknown(SearchingText);
        _nowPlaying = new NowPlayingService(_status.NowPlaying, Dispatcher);
        _ = _nowPlaying.StartAsync();
        _popup = new PopupWindow { DataContext = _status };
        ThemeManager.ApplyTo(_popup, _settings.PopupTheme);
        _popup.ConnectToggleRequested += async (_, _) => await ToggleConnectionAsync();
        _popup.SettingsRequested += (_, _) => OpenMainWindow(MainPage.Settings);
        ApplyPopupPosition();
        _popup.PositionChanged += (_, p) =>
        {
            // Versleept. Moet de plek niet onthouden worden, dan staat hij de volgende keer weer
            // rechtsonder (deze keer blijft hij waar je hem neerzette).
            if (!_settings.RememberPopupPosition)
            {
                ApplyPopupPosition();
                DiagnosticLog.Write($"pop-up versleept naar {p.X},{p.Y} (niet onthouden)");
                return;
            }

            _settings.PopupX = p.X;
            _settings.PopupY = p.Y;
            _settings.Save();
            _mainWindow?.RefreshPopupPosition();
            DiagnosticLog.Write($"pop-up versleept naar {p.X},{p.Y}");
        };
        _tray = new TrayIconService();
        UpdateTrayAppearance();
        // Of het geluid via de AirPods gaat, kan op elk moment veranderen (ook via Windows zelf).
        _outputWatch.Tick += (_, _) => UpdateTrayAppearance();
        _outputWatch.Start();
        _tray.OpenRequested += (_, _) => TogglePopup();
        _tray.MenuRequested += (_, _) => ShowTrayMenu();
        UpdateTray();

        _popupHotkey = new GlobalHotkey();
        _popupHotkey.Pressed += (_, _) => TogglePopup();
        ApplyPopupHotkey(_settings.PopupHotkeyEnabled ? Hotkey.Parse(_settings.PopupHotkey) : null);

        _connectHotkey = new GlobalHotkey();
        _connectHotkey.Pressed += async (_, _) => await ToggleConnectionAsync();
        ApplyConnectHotkey(_settings.ConnectHotkeyEnabled ? Hotkey.Parse(_settings.ConnectHotkey) : null);

        // De telefoon kan het streamen ook zelf stoppen: dan het venster bijwerken.
        _streaming.StateChanged += (_, _) => OnUiThread(() => _mainWindow?.ShowStreamState());

        _outputHotkey = new GlobalHotkey();
        _outputHotkey.Pressed += (_, _) => SwitchToNextOutput();
        ApplyOutputHotkey(_settings.SwitchOutputHotkeyEnabled ? Hotkey.Parse(_settings.SwitchOutputHotkey) : null);

        _scanner = new AirPodsScanner();
        _scanner.StatusUpdated += (_, s) => OnUiThread(() => ApplyStatus(s));
        _scanner.StatusLost += (_, _) => OnUiThread(() => MarkUnknown(UnknownText));
        _scanner.ScannerStopped += (_, error) =>
        {
            // Success = wij hebben zelf gestopt (bij afsluiten).
            if (error != BluetoothError.Success)
                OnUiThread(() => MarkUnknown(L.T("Bluetooth staat uit of is niet beschikbaar")));
        };

        await ConfigureForPairedDeviceAsync(_scanner);
        _scanner.Start();

        if (_settings.CheckForUpdates)
            _ = CheckForUpdatesAsync(notify: true);

        // "PodPilot.exe --show" opent de pop-up meteen (handig bij testen), "--main" het hoofdvenster
        // en "--settings" het hoofdvenster op het tabblad Instellingen.
        if (e.Args.Contains("--show"))
            _popup.ShowAtCorner();
        if (e.Args.Contains("--main"))
            OpenMainWindow(MainPage.Overview);
        if (e.Args.Contains("--settings"))
            OpenMainWindow(MainPage.Settings);
        // "--tray-menu" opent het menu van het tray-icoon op de plek van de muis (om te testen).
        if (e.Args.Contains("--tray-menu"))
            ShowTrayMenu();
        // "--test-toast" toont de melding van het wisselen van de geluidsuitgang, zonder te wisselen.
        if (e.Args.Contains("--test-toast"))
            OutputToast.Show(L.T("Geluid via: {0}", AudioOutputs.GetActive().FirstOrDefault(o => o.Id == AudioOutputs.GetDefaultId())?.Name),
                ThemeManager.CreateColorsFor(_settings.PopupTheme));

        // "PodPilot.exe --test-notification" toont een voorbeeld van de melding bij lage batterij.
        if (e.Args.Contains("--test-notification"))
            NotificationService.ShowLowBattery(_status.DeviceName,
                [new LowBatteryItem(L.T("Links"), _settings.LowBatteryThreshold), new LowBatteryItem(L.T("Rechts"), 10)]);
    }

    /// <summary>
    /// Zoekt de gepairde AirPods en kiest er één: de keuze uit de instellingen, of anders de eerste
    /// met een bekend model. Hun naam wordt de titel en hun model het filter voor de scanner,
    /// zodat de AirPods van anderen worden genegeerd.
    /// </summary>
    private async Task ConfigureForPairedDeviceAsync(AirPodsScanner scanner)
    {
        IReadOnlyList<PairedAppleDevice> paired = [];
        try
        {
            paired = await GetPairedHeadphonesAsync();
        }
        catch (Exception)
        {
            // Zonder gepaird apparaat werkt de scanner ook, alleen zonder modelfilter en zonder verbindknop.
        }

        var device = paired.FirstOrDefault(d => d.DeviceId == _settings.PreferredDeviceId)
                     ?? paired.FirstOrDefault(d => d.ProductId.HasValue)
                     ?? paired.FirstOrDefault();

        // Andere AirPods gekozen: de oude status hoort niet bij de nieuwe.
        if (_device is not null && device?.DeviceId != _device.DeviceId)
            MarkUnknown(SearchingText);

        _connection?.Dispose();
        _connection = null;
        _device = device;
        _lastAudioActive = null;

        if (device is null)
        {
            _status.DeviceName = "AirPods";
            scanner.PreferredModelId = null;
            _status.SetConnection(isPaired: false, isConnected: false);
            UpdateTray();
            return;
        }

        _status.DeviceName = device.Name;
        scanner.PreferredModelId = device.ProductId;
        UpdateTray();

        try
        {
            // Bij elke wijziging van de Bluetooth-verbinding de audiostatus opnieuw bepalen.
            _connection = await DeviceConnectionMonitor.CreateAsync(device.DeviceId);
            if (_connection is not null)
                _connection.ConnectionChanged += (_, connected) => OnUiThread(async () =>
                {
                    // Bluetooth weg terwijl de audio liep: meteen pauzeren, niet pas na de controle
                    // hieronder (anders klinkt er even muziek uit de laptopluidsprekers).
                    if (!connected && _status.IsConnected)
                        await PauseAfterDisconnectAsync();
                    await RefreshConnectionAsync(delay: true);
                });

            await RefreshConnectionAsync(delay: false);
        }
        catch (Exception)
        {
            _status.SetConnection(isPaired: true, isConnected: false);
        }
    }

    /// <summary>
    /// Gekoppelde Apple-apparaten die een geluidsuitgang hebben: AirPods en Beats, geen iPhone, iPad
    /// of Mac (die zijn ook Apple, maar geen koptelefoon). Zo kiest "Automatisch" nooit je telefoon.
    /// </summary>
    private static async Task<IReadOnlyList<PairedAppleDevice>> GetPairedHeadphonesAsync()
    {
        var paired = await PairedDeviceService.GetPairedAppleDevicesAsync();
        var withAudio = (await AudioDevices.GetAsync()).Select(d => d.ContainerId).ToHashSet();
        return paired.Where(d => d.ContainerId is Guid c && withAudio.Contains(c)).ToList();
    }

    // --- Hoofdvenster -------------------------------------------------------------------

    private Views.MainWindow? _mainWindow;

    /// <summary>Opent het hoofdvenster op een tabblad (of haalt het naar voren als het al open is).</summary>
    private async void OpenMainWindow(MainPage page)
    {
        _popup?.HidePopup();
        if (_mainWindow is not null)
        {
            _mainWindow.ShowPage(page);
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            return;
        }

        IReadOnlyList<PairedAppleDevice> devices = [];
        try
        {
            devices = await GetPairedHeadphonesAsync();
        }
        catch (Exception)
        {
            // Het venster toont dan "geen gepairde AirPods gevonden".
        }

        var previousDevice = _settings.PreferredDeviceId;
        _mainWindow = new Views.MainWindow(_status, _settings, devices, ApplyPopupHotkey, ApplyConnectHotkey,
            ApplyOutputHotkey);
        ThemeManager.ApplyTo(_mainWindow, _settings.Theme);
        _mainWindow.ShowPage(page);
        _mainWindow.ConnectToggleRequested += async (_, _) => await ToggleConnectionAsync();
        _mainWindow.CheckUpdatesRequested += async (_, _) => await CheckForUpdatesAsync(notify: false);
        _mainWindow.InstallUpdateRequested += async (_, _) => await InstallUpdateAsync();
        _mainWindow.ResetRequested += (_, _) => ResetSettings();
        _mainWindow.PodPilotContainerId = _device?.ContainerId;
        _mainWindow.Streaming = _streaming;
        _mainWindow.DeviceToggleHandler = ToggleDeviceAsync;
        ShowUpdateState();
        // Alleen in dit hoofdvenster en alleen zolang het open is: sluiten = weg.
        _mainWindow.EasterEggTriggered += (_, _) =>
        {
            DiagnosticLog.Write("verborgen achtergrond aan (tot het hoofdvenster sluit)");
            ApplyEasterEgg(_mainWindow);
            _mainWindow?.HideAboutContent();
        };
        _mainWindow.SettingsChanged += async (_, _) =>
        {
            ApplySettings();
            if (_settings.PreferredDeviceId != previousDevice && _scanner is not null)
            {
                previousDevice = _settings.PreferredDeviceId;
                await ConfigureForPairedDeviceAsync(_scanner);
            }
        };
        _mainWindow.Closed += (_, _) => _mainWindow = null;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    /// <summary>
    /// Zet de verborgen foto als achtergrond van het hoofdvenster (nooit de pop-up). Sleutels direct
    /// in Window.Resources gaan vóór de kleurendictionary van het thema, dus ook na een themawissel
    /// blijft hij staan. Een nieuw hoofdvenster heeft ze niet: na sluiten is de foto weg.
    /// </summary>
    private static void ApplyEasterEgg(Window? window)
    {
        if (window is null)
            return;

        var photo = new System.Windows.Media.ImageBrush(new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Assets/easter-egg.png")))
        {
            Stretch = System.Windows.Media.Stretch.UniformToFill,
        };
        photo.Freeze();
        window.Resources["PageBackground"] = photo;
        window.Resources["WindowBackground"] = photo;
    }

    /// <summary>
    /// Verbinden of verbreken vanaf het tabblad Apparaten. De AirPods van PodPilot gaan via de gewone
    /// route (met de opties zoals automatisch afspelen); andere apparaten via dezelfde techniek:
    /// wekken, het verzoek op het Bluetooth-audiofilter, en wachten tot het geluid (niet) actief is.
    /// </summary>
    private async Task<bool> ToggleDeviceAsync(AudioDevice device, bool connect)
    {
        if (device.ContainerId == _device?.ContainerId)
        {
            if (_status.IsConnected != connect)
                await ToggleConnectionAsync();
            return _status.IsConnected == connect;
        }

        DiagnosticLog.Write($"apparaten: {(connect ? "verbinden met" : "verbreken van")} {device.Name}");
        bool ok;
        if (connect)
        {
            using (var monitor = await DeviceConnectionMonitor.CreateAsync(device.DeviceId))
            {
                if (monitor is not null)
                    await monitor.WakeAsync();
            }

            ok = await BluetoothAudioConnector.ConnectAsync(device.ContainerId, device.Name) == AudioConnectResult.Requested
                 && await BluetoothAudioConnector.WaitForAudioAsync(device.ContainerId, device.Name, active: true, TimeSpan.FromSeconds(10));
        }
        else
        {
            ok = await BluetoothAudioConnector.DisconnectAsync(device.ContainerId, device.Name) == AudioConnectResult.Requested
                 && await BluetoothAudioConnector.WaitForAudioAsync(device.ContainerId, device.Name, active: false, TimeSpan.FromSeconds(6));
        }

        DiagnosticLog.Write($"apparaten: {device.Name} → {(ok ? "gelukt" : "mislukt")}");
        return ok;
    }

    /// <summary>
    /// Alle instellingen terug naar de standaard (settings.json opnieuw). "Starten met Windows" staat
    /// in de registry en blijft zoals het is. Het hoofdvenster gaat opnieuw open met de nieuwe waarden.
    /// </summary>
    private void ResetSettings()
    {
        DiagnosticLog.Write("instellingen: terug naar standaard");
        _settings = new AppSettings();
        _settings.Save();

        ApplySettings();
        ApplyPopupHotkey(null);
        ApplyConnectHotkey(null);
        ApplyOutputHotkey(null);

        _mainWindow?.Close();
        OpenMainWindow(MainPage.Settings);
    }

    // --- Updates -----------------------------------------------------------------------

    private UpdateInfo? _availableUpdate;

    /// <summary>
    /// Kijkt op GitHub of er een nieuwere versie is en toont dat op het tabblad Over. Met
    /// <paramref name="notify"/> komt er ook een Windows-melding, maar maar één keer per versie.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool notify)
    {
        _mainWindow?.SetUpdateState(L.T("Zoeken naar updates…"), canInstall: false, busy: true);
        try
        {
            _availableUpdate = await UpdateService.CheckAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"updates: zoeken mislukt: {ex.Message}");
            _mainWindow?.SetUpdateState(L.T("Zoeken lukt niet. Is er internet?"), canInstall: false, busy: false);
            return;
        }

        ShowUpdateState();
        if (notify && _availableUpdate is { } update && _settings.UpdateNotifiedVersion != update.Version.ToString())
        {
            _settings.UpdateNotifiedVersion = update.Version.ToString();
            _settings.Save();
            NotificationService.ShowUpdateAvailable(update.Version, () => OnUiThread(() => OpenMainWindow(MainPage.About)));
        }
    }

    private void ShowUpdateState() => _mainWindow?.SetUpdateState(
        _availableUpdate is { } update
            ? L.T("Versie {0} is beschikbaar (je hebt {1}).", update.Version, UpdateService.CurrentVersion)
            : L.T("Je hebt de nieuwste versie ({0}).", UpdateService.CurrentVersion),
        canInstall: _availableUpdate is not null, busy: false);

    /// <summary>"Bijwerken": downloaden, stil installeren en opnieuw starten.</summary>
    private async Task InstallUpdateAsync()
    {
        if (_availableUpdate is not { } update)
            return;

        try
        {
            var progress = new Progress<double>(p =>
                _mainWindow?.SetUpdateState(L.T("Downloaden… {0:P0}", p), canInstall: false, busy: true));
            _mainWindow?.SetUpdateState(L.T("Downloaden…"), canInstall: false, busy: true);
            await UpdateService.StartInstallAsync(update, progress);
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"updates: bijwerken mislukt: {ex.Message}");
            _mainWindow?.SetUpdateState(L.T("Bijwerken lukt niet. Probeer het later opnieuw."), canInstall: true, busy: false);
            return;
        }

        // De installer vervangt de exe; die moet daarvoor dicht zijn. Daarna start hij PodPilot opnieuw.
        _mainWindow?.SetUpdateState(L.T("Installeren… PodPilot start zo opnieuw."), canInstall: false, busy: true);
        await Task.Delay(500);
        Shutdown();
    }

    /// <summary>Zet de sneltoets voor de pop-up (null = uit). Geeft false als hij al in gebruik is.</summary>
    private bool ApplyPopupHotkey(Hotkey? hotkey) => ApplyHotkey(_popupHotkey, hotkey, "pop-up");

    /// <summary>Zet de sneltoets voor verbinden/verbreken (null = uit). Geeft false als hij al in gebruik is.</summary>
    private bool ApplyConnectHotkey(Hotkey? hotkey) => ApplyHotkey(_connectHotkey, hotkey, "verbinden");

    /// <summary>Zet de sneltoets voor het wisselen van de geluidsuitgang (null = uit).</summary>
    private bool ApplyOutputHotkey(Hotkey? hotkey) => ApplyHotkey(_outputHotkey, hotkey, "geluidsuitgang");

    private static bool ApplyHotkey(GlobalHotkey? target, Hotkey? hotkey, string name)
    {
        var ok = target?.Set(hotkey) ?? false;
        if (hotkey is not null)
            DiagnosticLog.Write($"sneltoets {name} {hotkey}: {(ok ? "actief" : "al in gebruik")}");
        return ok;
    }

    /// <summary>Past de huidige instellingen toe (thema, meldingsdrempel). Bij het opstarten en na elke wijziging.</summary>
    private void ApplySettings()
    {
        ApplyThemes();
        UpdateTrayAppearance();
        ApplyPopupPosition();
        _lowBattery.Threshold = _settings.NotifyLowBattery ? _settings.LowBatteryThreshold : 0;
    }

    /// <summary>De bewaarde plek van de pop-up doorgeven (geen, of niet onthouden = rechtsonder).</summary>
    private void ApplyPopupPosition()
    {
        if (_popup is not null)
            _popup.CustomPosition = _settings.RememberPopupPosition && _settings.PopupX is int x && _settings.PopupY is int y
                ? new System.Drawing.Point(x, y)
                : null;
    }

    /// <summary>
    /// Hoofdvenster en pop-up hebben elk een eigen thema. Het thema van het hoofdvenster is ook de
    /// standaard voor de rest (tooltips en dergelijke).
    /// </summary>
    private readonly System.Windows.Threading.DispatcherTimer _outputWatch = new() { Interval = TimeSpan.FromSeconds(2) };

    /// <summary>
    /// Stijl van het tray-icoon uit de instellingen, en gedimd als dat aanstaat en het geluid niet
    /// via de AirPods gaat (bijv. via de laptopluidspreker).
    /// </summary>
    private void UpdateTrayAppearance()
    {
        var dimmed = _settings.TrayDimWhenNotAirPods && !OutputIsAirPods();
        _tray?.SetAppearance(TrayIconOptions.From(_settings), dimmed);
    }

    private bool OutputIsAirPods()
    {
        if (_device?.ContainerId is not Guid containerId)
            return false;
        var defaultId = AudioOutputs.GetDefaultId();
        return AudioOutputs.GetActive().Any(o => o.Id == defaultId && o.ContainerId == containerId);
    }

    private void ApplyThemes()
    {
        ThemeManager.CustomBackground = _settings.UseCustomBackground ? ThemeManager.ParseColor(_settings.CustomBackground) : null;
        ThemeManager.CustomAccent = _settings.UseCustomAccent ? ThemeManager.ParseColor(_settings.CustomAccent) : null;
        ThemeManager.Apply(_settings.Theme);
        if (_mainWindow is not null)
            ThemeManager.ApplyTo(_mainWindow, _settings.Theme);
        if (_popup is not null)
            ThemeManager.ApplyTo(_popup, _settings.PopupTheme);
        _tray?.RefreshTheme();
    }

    /// <summary>
    /// Bepaalt of de audio verbonden is. Een Bluetooth-verbinding alleen is niet genoeg: we kijken
    /// of het audio-endpoint actief is. Dat gebeurt soms pas een moment na de Bluetooth-verbinding,
    /// vandaar de optionele vertraging.
    /// </summary>
    private async Task RefreshConnectionAsync(bool delay)
    {
        if (_device?.ContainerId is not Guid containerId)
            return;

        // Met vertraging: tot 2 s lang elke 0,25 s kijken of de audio verandert, in plaats van
        // altijd 1,5 s te wachten. Zo zie je "verbonden" zodra het zo is.
        var active = await BluetoothAudioConnector.IsAudioActiveAsync(containerId, _device.Name);
        for (var i = 0; delay && i < 8 && active == _status.IsConnected; i++)
        {
            await Task.Delay(250);
            active = await BluetoothAudioConnector.IsAudioActiveAsync(containerId, _device.Name);
        }
        DiagnosticLog.Write($"verbinding: audio {(active ? "verbonden" : "niet verbonden")}");
        _status.SetConnection(isPaired: true, isConnected: active);

        // Alleen echte overgangen tellen: bij het opstarten (of na wisselen van AirPods) is de
        // vorige stand onbekend, en dan moet er niet ineens muziek gaan spelen.
        var previous = _lastAudioActive;
        _lastAudioActive = active;
        if (previous == false && active)
        {
            if (_settings.SwitchOutputOnConnect)
                SwitchOutputToDevice(containerId);

            if (_settings.AutoPlayOnConnect)
            {
                // Even wachten tot Windows het geluid naar de AirPods heeft omgezet.
                await Task.Delay(TimeSpan.FromSeconds(1));
                await _media.PlayCurrentAsync();
            }
        }
        else if (previous == true && !active)
        {
            await PauseAfterDisconnectAsync();
        }
    }

    // Laatst bekende audiostatus; null = nog onbekend (net gestart of andere AirPods gekozen).
    private bool? _lastAudioActive;

    /// <summary>Maakt de stereo-uitgang van de AirPods het standaard-afspeelapparaat.</summary>
    private static void SwitchOutputToDevice(Guid containerId)
    {
        if (AudioOutputs.FindForDevice(containerId) is not { } output)
        {
            DiagnosticLog.Write("geluid: geen uitgang van de AirPods gevonden");
            return;
        }

        if (AudioOutputs.GetDefaultId() == output.Id)
            return;

        DiagnosticLog.Write($"geluid: naar {output.Name} → {(AudioOutputs.SetDefault(output.Id) ? "gelukt" : "mislukt")}");
    }

    /// <summary>Sneltoets: naar de volgende geluidsuitgang, met een korte melding in beeld.</summary>
    private void SwitchToNextOutput()
    {
        if (AudioOutputs.Next() is not { } next)
            return;

        var ok = AudioOutputs.SetDefault(next.Id);
        DiagnosticLog.Write($"geluid: sneltoets, naar {next.Name} → {(ok ? "gelukt" : "mislukt")}");
        OutputToast.Show(ok ? L.T("Geluid via: {0}", next.Name) : L.T("Wisselen naar {0} lukt niet", next.Name),
            ThemeManager.CreateColorsFor(_settings.PopupTheme));
    }

    /// <summary>Pauzeert alles wat speelt als de AirPods loskomen (als die optie aan staat).</summary>
    private async Task PauseAfterDisconnectAsync()
    {
        if (!_settings.AutoPauseOnDisconnect)
            return;

        DiagnosticLog.Write("media: AirPods niet meer verbonden, pauzeren");
        await _media.PauseIfPlayingAsync();
        // Niet later "hervatten" bij het indoen: dit was geen pauze door oordetectie.
        _media.Forget();
    }

    /// <summary>
    /// Verbindt of verbreekt de audio. Lukt dat niet binnen een paar seconden, dan openen we de
    /// Bluetooth-instellingen van Windows (fallback).
    /// </summary>
    private async Task ToggleConnectionAsync()
    {
        if (_device?.ContainerId is not Guid containerId || _connection is null)
        {
            // Niet gepaird (of het apparaat is niet te openen): koppelen kan alleen via Windows zelf.
            WindowsSettings.OpenBluetooth();
            return;
        }

        if (!_status.IsConnected)
        {
            _autoConnectBlockedUntil = DateTimeOffset.MinValue;
            // Bluetooth-instellingen alleen als de AirPods wel in beeld zijn; anders staat er in de
            // pop-up dat je de case moet openen.
            if (!await ConnectAsync(_device, containerId, _connection, automatic: false) && _status.IsKnown)
                WindowsSettings.OpenBluetooth();
            return;
        }

        // Zelf verbroken: 10 seconden niet automatisch verbinden, zodat de verbinding niet meteen
        // terugkomt. Daarna werkt "verbinden als je ze indoet" weer, ook zonder de case te sluiten.
        _autoConnectBlockedUntil = DateTimeOffset.Now.AddSeconds(10);
        DiagnosticLog.Write("verbreken (knop): zelf verbroken, 10 s geen automatisch verbinden");
        _status.SetBusy(true, L.T("Verbinding verbreken…"));
        var result = await BluetoothAudioConnector.DisconnectAsync(containerId, _device.Name);
        var succeeded = result == AudioConnectResult.Requested
                        && await BluetoothAudioConnector.WaitForAudioAsync(containerId, _device.Name,
                               active: false, TimeSpan.FromSeconds(6));

        _status.SetBusy(false, succeeded ? null : L.T("Verbreken lukt niet; gebruik Instellingen"));
        await RefreshConnectionAsync(delay: false);

        if (!succeeded)
            WindowsSettings.OpenBluetooth();
    }

    /// <summary>Start de wekstap (SDP-query), of geeft de query terug die nog loopt.</summary>
    private Task StartWake(DeviceConnectionMonitor connection)
    {
        if (_wake is null || _wake.IsCompleted)
            _wake = connection.WakeAsync();
        return _wake;
    }

    /// <summary>
    /// Vooraf wekken: zodra de AirPods in beeld komen (case open, of uit de case) en de audio niet
    /// verbonden is, alvast de Bluetooth-verbinding opzetten. Verbinden (knop, oordop in) hoeft dan
    /// niet meer te wekken: ~2 s in plaats van ~8 s. Eén keer per keer dat ze in beeld komen.
    /// </summary>
    private async void PreWake()
    {
        if (_preWoken || _connection is not { } connection || _status.IsConnected || !_status.CanToggleConnection)
            return;
        _preWoken = true;
        if (connection.IsConnected)
            return;

        var t0 = DateTime.UtcNow;
        await StartWake(connection);
        DiagnosticLog.Write($"vooraf wekken: klaar na {(DateTime.UtcNow - t0).TotalSeconds:0.0} s, " +
                            $"Bluetooth {(connection.IsConnected ? "ja" : "nee")}");
    }

    /// <summary>Verbindt de audio en wacht tot het endpoint actief is. Geeft true als dat lukte.</summary>
    private async Task<bool> ConnectAsync(PairedAppleDevice device, Guid containerId,
        DeviceConnectionMonitor connection, bool automatic)
    {
        _status.SetBusy(true, automatic ? L.T("Automatisch verbinden…") : L.T("Verbinden…"));
        DiagnosticLog.Write($"verbinden ({(automatic ? "automatisch" : "knop")}): start");

        // Tot 30 seconden lang proberen. Net na het indoen (of openen van de case) zijn de AirPods
        // nog bezig met opstarten; een verzoek dat dan komt, wordt aangenomen maar genegeerd.
        // Werkwijze: eerst een Bluetooth-verbinding (ACL) via de wekstap; sommige drivers (Intel
        // Smart Sound) negeren het audioverzoek zonder. Pas als die er is één verzoek sturen en dat
        // 5 s de tijd geven: elke 2,5 s een nieuw verzoek brak een opbouwende verbinding af (gemeten).
        var started = DateTime.UtcNow;
        var deadline = started + TimeSpan.FromSeconds(30);
        var result = AudioConnectResult.Failed;
        var succeeded = false;
        var attempt = 0;

        // Niet doen: het verzoek al vóór of tijdens de wekstap sturen. Getest (5-11-2026): dat
        // verzoek werd genegeerd en ook de volgende pogingen mislukten, 22 s in plaats van 8 s.
        while (!succeeded && DateTime.UtcNow < deadline)
        {
            var woke = "al verbonden";
            if (!connection.IsConnected)
            {
                // De SDP-query kan tijdens het opstarten ~15 s hangen; een query die nog loopt (ook
                // die van het vooraf wekken) niet opnieuw starten, en ondertussen kijken of de
                // Bluetooth-verbinding er al is.
                var wake = StartWake(connection);
                var t0 = DateTime.UtcNow;
                while (!wake.IsCompleted && !connection.IsConnected && DateTime.UtcNow < deadline)
                    await Task.WhenAny(wake, Task.Delay(250));

                if (!connection.IsConnected && !wake.IsCompleted)
                    break; // deadline: AirPods niet bereikbaar
                woke = connection.IsConnected
                    ? $"Bluetooth na {(DateTime.UtcNow - t0).TotalSeconds:0.0} s"
                    : "wekken klaar, geen Bluetooth";
            }

            attempt++;
            result = await BluetoothAudioConnector.ConnectAsync(containerId, device.Name);
            succeeded = result == AudioConnectResult.Requested
                        && await BluetoothAudioConnector.WaitForAudioAsync(containerId, device.Name,
                               active: true, TimeSpan.FromSeconds(5));
            DiagnosticLog.Write($"verbinden: poging {attempt} {(succeeded ? "gelukt" : "mislukt")} " +
                                $"na {(DateTime.UtcNow - started).TotalSeconds:0.0} s ({woke}, verzoek {result})");

            // Geen verzoek mogelijk (geen audio-endpoint) of de AirPods slapen: opnieuw proberen heeft geen zin.
            if (result == AudioConnectResult.NoAudioEndpoint || !_status.IsKnown)
                break;
        }

        // Een laat aangekomen verzoek kan alsnog gelukt zijn.
        if (!succeeded && attempt > 0)
            succeeded = await BluetoothAudioConnector.WaitForAudioAsync(containerId, device.Name,
                active: true, TimeSpan.Zero);

        // Geen advertenties = de AirPods slapen (dichte case) of zijn buiten bereik: dan helpen de
        // Bluetooth-instellingen niet, maar de case openen wel.
        _status.SetBusy(false, succeeded ? null
            : !_status.IsKnown ? L.T("Open de case of doe de AirPods in, en probeer opnieuw")
            : automatic ? L.T("Automatisch verbinden lukt niet")
            : L.T("Lukt niet; verbind via Instellingen"));
        await RefreshConnectionAsync(delay: false);
        DiagnosticLog.Write($"verbinden ({(automatic ? "automatisch" : "knop")}): " +
                            $"{(succeeded ? "gelukt" : $"mislukt, verzoek {result}, AirPods {(_status.IsKnown ? "in beeld" : "niet in beeld")}")}");
        return succeeded;
    }

    /// <summary>
    /// Verbindt automatisch (case open of oordop in), als dat aanstaat. Niet als de audio al
    /// verbonden is, als er al een poging loopt, binnen 30 seconden na de vorige poging, of als je
    /// zelf net hebt verbroken. Mislukt het, dan blijft het stil (geen Bluetooth-instellingen).
    /// </summary>
    private async void TryAutoConnect(string reason)
    {
        if (_device?.ContainerId is not Guid containerId || _connection is not { } connection)
            return;
        if (_status.IsConnected || !_status.CanToggleConnection)
            return;

        if (DateTimeOffset.Now < _autoConnectBlockedUntil)
        {
            DiagnosticLog.Write($"auto-verbinden ({reason}): overgeslagen, net zelf verbroken");
            return;
        }

        // Niet twee pogingen tegelijk, en na een mislukte poging 5 s rust. (Vroeger 30 s na de
        // start: dan werd "oordop in" vlak na een mislukte "case open"-poging overgeslagen.)
        if (_autoConnecting || DateTimeOffset.Now - _autoConnectEnded < TimeSpan.FromSeconds(5))
        {
            DiagnosticLog.Write($"auto-verbinden ({reason}): overgeslagen, {(_autoConnecting ? "loopt al" : "net geprobeerd")}");
            return;
        }

        _autoConnecting = true;
        DiagnosticLog.Write($"auto-verbinden ({reason}): start");
        try
        {
            var succeeded = await ConnectAsync(_device, containerId, connection, automatic: true);
            DiagnosticLog.Write($"auto-verbinden ({reason}): {(succeeded ? "gelukt" : "mislukt")}");
        }
        finally
        {
            _autoConnecting = false;
            _autoConnectEnded = DateTimeOffset.Now;
        }
    }

    private void ApplyStatus(AirPodsStatus status)
    {
        _status.Apply(status);
        UpdateTray();

        if (_history is not null)
        {
            _history.Record(status);
            _status.Left.SetRemaining(_history.EstimateRemaining(Bud.Left, status.Left, status.ReceivedAt));
            _status.Right.SetRemaining(status.Model.IsHeadphones ? null
                : _history.EstimateRemaining(Bud.Right, status.Right, status.ReceivedAt));
        }

        // Case geopend in de buurt van de pc: pop-up tonen zonder focus te pakken.
        if (_lidDetector.Process(status))
        {
            if (_settings.AutoPopupOnLidOpen && _popup is { IsVisible: false })
                _popup.ShowAtCorner(activate: false);
            if (_settings.AutoConnectOnLidOpen)
                TryAutoConnect("case open");
        }

        // AirPods in beeld maar niet verbonden: alvast wekken, dan gaat verbinden straks sneller.
        if (!_status.IsConnected)
            PreWake();

        // Oordop net onder de drempel: Windows-melding. Daarnaast de pop-up (zonder focus), want
        // Windows verbergt banners soms (Niet storen, banners uit, automatische regels). Dan
        // belandt de melding alleen stil in het Meldingencentrum.
        var low = _lowBattery.Process(status);
        if (low.Count > 0)
        {
            NotificationService.ShowLowBattery(_status.DeviceName, low);
            if (_popup is { IsVisible: false })
                _popup.ShowAtCorner(activate: false);
        }

        HandleEarDetection(status);
    }

    /// <summary>
    /// Pauzeert media als er een oordop uitgaat en hervat als hij weer in gaat. Dit gebeurt alleen
    /// als de AirPods-audio met deze pc verbonden is: luister je via je telefoon, dan moet muziek op
    /// de pc niet stoppen.
    /// </summary>
    private async void HandleEarDetection(AirPodsStatus status)
    {
        var previous = _ears.StableInEarCount ?? 0;
        var earEvent = _ears.Process(status);

        var rawInEar = $"L {(status.Left.IsInEar ? "in" : "uit")}, R {(status.Right.IsInEar ? "in" : "uit")}";
        if (rawInEar != _lastRawInEar)
        {
            _lastRawInEar = rawInEar;
            DiagnosticLog.Write($"oor: {rawInEar} (stabiel: {_ears.StableInEarCount?.ToString() ?? "?"}, ruw {status.Message.ToHex()})");
        }

        if (earEvent == EarEvent.None)
            return;

        DiagnosticLog.Write($"oor: {earEvent}, nu {_ears.StableInEarCount} in het oor; " +
                            $"pauzeren {(_settings.AutoPauseOnEarRemoval ? "aan" : "uit")}, " +
                            $"audio {(_status.IsConnected ? "verbonden" : "niet verbonden")}");

        // Eerste oordop in (vanuit "geen in het oor"): automatisch verbinden.
        if (earEvent == EarEvent.Inserted && previous == 0 && _settings.AutoConnectOnEarInsertion)
            TryAutoConnect("oordop in");

        if (!_settings.AutoPauseOnEarRemoval || !_status.IsConnected)
        {
            _media.Forget();
            return;
        }

        if (earEvent == EarEvent.Removed && !_media.HasPausedByUs)
        {
            _inEarBeforePause = previous;
            await _media.PauseIfPlayingAsync();
        }
        else if (earEvent == EarEvent.Inserted && _media.HasPausedByUs
                 && _ears.StableInEarCount >= _inEarBeforePause)
        {
            // Pas hervatten als er weer evenveel in zit als vóór het pauzeren (bijv. allebei).
            if (_settings.AutoResumeOnEarInsertion)
                await _media.ResumeIfPausedByUsAsync();
            else
                _media.Forget();
        }
    }

    private void MarkUnknown(string reason)
    {
        DiagnosticLog.Write($"status onbekend: {reason}");
        _status.MarkUnknown(reason);
        _history?.MarkGap(DateTimeOffset.Now);
        _lidDetector.Reset();
        _ears.Reset();
        _lastRawInEar = null; // eerste signaal hierna weer in het log
        _preWoken = false; // volgende keer in beeld: weer vooraf wekken
        _autoConnectBlockedUntil = DateTimeOffset.MinValue; // AirPods uit beeld (case dicht): automatisch verbinden mag weer
        UpdateTray();
    }

    /// <summary>
    /// Menu onder de rechtermuisknop van het tray-icoon. Wordt elke keer opnieuw opgebouwd, zodat
    /// "Verbinden"/"Verbinding verbreken" en de sneltoets kloppen. Thema = dat van de pop-up.
    /// </summary>
    private void ShowTrayMenu()
    {
        _popup?.HidePopup();
        var popupHotkey = _settings.PopupHotkeyEnabled ? Hotkey.Parse(_settings.PopupHotkey)?.ToString() : null;
        var connectHotkey = _settings.ConnectHotkeyEnabled ? Hotkey.Parse(_settings.ConnectHotkey)?.ToString() : null;
        var connectText = !_status.IsPaired ? L.T("Koppelen…")
            : _status.IsConnected ? L.T("Verbinding verbreken met {0}", _status.DeviceName)
            : L.T("Verbinden met {0}", _status.DeviceName);

        // E7F6 = koptelefoon, E8A7 = venster openen, E71B = koppeling, E713 = tandwiel,
        // E702 = Bluetooth, E7E8 = aan/uit-knop
        TrayMenu.Show(
        [
            new TrayMenuEntry("", L.T("Status tonen"), popupHotkey, () => _popup?.ShowAtCorner()),
            new TrayMenuEntry("", L.T("PodPilot openen"), null, () => OpenMainWindow(MainPage.Overview)),
            null,
            new TrayMenuEntry("", connectText, connectHotkey, async () => await ToggleConnectionAsync(),
                _status.CanToggleConnection),
            // E7F5 = luidspreker; alleen zolang een telefoon via de pc afspeelt.
            .. (_streaming.Active is { } stream
                ? new TrayMenuEntry?[] { new("", L.T("Streamen van {0} stoppen", stream.Name), null, _streaming.Disconnect) }
                : []),
            null,
            new TrayMenuEntry("", L.T("Instellingen"), null, () => OpenMainWindow(MainPage.Settings)),
            new TrayMenuEntry("", L.T("Bluetooth-instellingen"), null, WindowsSettings.OpenBluetooth),
            null,
            new TrayMenuEntry("", L.T("Afsluiten"), null, Shutdown),
        ], ThemeManager.CreateColorsFor(_settings.PopupTheme));
    }

    private void UpdateTray() => _tray?.Update(_status.TooltipText, _status.LowestPercent);

    private void TogglePopup()
    {
        if (_popup is null)
            return;

        if (_popup.IsVisible && !_popup.IsActive)
            _popup.Activate(); // automatisch getoond: nu "vastpakken" in plaats van sluiten
        else if (_popup.IsVisible)
            _popup.HidePopup();
        else if (!_popup.WasJustHidden)
            _popup.ShowAtCorner();
    }

    /// <summary>Windows-thema veranderd (licht/donker): UI en tray-icoon bijwerken.</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General)
            return;

        // Ook de accentkleur kan zijn veranderd; die zit in elk thema.
        OnUiThread(ApplyThemes);
    }

    /// <summary>Scanner-events komen op een achtergrondthread; de UI mag alleen op de UI-thread worden aangepast.</summary>
    private void OnUiThread(Action action) => Dispatcher.BeginInvoke(action);

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _scanner?.Dispose();
        _connection?.Dispose();
        _popupHotkey?.Dispose();
        _connectHotkey?.Dispose();
        _outputHotkey?.Dispose();
        _streaming.Dispose();
        _tray?.Dispose();
        if (_popup is not null)
        {
            _popup.AllowClose = true;
            _popup.Close();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
