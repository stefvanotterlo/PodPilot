using System.Globalization;
using PodPilot.Core.Models;

namespace PodPilot.Core.Scanning;

public enum Bud
{
    Left,
    Right,
}

/// <summary>Eén regel uit de geschiedenis. <see cref="Bud"/> null = onderbreking (geen signaal).</summary>
public readonly record struct BatterySample(DateTimeOffset Time, Bud? Bud, int Percent, bool InEar, bool Charging)
{
    public bool IsGap => Bud is null;

    public static BatterySample Gap(DateTimeOffset time) => new(time, null, 0, false, false);
}

/// <summary>
/// Houdt het batterijverloop van de oordopjes bij en schat daarmee de resterende luistertijd.
/// </summary>
/// <remarks>
/// AirPods geven de batterij in stappen van 10%. We meten daarom hoe lang één stap duurt: de tijd
/// tussen twee opeenvolgende dalingen van 10%, terwijl de oordop in het oor zit en niet laadt, en
/// zonder onderbreking van het signaal ertussen. De schatting is de mediaan van de laatste metingen,
/// zodat één rare meting (bijv. een pauze in het oor) weinig uitmaakt.
/// <para>
/// Opslag: één regel per wijziging in <c>battery-history.csv</c> ("tijd;L;80;1;0", of "tijd;-"
/// voor een onderbreking). Regels ouder dan <see cref="Retention"/> worden bij het laden opgeruimd.
/// </para>
/// </remarks>
public sealed class BatteryHistory
{
    /// <summary>Zo lang blijven metingen bewaard.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>Zo veel stappen van 10% moeten er minstens gemeten zijn voor een schatting.</summary>
    public const int MinimumIntervals = 2;

    // Een stap van 10% korter dan 3 minuten of langer dan 90 minuten is geen gewoon luisteren
    // (haperend signaal, of de oordop lag stil in het oor).
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MaxInterval = TimeSpan.FromMinutes(90);
    private const int IntervalsForMedian = 20;

    private readonly string? _filePath;
    private readonly Dictionary<Bud, BatterySample> _last = [];
    private readonly Dictionary<Bud, DrainTracker> _trackers = new() { [Bud.Left] = new(), [Bud.Right] = new() };
    private readonly Dictionary<Bud, List<TimeSpan>> _intervals = new() { [Bud.Left] = [], [Bud.Right] = [] };
    private bool _lastWasGap = true;

    /// <param name="filePath">CSV-bestand; null = alleen in het geheugen (voor tests).</param>
    /// <param name="now">Huidige tijd, voor het opruimen van oude regels.</param>
    public BatteryHistory(string? filePath, DateTimeOffset now)
    {
        _filePath = filePath;
        if (filePath is null || !File.Exists(filePath))
            return;

        var kept = new List<string>();
        var removedAny = false;
        foreach (var line in File.ReadAllLines(filePath))
        {
            if (!TryParse(line, out var sample) || now - sample.Time > Retention)
            {
                removedAny = true;
                continue;
            }

            kept.Add(line);
            Replay(sample);
        }

        // Na het laden begint een nieuwe sessie: wat er sinds het afsluiten gebeurde, weten we niet.
        Replay(BatterySample.Gap(now));

        if (removedAny)
            File.WriteAllLines(filePath, kept);
    }

    /// <summary>Verwerkt een nieuwe status. Alleen wijzigingen komen in de geschiedenis.</summary>
    public void Record(AirPodsStatus status)
    {
        Record(Bud.Left, status.Left, status.ReceivedAt);
        if (!status.Model.IsHeadphones)
            Record(Bud.Right, status.Right, status.ReceivedAt);
    }

    /// <summary>Het signaal is weg (case dicht, buiten bereik): metingen lopen niet door over dit gat.</summary>
    public void MarkGap(DateTimeOffset at)
    {
        if (_lastWasGap)
            return;

        Add(BatterySample.Gap(at));
    }

    /// <summary>Gemeten duur van één stap van 10% (mediaan), of null als er te weinig metingen zijn.</summary>
    public TimeSpan? DrainPerStep(Bud bud)
    {
        var intervals = _intervals[bud];
        if (intervals.Count < MinimumIntervals)
            return null;

        var recent = intervals.TakeLast(IntervalsForMedian).Order().ToList();
        return recent.Count % 2 == 1
            ? recent[recent.Count / 2]
            : (recent[recent.Count / 2 - 1] + recent[recent.Count / 2]) / 2;
    }

