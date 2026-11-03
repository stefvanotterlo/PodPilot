using PodPilot.Core.Protocol;

namespace PodPilot.Core.Models;

/// <summary>Status van één onderdeel (linker oordop, rechter oordop of case).</summary>
/// <param name="BatteryPercent">0..100 in stappen van 10, of null als het onbekend is.</param>
/// <param name="IsCharging">True als het onderdeel aan het opladen is.</param>
/// <param name="IsInEar">True als de oordop in het oor zit (niet van toepassing op de case).</param>
public readonly record struct ComponentStatus(int? BatteryPercent, bool IsCharging, bool IsInEar)
{
    public static ComponentStatus Unknown => new(null, false, false);

    public bool IsKnown => BatteryPercent.HasValue;

    /// <summary>Korte tekst, bijv. "80%" of "?".</summary>
    public string PercentText => BatteryPercent is int p ? $"{p}%" : "?";
}

/// <summary>
/// Momentopname van de AirPods-status, klaar voor de UI.
/// Dit object verandert niet; bij elke nieuwe advertentie komt er een nieuw object.
/// </summary>
public sealed record AirPodsStatus(
    AirPodsModelInfo Model,
    ComponentStatus Left,
    ComponentStatus Right,
    ComponentStatus Case,
    bool IsLidOpen,
    short Rssi,
    ulong AdvertisementAddress,
    DateTimeOffset ReceivedAt,
    ProximityPairingMessage Message)
{
    /// <summary>Maakt een status van een gedecodeerd bericht.</summary>
    public static AirPodsStatus FromMessage(ProximityPairingMessage msg, ulong address, short rssi,
                                            DateTimeOffset receivedAt)
    {
        var model = AirPodsModels.Lookup(msg.ModelId);

        if (model.IsHeadphones)
        {
            // AirPods Max hebben maar één batterij. Welke nibble die gebruikt, is niet zeker,
            // dus nemen we de eerste die een waarde heeft. "Links" staat voor de koptelefoon.
            var battery = msg.LeftBattery ?? msg.RightBattery;
            var charging = msg.LeftCharging || msg.RightCharging;
            return new AirPodsStatus(model,
                new ComponentStatus(battery, charging, msg.LeftInEar || msg.RightInEar),
                ComponentStatus.Unknown, ComponentStatus.Unknown,
                IsLidOpen: false, rssi, address, receivedAt, msg);
        }

        return new AirPodsStatus(model,
            new ComponentStatus(msg.LeftBattery, msg.LeftCharging, msg.LeftInEar),
            new ComponentStatus(msg.RightBattery, msg.RightCharging, msg.RightInEar),
            new ComponentStatus(msg.CaseBattery, msg.CaseCharging, false),
            msg.IsLidOpen, rssi, address, receivedAt, msg);
    }

    /// <summary>
    /// Laagste bekende batterij van de oordopjes (of de koptelefoon), voor meldingen bij een lage batterij.
    /// De case telt niet mee.
    /// </summary>
    public int? LowestEarbudBattery =>
        new[] { Left.BatteryPercent, Right.BatteryPercent }.Where(b => b.HasValue).Min();

    /// <summary>Korte samenvatting voor bijvoorbeeld de tooltip van het systeemvak.</summary>
    public string ToSummary() => Model.IsHeadphones
        ? $"{Model.Name}: {Left.PercentText}"
        : $"{Model.Name} – L {Left.PercentText} · R {Right.PercentText} · Case {Case.PercentText}";
}
