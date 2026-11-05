using PodPilot.App.Services;
using PodPilot.Core.Models;

namespace PodPilot.App.ViewModels;

/// <summary>Alles wat de pop-up en het tray-icoon tonen.</summary>
public sealed class StatusViewModel : ObservableObject
{
    private string _deviceName = "AirPods";
    private string _modelName = "";
    private string _statusText = "";
    private bool _isKnown;
    private ushort? _lastModelId;

    public StatusViewModel()
    {
        Components = [Left, Right, Case];
    }

    public ComponentViewModel Left { get; } = new(ComponentKind.LeftBud, L.T("Links"));
    public ComponentViewModel Right { get; } = new(ComponentKind.RightBud, L.T("Rechts"));
    public ComponentViewModel Case { get; } = new(ComponentKind.Case, L.T("Case"));

    /// <summary>Wat er nu speelt (pop-up en Overzicht).</summary>
    public NowPlayingViewModel NowPlaying { get; } = new();

    /// <summary>De onderdelen in de volgorde waarin de pop-up ze toont.</summary>
    public IReadOnlyList<ComponentViewModel> Components { get; }

    /// <summary>Naam van het gepairde apparaat, bijv. "Mijn AirPods".</summary>
    public string DeviceName
    {
        get => _deviceName;
        set
        {
            if (SetField(ref _deviceName, value))
                OnPropertyChanged(nameof(TooltipText));
        }
    }

    public string ModelName
    {
        get => _modelName;
        private set => SetField(ref _modelName, value);
    }

    /// <summary>Regel onder de naam, bijv. "Case open · bijgewerkt 10:31".</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsKnown
    {
        get => _isKnown;
        private set => SetField(ref _isKnown, value);
    }

    /// <summary>Regel onder de naam in de pop-up: model en status samen.</summary>
    public string SubtitleText => IsKnown ? $"{ModelName} · {StatusText}" : StatusText;

    /// <summary>Laagste batterij van de oordopjes, voor de kleur van het tray-icoon.</summary>
    public int? LowestPercent =>
        new[] { Left.Percent, Right.Percent }.Where(p => p.HasValue).Min();

    /// <summary>Tekst voor de tooltip van het systeemvak (maximaal 127 tekens).</summary>
    public string TooltipText
    {
        get
        {
            if (!IsKnown)
                return L.T("{0}: status onbekend", DeviceName);

            if (Case.IsVisible is false)
                return $"{DeviceName}\n{Left.PercentText}";

            return $"{DeviceName}\n{L.T("L")} {Left.PercentText}{InEar(Left)} · {L.T("R")} {Right.PercentText}{InEar(Right)} · {L.T("Case")} {Case.PercentText}";

            static string InEar(ComponentViewModel bud) => bud.IsInEar ? L.T(" (in oor)") : "";
        }
    }

    // --- Verbinding met deze pc -------------------------------------------------------------

    private bool _isPaired;
    private bool _isConnected;
    private bool _isBusy;
    private string? _connectionMessage;

    /// <summary>True als er gepairde AirPods zijn gevonden (anders: knop "Koppelen…").</summary>
    public bool IsPaired => _isPaired;

    /// <summary>True als de audio van de AirPods met deze pc verbonden is.</summary>
    public bool IsConnected => _isConnected;

    public bool CanToggleConnection => !_isBusy;

    public string ConnectButtonText =>
        !_isPaired ? L.T("Koppelen…")
        : _isBusy ? L.T("Bezig…")
        : _isConnected ? L.T("Verbinding verbreken")
        : L.T("Verbinden");

    public string ConnectionText =>
        _connectionMessage
        ?? (!_isPaired ? L.T("Niet gekoppeld met deze pc")
            : _isConnected ? L.T("Verbonden met deze pc")
            : L.T("Niet verbonden"));

    /// <summary>Werkt de verbindingsstatus bij. Een eventuele foutmelding verdwijnt als de status echt verandert.</summary>
    public void SetConnection(bool isPaired, bool isConnected)
    {
        if (_isConnected != isConnected)
            _connectionMessage = null;

        _isPaired = isPaired;
        _isConnected = isConnected;
        RaiseConnectionChanged();
    }

    /// <summary>Tijdens verbinden/verbreken: knop uitschakelen en een tekst tonen.</summary>
    public void SetBusy(bool isBusy, string? message)
    {
        _isBusy = isBusy;
        _connectionMessage = message;
        RaiseConnectionChanged();
    }

    private void RaiseConnectionChanged()
    {
        OnPropertyChanged(nameof(IsPaired));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(CanToggleConnection));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectionText));
    }

    /// <summary>Verwerkt een nieuwe status van de scanner.</summary>
    public void Apply(AirPodsStatus status)
    {
        IsKnown = true;
        ModelName = L.T(status.Model.Name);
        ConfigureForModel(status.Model);

        Left.Update(status.Left.BatteryPercent, status.Left.IsCharging, status.Left.IsInEar);
        Right.Update(status.Right.BatteryPercent, status.Right.IsCharging, status.Right.IsInEar);

        // Een case zonder oordopjes geeft zijn batterij niet door. Toon dan de laatst bekende
        // waarde (gedimd), zolang het om dezelfde AirPods gaat.
        if (status.Case.IsKnown)
            Case.Update(status.Case.BatteryPercent, status.Case.IsCharging, false);
        else if (Case.Percent is int lastKnown && _lastModelId == status.Model.Id)
            Case.Update(lastKnown, false, false, isStale: true);
        else
            Case.Clear();

        _lastModelId = status.Model.Id;

        // De dekselstatus klopt alleen als er een oordop in de case zit; anders laten we hem weg.
        var lid = status.Case.IsKnown && status.IsLidOpen ? L.T("Case open · ") : "";
        StatusText = lid + L.T("bijgewerkt {0:HH:mm:ss}", status.ReceivedAt);

        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(LowestPercent));
        OnPropertyChanged(nameof(TooltipText));
    }

    /// <summary>Zet alles op "onbekend", met een uitleg voor de gebruiker.</summary>
    public void MarkUnknown(string reason)
    {
        IsKnown = false;
        Left.Clear();
        Right.Clear();
        Case.Clear();
        StatusText = reason;

        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(LowestPercent));
        OnPropertyChanged(nameof(TooltipText));
    }

    /// <summary>Een koptelefoon (AirPods Max) toont één onderdeel in plaats van drie.</summary>
    private void ConfigureForModel(AirPodsModelInfo model)
    {
        Left.Kind = model.IsHeadphones ? ComponentKind.Headphones : ComponentKind.LeftBud;
        Left.Label = model.IsHeadphones ? L.T("Koptelefoon") : L.T("Links");
        Right.IsVisible = !model.IsHeadphones;
        Case.IsVisible = model.HasCase;
    }
}
