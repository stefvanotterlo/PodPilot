using System.Diagnostics.CodeAnalysis;

namespace PodPilot.Core.Protocol;

/// <summary>
/// Gedecodeerd "Proximity Pairing"-bericht (Apple Continuity-type 0x07) zoals AirPods
/// dat via BLE-advertenties uitzenden.
/// </summary>
/// <remarks>
/// Indeling van de payload (de bytes na het type- en lengtebyte), gebaseerd op publiek
/// reverse-engineeringwerk (o.a. furiousMAC/continuity, OpenPods en AirPodsDesktop):
/// <code>
///  offset  inhoud
///  0       prefix (meestal 0x01)
///  1-2     model-ID, little endian (bijv. 0x2014 = AirPods Pro 2)
///  3       statusbyte
///            0x20 = deze advertentie komt van de LINKER oordop (anders van de rechter)
///            0x02 = uitzendende oordop zit in het oor
///            0x08 = andere oordop zit in het oor
///            0x40 = uitzendende oordop ligt in de case; dan zijn 0x02 en 0x08 omgedraaid
///                   (met --ear-test gemeten: één in het oor, de andere in de open case)
///  4      batterij: lage nibble = uitzendende oordop, hoge nibble = andere oordop
///            0..10 = 0..100% in stappen van 10, 15 = onbekend/niet aanwezig
///  5       hoge nibble = laadvlaggen (0x1 uitzendende oordop, 0x2 andere oordop, 0x4 case)
///          lage nibble = batterij van de case
///  6       deksel: bit 0x08 = deksel dicht; de overige bits zijn een teller
///  7       kleur van het apparaat
///  8       verbindingsstatus: 0x00 = nergens mee verbonden, 0x04 = verbonden (stil),
///          0x05 = muziek, 0x06 = gesprek, 0x07 = gaat over, 0x09 = ophangen (o.a. LibrePods)
///  9..     versleuteld deel (niet te lezen zonder de sleutel van het apparaat)
/// </code>
/// AirPods zenden afwisselend vanaf de linker en de rechter oordop uit. Daarom zijn de velden
/// "uitzendend"/"ander" en worden ze hier met de 0x20-vlag omgezet naar links/rechts.
/// </remarks>
public sealed class ProximityPairingMessage
{
    // Lengte van een AirPods-statusbericht: 9 leesbare bytes + 16 versleutelde bytes.
    // Er bestaan ook 0x07-berichten van 17 bytes (die beginnen met 0x06). Die bevatten geen
    // leesbare status; als we ze toch decoderen, ontstaan nepmodellen zoals 0x4C73.
    private const int StatusPayloadLength = 0x19;

    // Bits in het statusbyte.
    private const byte StatusLeftIsBroadcasting = 0x20;
    private const byte StatusBroadcastingInEar = 0x02;
    private const byte StatusOtherInEar = 0x08;
    private const byte StatusBroadcastingInCase = 0x40;

    // Bits in de hoge nibble van het laadbyte (na >> 4).
    private const byte ChargeBroadcasting = 0x01;
    private const byte ChargeOther = 0x02;
    private const byte ChargeCase = 0x04;

    // Bit in het dekselbyte.
    private const byte LidClosedBit = 0x08;

    private ProximityPairingMessage(byte[] payload)
    {
        Payload = payload;
    }

    /// <summary>De ruwe payload (zonder type- en lengtebyte). Handig voor debuggen.</summary>
    public byte[] Payload { get; }

    /// <summary>Model-ID, gelijk aan het Bluetooth-product-ID (PID) van het apparaat.</summary>
    public ushort ModelId => (ushort)(Payload[1] | (Payload[2] << 8));

    public byte StatusByte => Payload[3];
    public byte BatteryByte => Payload[4];
    public byte ChargeByte => Payload[5];
    public byte LidByte => Payload[6];

    /// <summary>Kleurcode van de behuizing.</summary>
    public byte Color => Payload[7];

    /// <summary>
    /// True als de linker oordop dit bericht uitzendt ("primair" is). Hiermee worden de
    /// velden van de uitzendende en de andere oordop omgezet naar links en rechts.
    /// </summary>
    public bool IsLeftBroadcasting => (StatusByte & StatusLeftIsBroadcasting) != 0;

