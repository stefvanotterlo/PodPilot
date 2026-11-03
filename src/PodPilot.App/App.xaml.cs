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
    private const string SearchingText = "Zoeken… open de case of doe de AirPods in";
    private const string UnknownText = "Geen signaal: status onbekend";

    private Mutex? _singleInstance;
    private AirPodsScanner? _scanner;
    private TrayIconService? _tray;
    private GlobalHotkey? _popupHotkey;
    private PopupWindow? _popup;
    private PairedAppleDevice? _device;
    private DeviceConnectionMonitor? _connection;
    private readonly StatusViewModel _status = new();
    private readonly LidOpenDetector _lidDetector = new();
    private readonly LowBatteryMonitor _lowBattery = new();
    private readonly EarDetectionMonitor _ears = new();
    private readonly MediaController _media = new();
    private BatteryHistory? _history;
    private int _inEarBeforePause;
    private string? _lastRawInEar;
    private DateTimeOffset _lastAutoConnect = DateTimeOffset.MinValue;    private bool _autoConnectSuppressed;
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
        _popup = new PopupWindow { DataContext = _status };
        ThemeManager.ApplyTo(_popup, _settings.PopupTheme);
        _popup.ConnectToggleRequested += async (_, _) => await ToggleConnectionAsync();
        _popup.SettingsRequested += (_, _) => OpenMainWindow(MainPage.Settings);
        _tray = new TrayIconService();
        _tray.OpenRequested += (_, _) => TogglePopup();
        _tray.MainWindowRequested += (_, _) => OpenMainWindow(MainPage.Overview);
        _tray.SettingsRequested += (_, _) => OpenMainWindow(MainPage.Settings);
        _tray.ExitRequested += (_, _) => Shutdown();
        UpdateTray();

        _popupHotkey = new GlobalHotkey();
        _popupHotkey.Pressed += (_, _) => TogglePopup();
        ApplyPopupHotkey(_settings.PopupHotkeyEnabled ? Hotkey.Parse(_settings.PopupHotkey) : null);

        _scanner = new AirPodsScanner();
        _scanner.StatusUpdated += (_, s) => OnUiThread(() => ApplyStatus(s));
        _scanner.StatusLost += (_, _) => OnUiThread(() => MarkUnknown(UnknownText));
        _scanner.ScannerStopped += (_, error) =>
        {
            // Success = wij hebben zelf gestopt (bij afsluiten).
            if (error != BluetoothError.Success)
                OnUiThread(() => MarkUnknown("Bluetooth staat uit of is niet beschikbaar"));
        };

        await ConfigureForPairedDeviceAsync(_scanner);
        _scanner.Start();

        // "PodPilot.exe --show" opent de pop-up meteen (handig bij testen), "--main" het hoofdvenster
        // en "--settings" het hoofdvenster op het tabblad Instellingen.
        if (e.Args.Contains("--show"))
            _popup.ShowAtCorner();
        if (e.Args.Contains("--main"))
            OpenMainWindow(MainPage.Overview);
        if (e.Args.Contains("--settings"))
            OpenMainWindow(MainPage.Settings);

        // "PodPilot.exe --test-notification" toont een voorbeeld van de melding bij lage batterij.
        if (e.Args.Contains("--test-notification"))
            NotificationService.ShowLowBattery(_status.DeviceName,
                [new LowBatteryItem("Links", _settings.LowBatteryThreshold), new LowBatteryItem("Rechts", 10)]);
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
            paired = await PairedDeviceService.GetPairedAppleDevicesAsync();
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
                _connection.ConnectionChanged += (_, _) => OnUiThread(async () => await RefreshConnectionAsync(delay: true));

            await RefreshConnectionAsync(delay: false);
        }
        catch (Exception)
        {
            _status.SetConnection(isPaired: true, isConnected: false);
        }
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
            devices = await PairedDeviceService.GetPairedAppleDevicesAsync();
        }
        catch (Exception)
        {
            // Het venster toont dan "geen gepairde AirPods gevonden".
        }

        var previousDevice = _settings.PreferredDeviceId;
        _mainWindow = new Views.MainWindow(_status, _settings, devices, ApplyPopupHotkey);
        ThemeManager.ApplyTo(_mainWindow, _settings.Theme);
        _mainWindow.ShowPage(page);
        _mainWindow.ConnectToggleRequested += async (_, _) => await ToggleConnectionAsync();
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

    /// <summary>Zet de sneltoets voor de pop-up (null = uit). Geeft false als een andere app hem al gebruikt.</summary>
    private bool ApplyPopupHotkey(Hotkey? hotkey)
    {
        var ok = _popupHotkey?.Set(hotkey) ?? false;
        if (hotkey is not null)
            DiagnosticLog.Write($"sneltoets pop-up {hotkey}: {(ok ? "actief" : "al in gebruik door een andere app")}");
        return ok;
    }

    /// <summary>Past de huidige instellingen toe (thema, meldingsdrempel). Bij het opstarten en na elke wijziging.</summary>
    private void ApplySettings()
    {
        ApplyThemes();
        _lowBattery.Threshold = _settings.NotifyLowBattery ? _settings.LowBatteryThreshold : 0;
    }

    /// <summary>
    /// Hoofdvenster en pop-up hebben elk een eigen thema. Het thema van het hoofdvenster is ook de
    /// standaard voor de rest (tooltips en dergelijke).
    /// </summary>
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

        if (delay)
            await Task.Delay(TimeSpan.FromSeconds(1.5));

        var active = await BluetoothAudioConnector.IsAudioActiveAsync(containerId, _device.Name);
        DiagnosticLog.Write($"verbinding: audio {(active ? "verbonden" : "niet verbonden")}");
        _status.SetConnection(isPaired: true, isConnected: active);
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
            _autoConnectSuppressed = false;
            if (!await ConnectAsync(_device, containerId, _connection, automatic: false))
                WindowsSettings.OpenBluetooth();
            return;
        }

        // Zelf verbroken: niet meteen weer automatisch verbinden, pas als de AirPods een keer
        // uit beeld zijn geweest (case dicht).
        _autoConnectSuppressed = true;
        _status.SetBusy(true, "Verbinding verbreken…");
        var result = await BluetoothAudioConnector.DisconnectAsync(containerId, _device.Name);
        var succeeded = result == AudioConnectResult.Requested
                        && await BluetoothAudioConnector.WaitForAudioAsync(containerId, _device.Name,
                               active: false, TimeSpan.FromSeconds(6));

        _status.SetBusy(false, succeeded ? null : "Verbreken lukt niet; gebruik Instellingen");
        await RefreshConnectionAsync(delay: false);

        if (!succeeded)
            WindowsSettings.OpenBluetooth();
    }

    /// <summary>Verbindt de audio en wacht tot het endpoint actief is. Geeft true als dat lukte.</summary>
    private async Task<bool> ConnectAsync(PairedAppleDevice device, Guid containerId,
        DeviceConnectionMonitor connection, bool automatic)
    {
        _status.SetBusy(true, automatic ? "Automatisch verbinden…" : "Verbinden…");

        // Eerst een Bluetooth-verbinding forceren; sommige drivers (Intel Smart Sound) negeren
        // het audioverzoek anders.
        await connection.WakeAsync();
        var result = await BluetoothAudioConnector.ConnectAsync(containerId, device.Name);
        var succeeded = result == AudioConnectResult.Requested
                        && await BluetoothAudioConnector.WaitForAudioAsync(containerId, device.Name,
                               active: true, TimeSpan.FromSeconds(10));

        _status.SetBusy(false, succeeded ? null
            : automatic ? "Automatisch verbinden lukt niet"
            : "Lukt niet; verbind via Instellingen");
        await RefreshConnectionAsync(delay: false);
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

        if (_autoConnectSuppressed)
        {
            DiagnosticLog.Write($"auto-verbinden ({reason}): overgeslagen, net zelf verbroken");
            return;
        }

        var now = DateTimeOffset.Now;
        if (now - _lastAutoConnect < TimeSpan.FromSeconds(30))
            return;
        _lastAutoConnect = now;

        DiagnosticLog.Write($"auto-verbinden ({reason}): start");
        var succeeded = await ConnectAsync(_device, containerId, connection, automatic: true);
        DiagnosticLog.Write($"auto-verbinden ({reason}): {(succeeded ? "gelukt" : "mislukt")}");
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
        _lastRawInEar = null; // eerste signaal hierna weer in het log        _autoConnectSuppressed = false; // AirPods uit beeld (case dicht): automatisch verbinden mag weer
        UpdateTray();
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
