// PodPilot stap 1: een consoletool die laat zien of je AirPods worden gevonden.
//
// Gebruik:  dotnet run --project src/PodPilot.ConsoleScanner -- [--rssi -80] [--any-model] [--verbose]
//           dotnet run --project src/PodPilot.ConsoleScanner -- --connect | --disconnect

using System.Text;
using PodPilot.Core.Devices;
using PodPilot.Core.Models;
using PodPilot.Core.Scanning;

Console.OutputEncoding = Encoding.UTF8;

// --handsfree [off|on]: de "Hands-Free"-endpoints (belprofiel) van de AirPods tonen, of uit/aan zetten.
var handsFreeIndex = Array.IndexOf(args, "--handsfree");
if (handsFreeIndex >= 0)
{
    var airPods = (await AudioDevices.GetAsync()).FirstOrDefault();
    if (airPods is null)
    {
        Console.WriteLine("Geen Bluetooth-audioapparaat gevonden.");
        return;
    }

    var action = handsFreeIndex + 1 < args.Length ? args[handsFreeIndex + 1] : "";
    foreach (var e in AudioOutputs.GetHandsFreeEndpoints(airPods.ContainerId))
    {
        if (action is "off" or "on")
            Console.WriteLine($"{(action == "off" ? "Uitzetten" : "Aanzetten")}: {e.Name} → HRESULT 0x{AudioOutputs.SetEnabled(e.Id, action == "on"):X8}");
    }
    foreach (var e in AudioOutputs.GetHandsFreeEndpoints(airPods.ContainerId))
        Console.WriteLine($"  {(e.IsCapture ? "microfoon" : "uitgang  ")} {e.Name}  status 0x{e.State:X}{(e.IsDisabled ? " (uitgeschakeld)" : "")}");
    return;
}

// --devices: alle gepairde Bluetooth-audioapparaten, verbonden of niet, met batterij (als bekend).
if (args.Contains("--devices"))
{
    foreach (var d in await AudioDevices.GetAsync())
        Console.WriteLine($"• {d.Name}  {(d.IsConnected ? "verbonden" : "niet verbonden")}  " +
                          $"batterij {(d.BatteryPercent is int b ? $"{b}%" : "onbekend")}  container {d.ContainerId}");
    return;
}

// --outputs: toont de geluidsuitgangen en welke de standaard is; "--outputs --set <nr>" maakt
// uitgang <nr> de standaard (test voor het wisselen van de geluidsuitgang).
if (args.Contains("--outputs"))
{
    var outputs = AudioOutputs.GetActive();
    var setIndex = Array.IndexOf(args, "--set");
    if (setIndex >= 0 && setIndex + 1 < args.Length && int.TryParse(args[setIndex + 1], out var nr)
        && nr >= 1 && nr <= outputs.Count)
        Console.WriteLine($"Standaard maken: {outputs[nr - 1].Name} → {(AudioOutputs.SetDefault(outputs[nr - 1].Id) ? "gelukt" : "mislukt")}");

    var defaultId = AudioOutputs.GetDefaultId();
    for (var i = 0; i < outputs.Count; i++)
        Console.WriteLine($"{i + 1}. {outputs[i].Name}{(outputs[i].Id == defaultId ? "   ← standaard" : "")}");
    return;
}

// --- Argumenten -------------------------------------------------------------------------
short minRssi = -70;
var anyModel = args.Contains("--any-model");
var verbose = args.Contains("--verbose");
var rssiIndex = Array.IndexOf(args, "--rssi");
if (rssiIndex >= 0 && rssiIndex + 1 < args.Length && short.TryParse(args[rssiIndex + 1], out var parsed))
    minRssi = parsed;

Console.WriteLine("PodPilot – BLE-scanner (stap 1)");
Console.WriteLine($"Signaaldrempel: {minRssi} dBm   Modelfilter: {(anyModel ? "uit" : "aan")}   Verbose: {(verbose ? "aan" : "uit")}");
Console.WriteLine();

// --- Gepairde apparaten -----------------------------------------------------------------
Console.WriteLine("Gepairde Apple-apparaten op deze pc:");
IReadOnlyList<PairedAppleDevice> paired = [];
try
{
    paired = await PairedDeviceService.GetPairedAppleDevicesAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"  (kon gepairde apparaten niet opvragen: {ex.Message})");
}

if (paired.Count == 0)
    Console.WriteLine("  geen gevonden. De scanner toont dan alle AirPods in de buurt.");