    /// <summary>
    /// Geschatte resterende luistertijd, of null als er geen goede schatting is: de oordop zit niet
    /// in het oor, laadt, of er zijn nog te weinig metingen.
    /// </summary>
    public TimeSpan? EstimateRemaining(Bud bud, ComponentStatus current, DateTimeOffset now)
    {
        if (current.BatteryPercent is not int percent || percent <= 0 || current.IsCharging || !current.IsInEar)
            return null;
        if (DrainPerStep(bud) is not TimeSpan step)
            return null;

        // Bij 80% resten er ongeveer 8 stappen. Weten we sinds wanneer hij op 80% staat, dan gaat
        // de tijd die al verstreken is daar af (maar niet meer dan die ene stap).
        var remaining = step * (percent / 10.0);
        if (_trackers[bud].LevelSince is DateTimeOffset since)
            remaining -= TimeSpan.FromTicks(Math.Min((now - since).Ticks, step.Ticks));

        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void Record(Bud bud, ComponentStatus component, DateTimeOffset time)
    {
        if (component.BatteryPercent is not int percent)
            return;

        var sample = new BatterySample(time, bud, percent, component.IsInEar, component.IsCharging);
        if (_last.TryGetValue(bud, out var last) && !_lastWasGap
            && last.Percent == sample.Percent && last.InEar == sample.InEar && last.Charging == sample.Charging)
            return;

        Add(sample);
    }

    private void Add(BatterySample sample)
    {
        Replay(sample);
        if (_filePath is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.AppendAllText(_filePath, Format(sample) + Environment.NewLine);
        }
        catch (IOException)
        {
            // Een schrijffout mag de app niet laten crashen; de schatting werkt dan uit het geheugen.
        }
    }

    /// <summary>Verwerkt een regel in de toestand (bij het laden en bij elke nieuwe regel).</summary>
    private void Replay(BatterySample sample)
    {
        if (sample.IsGap)
        {
            foreach (var tracker in _trackers.Values)
                tracker.Gap();
            _lastWasGap = true;
            return;
        }

        var bud = sample.Bud!.Value;
        _last[bud] = sample;
        _lastWasGap = false;
        if (_trackers[bud].Process(sample) is TimeSpan interval)
            _intervals[bud].Add(interval);
    }

    /// <summary>
    /// Volgt per oordop de dalingen van 10%. Geeft de duur van een stap terug zodra er twee
    /// opeenvolgende, schone dalingen zijn gezien.
    /// </summary>
    private sealed class DrainTracker
    {
        private BatterySample? _previous;

        /// <summary>Sinds wanneer de oordop op het huidige niveau staat (alleen als die daling is gezien).</summary>
        public DateTimeOffset? LevelSince { get; private set; }

        public TimeSpan? Process(BatterySample sample)
        {
            var previous = _previous;
            _previous = sample;

            // Laden of uit het oor: dit telt niet als luisteren.
            if (sample.Charging || !sample.InEar)
            {
                LevelSince = null;
                return null;
            }

            if (previous is not { } p || p.Percent == sample.Percent)
                return null;

            // Alleen een daling van precies één stap, vanuit luisteren, is een schone meting.
            if (p.Charging || !p.InEar || p.Percent - sample.Percent != 10)
            {
                LevelSince = null;
                return null;
            }

            var since = LevelSince;
            LevelSince = sample.Time;
            if (since is null)
                return null;

            var interval = sample.Time - since.Value;
            return interval >= MinInterval && interval <= MaxInterval ? interval : null;
        }

        public void Gap()
        {
            _previous = null;
            LevelSince = null;
        }
    }

    // --- CSV ------------------------------------------------------------------------------

    private static string Format(BatterySample s) => s.IsGap
        ? $"{s.Time.ToString("o", CultureInfo.InvariantCulture)};-"
        : string.Join(';', s.Time.ToString("o", CultureInfo.InvariantCulture), s.Bud == Bud.Left ? "L" : "R",
            s.Percent.ToString(CultureInfo.InvariantCulture), s.InEar ? "1" : "0", s.Charging ? "1" : "0");

    private static bool TryParse(string line, out BatterySample sample)
    {
        sample = default;
        var parts = line.Split(';');
        if (parts.Length < 2
            || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
            return false;

        if (parts[1] == "-")
        {
            sample = BatterySample.Gap(time);
            return true;
        }

        if (parts.Length < 5 || parts[1] is not ("L" or "R")
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var percent))
            return false;

        sample = new BatterySample(time, parts[1] == "L" ? Bud.Left : Bud.Right, percent, parts[3] == "1", parts[4] == "1");
        return true;
    }
}
