using PodPilot.Core.Models;

namespace PodPilot.Core.Scanning;

/// <summary>Welke oordop(jes) al een tijd buiten oor en case liggen.</summary>
[Flags]
public enum LooseBuds
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right,
}

/// <summary>
/// Herkent een oordop die al een tijd niet in het oor zit en ook niet in de case ligt (bijv. op je
/// bureau, of niet goed in de case geklikt). Zo'n oordop laadt niet op en blijft verbonden.
/// </summary>
/// <remarks>
/// De AirPods melden "in de case" niet per oordop. Wel geldt: zolang er een oordop in de case
/// ligt, is de case-batterij bekend. Is die onbekend terwijl de oordopjes nog uitzenden, dan ligt
/// geen enkele oordop in de case. Een oordop die dan niet in het oor zit en niet laadt, ligt los.
/// Eén melding per keer; pas als hij weer in het oor of in de case is geweest, kan er een nieuwe komen.
/// </remarks>
public sealed class BudOutOfCaseMonitor
{
    private DateTimeOffset? _leftSince;
    private DateTimeOffset? _rightSince;
    private bool _leftNotified;
    private bool _rightNotified;

    /// <summary>Zo lang moet een oordop los liggen voor de melding.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMinutes(10);

    public bool Enabled { get; set; } = true;

    /// <summary>Geeft de oordopjes die net de wachttijd hebben bereikt (anders None).</summary>
    public LooseBuds Process(AirPodsStatus status)
    {
        if (!Enabled || status.Model.IsHeadphones)
            return LooseBuds.None;

        var noneInCase = !status.Case.IsKnown;
        var result = LooseBuds.None;
        if (Check(status.Left, noneInCase, status.ReceivedAt, ref _leftSince, ref _leftNotified))
            result |= LooseBuds.Left;
        if (Check(status.Right, noneInCase, status.ReceivedAt, ref _rightSince, ref _rightNotified))
            result |= LooseBuds.Right;
        return result;
    }

    /// <summary>Aanroepen als de AirPods uit beeld zijn (case dicht): dan liggen ze in de case.</summary>
    public void Reset()
    {
        _leftSince = _rightSince = null;
        _leftNotified = _rightNotified = false;
    }

    private bool Check(ComponentStatus bud, bool noneInCase, DateTimeOffset now,
                       ref DateTimeOffset? since, ref bool notified)
    {
        var loose = noneInCase && bud.IsKnown && !bud.IsInEar && !bud.IsCharging;
        if (!loose)
        {
            since = null;
            notified = false;
            return false;
        }

        since ??= now;
        if (notified || now - since < Delay)
            return false;

        notified = true;
        return true;
    }
}