foreach (var device in paired)
{
    var model = device.ProductId is ushort pid
        ? $"{AirPodsModels.Lookup(pid).Name}, PID 0x{pid:X4}"
        : "model onbekend";
    var state = device.IsConnected ? "verbonden" : "niet verbonden";
    Console.WriteLine($"  • {device.Name}  [{model}]  {state}  adres {FormatAddress(device.BluetoothAddress)}");
}
Console.WriteLine();

// --- Verbinden/verbreken testen (--connect / --disconnect) ------------------------------
var connect = args.Contains("--connect");
if (connect || args.Contains("--disconnect"))
{
    var target = paired.FirstOrDefault(d => d.ContainerId.HasValue);
    if (target is null)
    {
        Console.WriteLine("Geen gepaird apparaat met container-ID gevonden.");
        return;
    }

    using var monitor = await DeviceConnectionMonitor.CreateAsync(target.DeviceId);
    if (connect && args.Contains("--wake") && monitor is not null)
    {
        Console.WriteLine("SDP-query (wekken)...");
        await monitor.WakeAsync();
        Console.WriteLine($"Na wekken: {(monitor.IsConnected ? "ACL verbonden" : "niet verbonden")}");
    }
    Console.WriteLine($"{(connect ? "Verbinden met" : "Verbreken van")} {target.Name}...");
    var result = connect
        ? await BluetoothAudioConnector.ConnectAsync(target.ContainerId!.Value, target.Name, Console.WriteLine)
        : await BluetoothAudioConnector.DisconnectAsync(target.ContainerId!.Value, target.Name, Console.WriteLine);
    Console.WriteLine($"Verzoek: {result}");

    if (result == AudioConnectResult.Requested)
    {
        // Kijk naar het audio-endpoint: een Bluetooth-verbinding alleen betekent nog geen geluid.
        var reached = await BluetoothAudioConnector.WaitForAudioAsync(
            target.ContainerId!.Value, target.Name, active: connect, TimeSpan.FromSeconds(15));
        Console.WriteLine(reached
            ? $"Gelukt: audio {(connect ? "verbonden" : "verbroken")}."
            : "Audio-status niet veranderd binnen 15 s.");
    }
    return;
}

// --- Scanner ----------------------------------------------------------------------------
using var scanner = new AirPodsScanner { MinimumRssi = minRssi };

// Bij precies één gepaird model met bekend PID: alleen dat model accepteren.
var pairedModelIds = paired.Where(d => d.ProductId.HasValue).Select(d => d.ProductId!.Value).Distinct().ToList();
if (!anyModel && pairedModelIds.Count == 1)
{
    scanner.PreferredModelId = pairedModelIds[0];
    Console.WriteLine($"Alleen advertenties van model 0x{pairedModelIds[0]:X4} ({AirPodsModels.Lookup(pairedModelIds[0]).Name}) worden gebruikt.");
}

// --ear-test: begeleide test die per situatie (beide in, links uit, rechts uit, ...) laat zien welke
// statusbytes de AirPods sturen. Zo is te controleren of de in-oor-bits goed worden gelezen.
if (args.Contains("--ear-test"))
{
    await RunEarTestAsync(scanner);
    return;
}

scanner.AdvertisementSeen += (_, e) =>
{
    if (verbose && !e.Accepted)
        Console.WriteLine($"{Time()}   genegeerd ({e.RejectReason}) · {FormatAddress(e.Address)} · {e.Message.ToHex()}");
};

var lidDetector = new LidOpenDetector();
var earMonitor = new EarDetectionMonitor();

// --watch-media: log in-oor-wijzigingen en de afspeelstatus van Windows-media (test voor stap 7).
if (args.Contains("--watch-media"))
{
    var manager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    string? lastPlayback = null;
    _ = Task.Run(async () =>
    {
        while (true)
        {
            var session = manager.GetCurrentSession();
            var playback = session is null
                ? "geen mediasessie"
                : $"{session.SourceAppUserModelId}: {session.GetPlaybackInfo().PlaybackStatus}";
            if (playback != lastPlayback)
                Console.WriteLine($"{Time()} [media] {lastPlayback = playback}");
            await Task.Delay(300);
        }
    });

    scanner.StatusUpdated += (_, s) =>
    {
        var earEvent = earMonitor.Process(s);
        if (earEvent != EarEvent.None)
            Console.WriteLine($"{Time()} [oor] {earEvent} → {earMonitor.StableInEarCount} in het oor");
    };
}

