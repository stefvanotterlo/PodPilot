using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PodPilot.App.ViewModels;
using Windows.Media.Control;

namespace PodPilot.App.Services;

/// <summary>
/// Houdt bij welke media er speelt (via de mediabediening van Windows, dezelfde sessies als in het
/// volumemenu) en zet dat in een <see cref="NowPlayingViewModel"/>.
/// </summary>
internal sealed class NowPlayingService : IDisposable
{
    private readonly NowPlayingViewModel _model;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, string> _appNames = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly HashSet<GlobalSystemMediaTransportControlsSession> _watched = [];
    private GlobalSystemMediaTransportControlsSession? _session; // de getoonde sessie (voor de knoppen)
    private string _thumbnailKey = "";
    private bool _refreshing;
    private bool _refreshAgain;

    public NowPlayingService(NowPlayingViewModel model, Dispatcher dispatcher)
    {
        _model = model;
        _dispatcher = dispatcher;
        // Vangnet: niet elke speler meldt elke wijziging netjes.
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            (_, _) => Refresh(), dispatcher);
        _model.SetActions(
            () => RunAsync("vorige", s => s.TrySkipPreviousAsync().AsTask()),
            () => RunAsync("afspelen/pauzeren", s => s.TryTogglePlayPauseAsync().AsTask()),
            () => RunAsync("volgende", s => s.TrySkipNextAsync().AsTask()));
    }

    // Voor de mediasneltoetsen: hetzelfde als de knoppen in de pop-up.
    public Task PlayPauseAsync() => RunAsync("afspelen/pauzeren (sneltoets)", s => s.TryTogglePlayPauseAsync().AsTask());
    public Task NextAsync() => RunAsync("volgende (sneltoets)", s => s.TrySkipNextAsync().AsTask());
    public Task PreviousAsync() => RunAsync("vorige (sneltoets)", s => s.TrySkipPreviousAsync().AsTask());

    /// <summary>Stuurt een knop door naar de getoonde sessie en werkt daarna meteen bij.</summary>
    private async Task RunAsync(string what, Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        if (_session is not { } session)
            return;
        try
        {
            var ok = await action(session);
            DiagnosticLog.Write($"media: {what} {session.SourceAppUserModelId} → {(ok ? "gelukt" : "geweigerd")}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"media: {what} mislukt: {ex.Message}");
        }
        Refresh();
    }

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.SessionsChanged += (_, _) => Refresh();
            _manager.CurrentSessionChanged += (_, _) => Refresh();
            _timer.Start();
            Refresh();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"media: nu aan het afspelen niet beschikbaar: {ex.Message}");
        }
    }

    /// <summary>Kan vanaf elke thread worden aangeroepen; werkt bij op de UI-thread.</summary>
    private void Refresh() => _dispatcher.BeginInvoke(async () =>
    {
        // Niet twee keer tegelijk (het ophalen van de hoes is asynchroon).
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }
        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                await UpdateAsync();
            }
            while (_refreshAgain);
        }
        finally
        {
            _refreshing = false;
        }
    });

    private async Task UpdateAsync()
    {
        if (_manager is null)
            return;

        try
        {
            var sessions = _manager.GetSessions();
            foreach (var s in sessions)
            {
                if (_watched.Add(s))
                {
                    s.MediaPropertiesChanged += (_, _) => Refresh();
                    s.PlaybackInfoChanged += (_, _) => Refresh();
                }
            }

            // Wat speelt gaat voor; anders de sessie die Windows als huidige ziet (gepauzeerd).
            var session = sessions.FirstOrDefault(s =>
                              s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                          ?? _manager.GetCurrentSession();
            if (session is null)
            {
                Clear();
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            if (props is null || string.IsNullOrWhiteSpace(props.Title))
            {
                Clear();
                return;
            }

            _model.Title = props.Title.Trim();
            _model.Artist = (string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist : props.Artist)?.Trim() ?? "";
            _model.AppName = GetAppName(session.SourceAppUserModelId);
            var playback = session.GetPlaybackInfo();
            _model.IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            _model.CanPrevious = playback.Controls.IsPreviousEnabled;
            _model.CanNext = playback.Controls.IsNextEnabled;
            _model.CanPlayPause = playback.Controls.IsPlayPauseToggleEnabled
                                  || playback.Controls.IsPlayEnabled || playback.Controls.IsPauseEnabled;
            _session = session;
            _model.HasMedia = true;

            // De hoes alleen opnieuw laden bij een ander nummer.
            var key = $"{session.SourceAppUserModelId}|{props.Title}|{props.Artist}|{props.Thumbnail is not null}";
            if (key != _thumbnailKey)
            {
                _thumbnailKey = key;
                _model.Thumbnail = await LoadThumbnailAsync(props);
            }
        }
        catch (Exception)
        {
            // Sessie verdwenen terwijl we keken (app gesloten): volgende keer opnieuw.
        }
    }

    private void Clear()
    {
        _session = null;
        _model.HasMedia = false;
        _model.Thumbnail = null;
        _thumbnailKey = "";
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        if (props.Thumbnail is null)
            return null;
        try
        {
            using var stream = await props.Thumbnail.OpenReadAsync();
            using var source = stream.AsStreamForRead();
            var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            memory.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160;
            image.StreamSource = memory;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Leesbare naam van de speler: "Spotify.exe" → "Spotify", "MSEdge" → "Microsoft Edge".</summary>
    private string GetAppName(string id)
    {
        if (_appNames.TryGetValue(id, out var cached))
            return cached;

        var name = id;
        try
        {
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var process = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(id)).FirstOrDefault();
                var description = process?.MainModule?.FileVersionInfo.FileDescription;
                name = string.IsNullOrWhiteSpace(description) ? Path.GetFileNameWithoutExtension(id) : description;
            }
            else
            {
                name = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(id).DisplayInfo.DisplayName;
            }
        }
        catch (Exception)
        {
            name = id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(id) : id;
        }

        _appNames[id] = name;
        return name;
    }

    public void Dispose() => _timer.Stop();
}
