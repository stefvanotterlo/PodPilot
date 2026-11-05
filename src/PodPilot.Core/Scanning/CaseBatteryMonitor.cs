using PodPilot.Core.Models;

namespace PodPilot.Core.Scanning;

public enum CaseBatteryEvent
{
    None,

    /// <summary>De case kwam op of onder de drempel (en laadt niet op).</summary>
    Low,

    /// <summary>De case laadt op en is net op 100% gekomen.</summary>
    Full,
}

/// <summary>
/// Meldingen over de batterij van de case: bijna leeg en volledig opgeladen.
/// </summary>
/// <remarks>
/// De case geeft zijn batterij alleen door als er een oordop in ligt en de deksel open is, dus een
/// melding kan alleen op zo'n moment komen. Per situatie hoogstens één melding: "leeg" pas opnieuw
/// na opladen of als hij duidelijk boven de drempel is geweest, "vol" pas opnieuw als hij weer onder
/// de 90% is geweest.
/// </remarks>
public sealed class CaseBatteryMonitor
{
    private const int RearmMargin = 10;

    private bool _lowNotified;
    private bool _fullNotified;

    /// <summary>Drempel voor "bijna leeg" in procenten; 0 = uit.</summary>
    public int LowThreshold { get; set; } = 20;

    /// <summary>Melding als de case op 100% komt tijdens het opladen.</summary>
    public bool NotifyFull { get; set; } = true;

    public CaseBatteryEvent Process(AirPodsStatus status)
    {
        if (status.Model.IsHeadphones || status.Case.BatteryPercent is not int percent)
            return CaseBatteryEvent.None;

        // Bijna leeg.
        if (status.Case.IsCharging || percent > LowThreshold + RearmMargin)
            _lowNotified = false;
        else if (LowThreshold > 0 && !_lowNotified && percent <= LowThreshold)
        {
            _lowNotified = true;
            return CaseBatteryEvent.Low;
        }

        // Vol (de eerste keer 100% zien terwijl hij laadt, of net ervoor laadde).
        if (percent < 90)
            _fullNotified = false;
        else if (NotifyFull && !_fullNotified && percent >= 100 && status.Case.IsCharging)
        {
            _fullNotified = true;
            return CaseBatteryEvent.Full;
        }

        return CaseBatteryEvent.None;
    }
}