scanner.StatusUpdated += (_, s) =>
{
    if (lidDetector.Process(s))
        Console.WriteLine($"{Time()} *** Case geopend: hier verschijnt in de app de pop-up ***");

    var line = s.Model.IsHeadphones
        ? $"{Time()} {s.Model.Name}  RSSI {s.Rssi,4}  Batterij {Component(s.Left)}"
        : $"{Time()} {s.Model.Name}  RSSI {s.Rssi,4}  L {Component(s.Left)}  R {Component(s.Right)}  " +
          $"Case {Component(s.Case)}  Deksel {(s.IsLidOpen ? "open" : "dicht")}  " +
          $"(uitzender: {(s.Message.IsLeftBroadcasting ? "L" : "R")})";

    Console.WriteLine(line);
    if (verbose)
        Console.WriteLine($"           ruw: {s.Message.ToHex()}  adres {FormatAddress(s.AdvertisementAddress)}");
};

scanner.StatusLost += (_, _) =>
{
    lidDetector.Reset();
    Console.WriteLine($"{Time()} Geen data meer ontvangen: de status is ONBEKEND.");
};

scanner.ScannerStopped += (_, error) =>
    Console.WriteLine($"{Time()} Scanner gestopt: {error}. Staat Bluetooth aan?");

scanner.Start();
Console.WriteLine("Aan het scannen... Open de case naast de pc of doe de AirPods in.");
Console.WriteLine("Druk op Enter om te stoppen.");
Console.WriteLine();
if (Console.IsInputRedirected)
    await Task.Delay(Timeout.Infinite); // geen toetsenbord (uitvoer naar een bestand): stoppen met Ctrl+C of het proces beëindigen
Console.ReadLine();
scanner.Stop();

// --- Hulpfuncties -----------------------------------------------------------------------
static string Time() => DateTime.Now.ToString("HH:mm:ss");

static async Task RunEarTestAsync(AirPodsScanner scanner)
{
    // Het resultaat komt ook in een bestand, zodat het later terug te lezen is.
    var reportPath = Path.Combine(Path.GetTempPath(), "podpilot-eartest.txt");
    File.WriteAllText(reportPath, "");
    void Report(string line)
    {
        Console.WriteLine(line);
        File.AppendAllText(reportPath, line + Environment.NewLine);
    }

    var seen = new List<string>();
    scanner.StatusUpdated += (_, s) =>
    {
        var key = $"status 0x{s.Message.StatusByte:X2}, uitzender {(s.Message.IsLeftBroadcasting ? "L" : "R")} " +
                  $"→ gelezen als L {(s.Left.IsInEar ? "in" : "uit")}, R {(s.Right.IsInEar ? "in" : "uit")}";
        lock (seen)
        {
            if (!seen.Contains(key))
                seen.Add(key);
        }
    };
    scanner.Start();

    string[] steps =
    [
        "Doe BEIDE oordopjes in je oren.",
        "Haal alleen de LINKER oordop uit je oor. De rechter blijft in.",
        "Doe de linker weer in. Haal daarna alleen de RECHTER uit. De linker blijft in.",
        "Doe de rechter weer in, zodat er weer twee in zitten.",
        // Eén in het oor en de andere in de open case: hier wisselden de vlaggen per advertentie.
        "Leg de RECHTER in de case (deksel OPEN laten). Alleen de linker zit in je oor.",
        "Doe de rechter weer in je oor en leg de LINKER in de open case. Alleen de rechter zit in je oor.",
    ];

    foreach (var step in steps)
    {
        Console.WriteLine();
        Console.WriteLine(step);
        Console.Write("Druk op Enter als je dat gedaan hebt...");
        Console.ReadLine();
        lock (seen)
            seen.Clear();
        Console.WriteLine("Even meten, houd ze zo (10 seconden)...");
        await Task.Delay(TimeSpan.FromSeconds(10));

        Report($"[{step}]");
        lock (seen)
        {
            if (seen.Count == 0)
                Report("  (geen advertenties ontvangen)");
            foreach (var line in seen)
                Report("  " + line);
        }
    }

    Console.WriteLine();
    Console.WriteLine("Klaar. Je kunt dit venster sluiten.");
}

static string Component(ComponentStatus c)
{
    var flags = new List<string>();
    if (c.IsCharging) flags.Add("laadt");
    if (c.IsInEar) flags.Add("in oor");
    var text = c.PercentText.PadLeft(4);
    return flags.Count > 0 ? $"{text} ({string.Join(", ", flags)})" : text;
}

static string FormatAddress(ulong address) =>
    string.Join(":", BitConverter.GetBytes(address).Take(6).Reverse().Select(b => b.ToString("X2")));
