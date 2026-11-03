namespace PodPilot.Core.Protocol;

/// <summary>
/// Constanten uit het (niet officieel gedocumenteerde) Apple Continuity-protocol.
/// </summary>
public static class AppleContinuity
{
    /// <summary>Bluetooth SIG company identifier van Apple.</summary>
    public const ushort AppleCompanyId = 0x004C;

    /// <summary>TLV-berichttype "Proximity Pairing": de status van AirPods en Beats.</summary>
    public const byte ProximityPairingType = 0x07;

    /// <summary>Waarde van een batterij-nibble als het onderdeel niet aanwezig of onbekend is.</summary>
    public const int BatteryUnknownNibble = 0x0F;
}