    // --- Ruwe velden per uitzendende/andere oordop ---------------------------------------

    private int BroadcastingNibble => BatteryByte & 0x0F;
    private int OtherNibble => (BatteryByte >> 4) & 0x0F;
    private int ChargeFlags => (ChargeByte >> 4) & 0x0F;

    private bool BroadcastingCharging => (ChargeFlags & ChargeBroadcasting) != 0;
    private bool OtherCharging => (ChargeFlags & ChargeOther) != 0;
    // Zendt de oordop uit de case, dan gaan de in-oor-bits over de andere kant: 0x02 is dan de
    // andere oordop. Zonder deze omwisseling knippert "in oor" tussen links en rechts.
    private bool BroadcastingIsInCase => (StatusByte & StatusBroadcastingInCase) != 0;
    private bool BroadcastingInEar =>
        (StatusByte & (BroadcastingIsInCase ? StatusOtherInEar : StatusBroadcastingInEar)) != 0;
    private bool OtherInEar =>
        (StatusByte & (BroadcastingIsInCase ? StatusBroadcastingInEar : StatusOtherInEar)) != 0;

    // --- Omgezet naar links/rechts ----------------------------------------------------------

    public int? LeftBattery => NibbleToPercent(IsLeftBroadcasting ? BroadcastingNibble : OtherNibble);
    public int? RightBattery => NibbleToPercent(IsLeftBroadcasting ? OtherNibble : BroadcastingNibble);
    public int? CaseBattery => NibbleToPercent(ChargeByte & 0x0F);

    public bool LeftCharging => IsLeftBroadcasting ? BroadcastingCharging : OtherCharging;
    public bool RightCharging => IsLeftBroadcasting ? OtherCharging : BroadcastingCharging;
    public bool CaseCharging => (ChargeFlags & ChargeCase) != 0;

    public bool LeftInEar => IsLeftBroadcasting ? BroadcastingInEar : OtherInEar;
    public bool RightInEar => IsLeftBroadcasting ? OtherInEar : BroadcastingInEar;

    /// <summary>
    /// True als de case open is. Dit is alleen betekenisvol als er minstens één oordop in de
    /// case zit. Bij een gesloten case zenden de AirPods meestal helemaal niets uit.
    /// </summary>
    public bool IsLidOpen => (LidByte & LidClosedBit) == 0;

    /// <summary>
    /// Zoekt in de Apple-fabrikantdata (zonder company ID, zoals Windows die levert) naar een
    /// Proximity Pairing-bericht en decodeert het.
    /// </summary>
    /// <param name="manufacturerData">De bytes van <c>BluetoothLEManufacturerData.Data</c>.</param>
    /// <param name="message">Het gedecodeerde bericht, als dat is gevonden.</param>
    public static bool TryParse(ReadOnlySpan<byte> manufacturerData,
                                [NotNullWhen(true)] out ProximityPairingMessage? message)
    {
        message = null;

        // Apple-fabrikantdata bestaat uit één of meer TLV's: [type][lengte][lengte bytes data].
        var offset = 0;
        while (offset + 2 <= manufacturerData.Length)
        {
            var type = manufacturerData[offset];
            var length = manufacturerData[offset + 1];
            var payloadStart = offset + 2;

            // Een afgekapte TLV aan het eind: neem wat er is, maar niet meer dan er staat.
            var available = Math.Min(length, manufacturerData.Length - payloadStart);

            if (type == AppleContinuity.ProximityPairingType)
            {
                if (length != StatusPayloadLength || available < StatusPayloadLength)
                    return false;

                message = new ProximityPairingMessage(
                    manufacturerData.Slice(payloadStart, available).ToArray());
                return true;
            }

            offset = payloadStart + length;
        }

        return false;
    }

    /// <summary>Zet een batterij-nibble (0..10) om naar een percentage; anders null.</summary>
    private static int? NibbleToPercent(int nibble) => nibble <= 10 ? nibble * 10 : null;

    /// <summary>Hexweergave van de payload, bijv. "01 14 20 2B ...".</summary>
    public string ToHex() => BitConverter.ToString(Payload).Replace('-', ' ');
}
