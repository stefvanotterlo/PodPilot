using System.Windows.Input;

namespace PodPilot.App.Services;

/// <summary>
/// Een toetscombinatie, bijv. Ctrl+Alt+P. In settings.json staat hij als tekst ("Ctrl+Alt+P").
/// </summary>
public readonly record struct Hotkey(ModifierKeys Modifiers, Key Key)
{
    private static readonly (ModifierKeys Modifier, string Name)[] ModifierNames =
    [
        (ModifierKeys.Control, "Ctrl"),
        (ModifierKeys.Alt, "Alt"),
        (ModifierKeys.Shift, "Shift"),
        (ModifierKeys.Windows, "Win"),
    ];

    public override string ToString()
    {
        var modifiers = Modifiers; // een lambda in een struct mag 'this' niet gebruiken
        var parts = ModifierNames.Where(m => modifiers.HasFlag(m.Modifier)).Select(m => m.Name).ToList();
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    /// <summary>Leest een combinatie uit settings.json. Geeft null bij lege of onleesbare tekst.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var modifiers = ModifierKeys.None;
        foreach (var part in parts[..^1])
        {
            var match = ModifierNames.FirstOrDefault(m => m.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null)
                return null;
            modifiers |= match.Modifier;
        }

        // Cijfers staan als "1" in de tekst, maar heten D1 in de Key-enum.
        var keyName = parts[^1].Length == 1 && char.IsDigit(parts[^1][0]) ? "D" + parts[^1] : parts[^1];
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key))
            return null;

        var hotkey = new Hotkey(modifiers, key);
        return hotkey.IsValid ? hotkey : null;
    }

    /// <summary>
    /// Een losse letter zou tijdens het typen de pop-up openen. Daarom is een Ctrl, Alt of Win
    /// nodig (Shift alleen is ook gewoon typen), behalve bij de functietoetsen F1 t/m F24.
    /// </summary>
    public bool IsValid =>
        !IsModifierKey(Key) && Key != Key.None
        && ((Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0
            || Key is >= Key.F1 and <= Key.F24);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt
        or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    private static string KeyName(Key key) => key is >= Key.D0 and <= Key.D9 ? key.ToString()[1..] : key.ToString();
}
