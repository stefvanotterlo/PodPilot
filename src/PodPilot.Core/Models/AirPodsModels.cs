namespace PodPilot.Core.Models;

/// <summary>Beschrijving van een herkend model.</summary>
/// <param name="Id">Model-ID / Bluetooth-product-ID (bijv. 0x2014).</param>
/// <param name="Name">Weergavenaam.</param>
/// <param name="HasCase">False voor koptelefoons zonder oplaadcase.</param>
/// <param name="IsHeadphones">True voor een over-ear-koptelefoon (één batterij).</param>
/// <param name="IsKnown">False als het ID niet in de tabel staat.</param>
public sealed record AirPodsModelInfo(
    ushort Id,
    string Name,
    bool HasCase = true,
    bool IsHeadphones = false,
    bool IsKnown = true);

/// <summary>
/// Opzoektabel van model-ID's. Het ID is het Bluetooth-product-ID (Apple vendor ID 0x004C).
/// Staat je model er niet in? De consolescanner toont het ID, zodat je het hier kunt toevoegen.
/// </summary>
public static class AirPodsModels
{
    private static readonly Dictionary<ushort, AirPodsModelInfo> Known = new AirPodsModelInfo[]
    {
        new(0x2002, "AirPods (1e generatie)"),
        new(0x200F, "AirPods (2e generatie)"),
        new(0x2013, "AirPods (3e generatie)"),
        new(0x2019, "AirPods 4"),
        new(0x201B, "AirPods 4 (ANC)"),
        new(0x200E, "AirPods Pro"),
        new(0x2014, "AirPods Pro 2"),
        new(0x2024, "AirPods Pro 2 (USB-C)"),
        new(0x2027, "AirPods Pro 3"), // gezien in de buurt; vermoedelijk Pro 3, nog niet bevestigd
        new(0x200A, "AirPods Max", HasCase: false, IsHeadphones: true),
        new(0x201F, "AirPods Max (USB-C)", HasCase: false, IsHeadphones: true),
    }.ToDictionary(m => m.Id);

    /// <summary>Geeft de modelinformatie, of een "onbekend"-record met het ID in de naam.</summary>
    public static AirPodsModelInfo Lookup(ushort id) =>
        Known.TryGetValue(id, out var model)
            ? model
            : new AirPodsModelInfo(id, $"Onbekend Apple-audioapparaat (0x{id:X4})", IsKnown: false);
}
