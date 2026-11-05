using System.Windows.Input;
using System.Windows.Media;
using PodPilot.App.Services;

namespace PodPilot.App.ViewModels;

/// <summary>Wat er nu speelt (titel, artiest, app en hoes), voor de pop-up en het Overzicht.</summary>
public sealed class NowPlayingViewModel : ObservableObject
{
    private bool _hasMedia;
    private string _title = "";
    private string _artist = "";
    private string _appName = "";
    private bool _isPlaying;
    private ImageSource? _thumbnail;

    /// <summary>True als er een speler met een titel is; anders wordt het blok verborgen.</summary>
    public bool HasMedia
    {
        get => _hasMedia;
        set => SetField(ref _hasMedia, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    /// <summary>Artiest, of leeg (bijv. bij een video in de browser).</summary>
    public string Artist
    {
        get => _artist;
        set
        {
            if (SetField(ref _artist, value))
                OnPropertyChanged(nameof(Subtitle));
        }
    }

    /// <summary>Naam van de speler, bijv. "Spotify" of "Microsoft Edge".</summary>
    public string AppName
    {
        get => _appName;
        set
        {
            if (SetField(ref _appName, value))
                OnPropertyChanged(nameof(Subtitle));
        }
    }

    /// <summary>"Artiest · App", of alleen de app als er geen artiest is.</summary>
    public string Subtitle => string.IsNullOrWhiteSpace(Artist) ? AppName
        : string.IsNullOrWhiteSpace(AppName) ? Artist
        : $"{Artist} · {AppName}";

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetField(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(PlayPauseGlyph));
                OnPropertyChanged(nameof(PlayPauseToolTip));
            }
        }
    }

    /// <summary>E769 = pauze, E768 = afspelen (Segoe Fluent Icons).</summary>
    public string PlayPauseGlyph => _isPlaying ? "" : "";

    public string PlayPauseToolTip => _isPlaying ? L.T("Pauzeren") : L.T("Afspelen");

    /// <summary>Kopje boven de titel.</summary>
    public string StateText => _isPlaying ? L.T("Nu aan het afspelen") : L.T("Gepauzeerd");

    /// <summary>Albumhoes of miniatuur; null als de speler er geen geeft.</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (SetField(ref _thumbnail, value))
                OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    public bool HasThumbnail => _thumbnail is not null;

    private bool _canPrevious;
    private bool _canNext;
    private bool _canPlayPause;

    public bool CanPrevious { get => _canPrevious; set => SetField(ref _canPrevious, value); }
    public bool CanNext { get => _canNext; set => SetField(ref _canNext, value); }
    public bool CanPlayPause { get => _canPlayPause; set => SetField(ref _canPlayPause, value); }

    /// <summary>Knoppen; de acties worden door de NowPlayingService ingevuld.</summary>
    public ICommand PreviousCommand { get; } = new ActionCommand();
    public ICommand PlayPauseCommand { get; } = new ActionCommand();
    public ICommand NextCommand { get; } = new ActionCommand();

    internal void SetActions(Func<Task> previous, Func<Task> playPause, Func<Task> next)
    {
        ((ActionCommand)PreviousCommand).Action = previous;
        ((ActionCommand)PlayPauseCommand).Action = playPause;
        ((ActionCommand)NextCommand).Action = next;
    }

    /// <summary>Eenvoudige command; IsEnabled van de knop komt uit de Can…-eigenschappen.</summary>
    private sealed class ActionCommand : ICommand
    {
        public Func<Task>? Action { get; set; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter)
        {
            if (Action is not null)
                await Action();
        }
    }
}
