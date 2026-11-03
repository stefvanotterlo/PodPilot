using PodPilot.Core.Models;

namespace PodPilot.Core.Scanning;

/// <summary>
/// Herkent het moment waarop de case wordt geopend, voor de automatische pop-up.
/// </summary>
/// <remarks>
/// Een gesloten case zendt (bijna) niets uit. Daarom geldt als "nieuw geopend":
/// <list type="bullet">
/// <item>de deksel gaat van dicht naar open (als we het dichte bericht nog net hebben opgevangen), of</item>
/// <item>er komt een bericht met open deksel binnen na een stilte van <see cref="GapMeansClosed"/>.
///       De case was dan dicht en de AirPods zonden niets uit.</item>
/// </list>
/// Een cooldown voorkomt dat de pop-up steeds opnieuw verschijnt als het signaal hapert.
/// </remarks>
public sealed class LidOpenDetector
{
    private DateTimeOffset _lastSeen = DateTimeOffset.MinValue;
    private DateTimeOffset _lastFired = DateTimeOffset.MinValue;
    private bool _wasOpen;

    /// <summary>Zo lang zonder berichten betekent: de case was dicht.</summary>
    public TimeSpan GapMeansClosed { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>Minimale tijd tussen twee meldingen "case geopend".</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Verwerkt een status. Geeft true als de case zojuist is geopend.</summary>
    public bool Process(AirPodsStatus status)
    {
        var now = status.ReceivedAt;
        var afterSilence = now - _lastSeen > GapMeansClosed;
        _lastSeen = now;

        // De dekselstatus klopt alleen als er een oordop in de case zit; dan is de case-batterij bekend.
        var isOpen = status.Case.IsKnown && status.IsLidOpen;
        var justOpened = isOpen && (!_wasOpen || afterSilence);
        _wasOpen = isOpen;

        if (!justOpened || now - _lastFired < Cooldown)
            return false;

        _lastFired = now;
        return true;
    }

    /// <summary>Aanroepen als de status onbekend wordt; de volgende open deksel telt dan als nieuw.</summary>
    public void Reset() => _wasOpen = false;
}
