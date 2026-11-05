using PodPilot.App.Services;
namespace PodPilot.App.ViewModels;

/// <summary>Bepaalt welk icoon de pop-up toont.</summary>
public enum ComponentKind
{
    LeftBud,
    RightBud,
    Case,
    Headphones,
}

/// <summary>Eén onderdeel in de pop-up: linker oordop, rechter oordop, case of koptelefoon.</summary>
public sealed class ComponentViewModel : ObservableObject
{
    private ComponentKind _kind;
    private string _label;
    private int? _percent;
    private bool _isCharging;
    private bool _isInEar;
    private bool _isStale;
    private bool _isVisible = true;

    public ComponentViewModel(ComponentKind kind, string label)
    {
        _kind = kind;
        _label = label;
    }

    public ComponentKind Kind
    {
        get => _kind;
        set => SetField(ref _kind, value);
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetField(ref _label, value))
                OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    /// <summary>Naam onder het icoon, met "in oor" erbij als de oordop in het oor zit.</summary>
    public string DisplayLabel => _isInEar ? L.T("{0} · in oor", _label) : _label;

    /// <summary>Batterij in procenten, of null als die onbekend is.</summary>
    public int? Percent => _percent;

    public bool IsCharging => _isCharging;

    public bool IsInEar => _isInEar;

    /// <summary>
    /// True als dit de laatst bekende waarde is en niet een actuele, bijvoorbeeld bij de case
    /// zodra er geen oordop meer in zit.
    /// </summary>
    public bool IsStale => _isStale;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public string PercentText => _percent is int p ? $"{p}%" : "–";

    /// <summary>Waarde voor de batterijbalk (0..100).</summary>
    public double Value => _percent ?? 0;

    /// <summary>Niveau voor de kleur van de balk: Unknown, Low, Medium of High.</summary>
    public string Level => _percent switch
    {
        null => "Unknown",
        <= 20 => "Low",
        <= 50 => "Medium",
        _ => "High",
    };

    public string ToolTipText => _isStale ? L.T("Laatst bekende waarde") : _label;

    /// <summary>Werkt alle waarden in één keer bij.</summary>
    public void Update(int? percent, bool isCharging, bool isInEar, bool isStale = false)
    {
        if (SetField(ref _percent, percent, nameof(Percent)))
        {
            OnPropertyChanged(nameof(PercentText));
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(Level));
        }

        SetField(ref _isCharging, isCharging, nameof(IsCharging));
        if (SetField(ref _isInEar, isInEar, nameof(IsInEar)))
            OnPropertyChanged(nameof(DisplayLabel));
        if (SetField(ref _isStale, isStale, nameof(IsStale)))
            OnPropertyChanged(nameof(ToolTipText));
    }

    public void Clear()
    {
        Update(null, false, false);
        SetRemaining(null);
    }

    private string _remainingText = "";

    /// <summary>Geschatte resterende luistertijd, bijv. "nog ~2 u 10 min"; leeg als er geen schatting is.</summary>
    public string RemainingText
    {
        get => _remainingText;
        private set
        {
            if (SetField(ref _remainingText, value))
                OnPropertyChanged(nameof(HasRemaining));
        }
    }

    public bool HasRemaining => _remainingText.Length > 0;

    public void SetRemaining(TimeSpan? remaining) => RemainingText = FormatRemaining(remaining);

    /// <summary>Eigen tekst op de plek van de resterende tijd, bijv. "om 11:42" bij de case.</summary>
    public void SetNote(string text) => RemainingText = text;

    /// <summary>Uitleg bij de regel onder het percentage.</summary>
    public string NoteToolTip => _kind == ComponentKind.Case
        ? L.T("Laatst bekende waarde: de case geeft zijn batterij alleen door als er een oordop in ligt")
        : L.T("Schatting op basis van je eerdere luistersessies");

    private static string FormatRemaining(TimeSpan? remaining)
    {
        if (remaining is not TimeSpan r)
            return "";
        if (r.TotalMinutes < 10)
            return L.T("nog < 10 min");

        // AirPods melden in stappen van 10%; op 10 minuten afronden is al nauwkeurig genoeg.
        var minutes = (int)Math.Round(r.TotalMinutes / 10) * 10;
        if (minutes < 60)
            return L.T("nog ~{0} min", minutes);
        return minutes % 60 == 0 ? L.T("nog ~{0} u", minutes / 60) : L.T("nog ~{0} u {1} min", minutes / 60, minutes % 60);
    }
}
