using PodPilot.Core.Models;

namespace PodPilot.Core.Scanning;

public enum EarEvent
{
    None,

    /// <summary>Er zit minder dan eerst in het oor (een oordop uitgedaan, of beide).</summary>
    Removed,

    /// <summary>Er zit meer dan eerst in het oor.</summary>
    Inserted,
}

/// <summary>
/// Herkent het in- en uitdoen van oordopjes, voor automatisch pauzeren en hervatten.
/// </summary>
/// <remarks>
/// Standaard telt een nieuw aantal "in het oor" meteen. Als de AirPods met de pc verbonden zijn,
/// komt er maar elke 1 à 5 seconden een advertentie; wachten op een tweede maakt pauzeren
/// merkbaar traag. Haperen de in-oor-vlaggen toch, zet dan <see cref="Debounce"/> hoger: een nieuw
/// aantal telt dan pas als het zo lang hetzelfde blijft.
/// </remarks>
public sealed class EarDetectionMonitor
{
    private int _pendingCount = -1;
    private DateTimeOffset _pendingSince;

    /// <summary>Zo lang moet een nieuw aantal "in het oor" gelijk blijven voordat het telt.</summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.Zero;

    /// <summary>Het laatste stabiele aantal oordopjes in het oor (0, 1 of 2), of null als dat nog onbekend is.</summary>
    public int? StableInEarCount { get; private set; }

    /// <summary>Verwerkt een status en geeft aan of er net iets is uitgedaan of ingedaan.</summary>
    public EarEvent Process(AirPodsStatus status)
    {
        var count = (status.Left.IsInEar ? 1 : 0) + (status.Right.IsInEar ? 1 : 0);

        if (count != _pendingCount)
        {
            _pendingCount = count;
            _pendingSince = status.ReceivedAt;
        }

        if (status.ReceivedAt - _pendingSince < Debounce)
            return EarEvent.None;

        // De eerste stabiele waarde is het beginpunt, nog geen gebeurtenis.
        if (StableInEarCount is not int previous)
        {
            StableInEarCount = count;
            return EarEvent.None;
        }

        if (count == previous)
            return EarEvent.None;

        StableInEarCount = count;
        return count < previous ? EarEvent.Removed : EarEvent.Inserted;
    }

    /// <summary>Aanroepen als de status onbekend wordt; het volgende aantal is dan weer het beginpunt.</summary>
    public void Reset()
    {
        StableInEarCount = null;
        _pendingCount = -1;
    }
}
