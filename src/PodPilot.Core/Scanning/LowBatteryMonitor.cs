using PodPilot.Core.Models;

namespace PodPilot.Core.Scanning;

/// <summary>Eén oordop (of de koptelefoon) waarvan de batterij laag is.</summary>
public sealed record LowBatteryItem(string Label, int Percent);

/// <summary>
/// Bepaalt wanneer er een melding "batterij bijna leeg" moet komen.
/// </summary>
/// <remarks>
/// Per oordop komt er hoogstens één melding. Pas als de oordop weer oplaadt of duidelijk boven de
/// drempel komt (drempel + 10%), kan er een nieuwe komen. Zo voorkomen we een melding bij elke
/// advertentie. De case telt niet mee: een lege case merk je pas als je wilt opladen.
/// </remarks>
public sealed class LowBatteryMonitor
{
    private const int RearmMargin = 10;

    private bool _leftNotified;
    private bool _rightNotified;

    /// <summary>Drempel in procenten. Bij deze waarde of lager volgt een melding. 0 = uit.</summary>
    public int Threshold { get; set; } = 20;

    /// <summary>
    /// Verwerkt een status. Geeft de oordopjes die net onder de drempel zijn gekomen, of een lege
    /// lijst als er geen melding nodig is.
    /// </summary>
    public IReadOnlyList<LowBatteryItem> Process(AirPodsStatus status)
    {
        var alerts = new List<LowBatteryItem>();
        if (Threshold <= 0)
            return alerts;

        var leftLabel = status.Model.IsHeadphones ? "Koptelefoon" : "Links";
        Check(status.Left, leftLabel, ref _leftNotified, alerts);
        if (!status.Model.IsHeadphones)
            Check(status.Right, "Rechts", ref _rightNotified, alerts);

        return alerts;
    }

    private void Check(ComponentStatus component, string label, ref bool notified, List<LowBatteryItem> alerts)
    {
        if (component.BatteryPercent is not int percent)
            return;

        // Opladen of weer ruim boven de drempel: later mag er een nieuwe melding komen.
        if (component.IsCharging || percent > Threshold + RearmMargin)
        {
            notified = false;
            return;
        }

        if (!notified && percent <= Threshold)
        {
            notified = true;
            alerts.Add(new LowBatteryItem(label, percent));
        }
    }
}
