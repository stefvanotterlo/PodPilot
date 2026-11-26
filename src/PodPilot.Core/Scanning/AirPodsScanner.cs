using PodPilot.Core.Models;
using PodPilot.Core.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace PodPilot.Core.Scanning;

/// <summary>Informatie over één ontvangen AirPods-advertentie, ook als die wordt genegeerd.</summary>
public sealed record AdvertisementSeenEventArgs(
    ulong Address,
    short Rssi,
    ProximityPairingMessage Message,
    bool Accepted,
    string? RejectReason);

/// <summary>
/// Luistert naar BLE-advertenties, haalt de AirPods-status eruit en kiest bij meerdere sets
/// in de buurt de juiste. Alle events komen binnen op een achtergrondthread.
/// </summary>
public sealed class AirPodsScanner : IDisposable
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly Timer _staleTimer;
    private readonly object _gate = new();
    private AirPodsStatus? _current;
    private bool _disposed;

    public AirPodsScanner()
    {
        // Actieve scanmodus: Windows vraagt ook om scan-responses, zodat we sneller data krijgen.
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        _watcher.Received += OnReceived;
        _watcher.Stopped += OnStopped;

        _staleTimer = new Timer(_ => CheckStale(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Advertenties met een zwakker signaal dan dit (in dBm) worden genegeerd.</summary>
    public short MinimumRssi { get; set; } = -70;

    /// <summary>Na zoveel tijd zonder data wordt de status "onbekend" (<see cref="StatusLost"/>).</summary>
    public TimeSpan StaleAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Als de huidige set zo lang niets meer heeft uitgezonden, mag een oordop die erbij past
    /// (zelfde model, kleur en batterij) het overnemen, ook met een zwakker signaal.
    /// </summary>
    public TimeSpan SwitchAfter { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Alleen advertenties met dit model-ID accepteren, bijvoorbeeld het product-ID van de
    /// gepairde AirPods. Met null wordt elk model geaccepteerd.
    /// </summary>
    public ushort? PreferredModelId { get; set; }

    /// <summary>De laatst bekende status, of null als die onbekend is.</summary>
    public AirPodsStatus? Current
    {
        get { lock (_gate) return _current; }
    }

    public bool IsRunning => _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;

    /// <summary>Elke AirPods-advertentie, geaccepteerd of niet (voor debuggen).</summary>
    public event EventHandler<AdvertisementSeenEventArgs>? AdvertisementSeen;

    /// <summary>Er is een nieuwe status van de gekozen AirPods.</summary>
    public event EventHandler<AirPodsStatus>? StatusUpdated;

    /// <summary>
    /// Een andere set AirPods heeft het overgenomen (komt vóór <see cref="StatusUpdated"/>). Wie
    /// in-oor-wijzigingen bijhoudt, moet dan opnieuw beginnen: het verschil met de vorige set is
    /// geen in- of uitdoen.
    /// </summary>
    public event EventHandler<AirPodsStatus>? SetChanged;

    /// <summary>Er is al <see cref="StaleAfter"/> geen data meer binnengekomen.</summary>
    public event EventHandler? StatusLost;

    /// <summary>
    /// De watcher is gestopt. De BluetoothError geeft de reden, bijvoorbeeld
    /// RadioNotAvailable als Bluetooth uit staat.
    /// </summary>
    public event EventHandler<BluetoothError>? ScannerStopped;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _watcher.Start();
        _staleTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    public void Stop()
    {
        _staleTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            _watcher.Stop();
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender,
                            BluetoothLEAdvertisementReceivedEventArgs args)
    {
        foreach (var data in args.Advertisement.ManufacturerData)
        {
            if (data.CompanyId != AppleContinuity.AppleCompanyId)
                continue;

            if (ProximityPairingMessage.TryParse(ToArray(data.Data), out var message))
                HandleMessage(args.BluetoothAddress, args.RawSignalStrengthInDBm, message);
        }
    }

    private void HandleMessage(ulong address, short rssi, ProximityPairingMessage message)
    {
        var now = DateTimeOffset.Now;
        string? rejectReason = null;
        AirPodsStatus? updated = null;
        var newSet = false;

        if (rssi < MinimumRssi)
        {
            rejectReason = $"signaal te zwak ({rssi} < {MinimumRssi} dBm)";
        }
        else if (PreferredModelId is ushort preferred && message.ModelId != preferred)
        {
            rejectReason = $"ander model (0x{message.ModelId:X4}) dan de gekozen AirPods (0x{preferred:X4})";
        }
        else
        {
            lock (_gate)
            {
                var decision = Decide(_current, address, rssi, message, now);
                if (decision == TakeOver.Reject)
                {
                    rejectReason = "andere AirPods in de buurt (andere batterijwaarden of kleur)";
                }
                else
                {
                    newSet = decision == TakeOver.NewSet && _current is not null;
                    _current = updated = AirPodsStatus.FromMessage(message, address, rssi, now);
                }
            }
        }

        AdvertisementSeen?.Invoke(this,
            new AdvertisementSeenEventArgs(address, rssi, message, rejectReason is null, rejectReason));

        if (newSet && updated is not null)
            SetChanged?.Invoke(this, updated);
        if (updated is not null)
            StatusUpdated?.Invoke(this, updated);
    }

    private enum TakeOver
    {
        Reject,

        /// <summary>Dezelfde set (dezelfde of de andere oordop): gewoon bijwerken.</summary>
        SameSet,

        /// <summary>Een andere set neemt het over: geen in-oor-gebeurtenis afleiden uit de wissel.</summary>
        NewSet,
    }

    /// <summary>
    /// Bepaalt of een nieuwe advertentie de huidige status mag vervangen. Zo blijven we bij
    /// onze eigen AirPods, ook als iemand anders in de buurt hetzelfde model heeft.
    /// </summary>
    /// <remarks>
    /// Elke oordop zendt met een eigen, wisselend BLE-adres, en het herkenbare deel van het bericht
    /// is niet aan onze set gekoppeld. Daarom herkennen we de eigen set aan model, kleur én de
    /// batterijwaarden (die veranderen maar in stappen van 10%). Een set met andere waarden neemt
    /// het alleen over als die van ons al <see cref="StaleAfter"/> stil is (dan zijn de status en de
    /// in-oor-detectie toch al gewist), of als hij heel veel dichterbij is (bijvoorbeeld als we bij
    /// het opstarten eerst de set van een ander zagen).
    /// </remarks>
    private TakeOver Decide(AirPodsStatus? current, ulong address, short rssi,
                            ProximityPairingMessage message, DateTimeOffset now)
    {
        if (current is null)
            return TakeOver.NewSet;

        // Dezelfde zender (dezelfde oordop): altijd bijwerken.
        if (current.AdvertisementAddress == address)
            return TakeOver.SameSet;

        var looksLikeOurs = current.Message.ModelId == message.ModelId
                            && current.Message.Color == message.Color
                            && BatteriesMatch(current.Message, message);

        // De andere oordop van dezelfde set (of dezelfde oordop met een nieuw adres).
        if (looksLikeOurs && (rssi >= current.Rssi - 15 || now - current.ReceivedAt > SwitchAfter))
            return TakeOver.SameSet;

        // Een andere set: alleen als die véél dichterbij is (wij zitten dan waarschijnlijk op de
        // verkeerde set), en dan als nieuwe set.
        if (!looksLikeOurs && rssi > current.Rssi + TakeOverMargin)
            return TakeOver.NewSet;

        return TakeOver.Reject;
    }

    /// <summary>Zoveel dB sterker moet een andere set zijn om het over te nemen.</summary>
    public int TakeOverMargin { get; set; } = 20;

    /// <summary>
    /// True als de batterijwaarden bij dezelfde set kunnen horen: per oordop en de case hoogstens
    /// 10% verschil (onbekend telt als passend).
    /// </summary>
    private static bool BatteriesMatch(ProximityPairingMessage a, ProximityPairingMessage b)
    {
        static bool Close(int? x, int? y) => x is null || y is null || Math.Abs(x.Value - y.Value) <= 10;
        return Close(a.LeftBattery, b.LeftBattery) && Close(a.RightBattery, b.RightBattery)
               && Close(a.CaseBattery, b.CaseBattery);
    }

    private void CheckStale()
    {
        var lost = false;
        lock (_gate)
        {
            if (_current is not null && DateTimeOffset.Now - _current.ReceivedAt > StaleAfter)
            {
                _current = null;
                lost = true;
            }
        }

        if (lost)
            StatusLost?.Invoke(this, EventArgs.Empty);
    }

    private void OnStopped(BluetoothLEAdvertisementWatcher sender,
                           BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        ScannerStopped?.Invoke(this, args.Error);
    }

    /// <summary>Kopieert een WinRT-IBuffer naar een byte-array.</summary>
    private static byte[] ToArray(IBuffer buffer)
    {
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Stop();
        _watcher.Received -= OnReceived;
        _watcher.Stopped -= OnStopped;
        _staleTimer.Dispose();
    }
}
