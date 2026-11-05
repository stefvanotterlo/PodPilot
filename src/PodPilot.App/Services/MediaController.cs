using Windows.Media.Control;

namespace PodPilot.App.Services;

/// <summary>
/// Pauzeert en hervat media via de mediabediening van Windows (dezelfde sessies als in het
/// volumemenu: Spotify, browsers, Mediaspeler, ...).
/// </summary>
internal sealed class MediaController
{
    // Na zo lang uit het oor hervatten we niet meer automatisch; dan was je echt gestopt.
    private static readonly TimeSpan ResumeWindow = TimeSpan.FromMinutes(5);

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private List<GlobalSystemMediaTransportControlsSession> _pausedSessions = [];
    private DateTimeOffset _pausedAt;

    /// <summary>True als wij iets hebben gepauzeerd dat nog niet is hervat.</summary>
    public bool HasPausedByUs => _pausedSessions.Count > 0;

    /// <summary>
    /// Pauzeert alle sessies die nu spelen en onthoudt welke dat waren. We kijken naar álle sessies,
    /// niet alleen de "huidige": Windows kiest als huidige soms een andere app dan die speelt
    /// (bijv. een browsertabblad terwijl Spotify speelt).
    /// </summary>
    public async Task PauseIfPlayingAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _manager.GetSessions();
            DiagnosticLog.Write($"media: {sessions.Count} sessie(s): " +
                string.Join(", ", sessions.Select(s => $"{s.SourceAppUserModelId}={s.GetPlaybackInfo().PlaybackStatus}")));

            foreach (var session in sessions)
            {
                if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    continue;

                var paused = await session.TryPauseAsync();
                DiagnosticLog.Write($"media: pauze {session.SourceAppUserModelId} → {(paused ? "gelukt" : "geweigerd")}");
                if (paused)
                    _pausedSessions.Add(session);
            }

            _pausedAt = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            // Geen mediasessies of de API is niet beschikbaar: niets te pauzeren.
            DiagnosticLog.Write($"media: pauzeren mislukt: {ex.Message}");
        }
    }

    /// <summary>
    /// Hervat de sessies die wij hebben gepauzeerd, als dat niet te lang geleden is en de gebruiker
    /// intussen niet zelf op afspelen heeft gedrukt.
    /// </summary>
    public async Task ResumeIfPausedByUsAsync()
    {
        var sessions = _pausedSessions;
        _pausedSessions = [];
        if (DateTimeOffset.Now - _pausedAt > ResumeWindow)
        {
            DiagnosticLog.Write("media: niet hervat, te lang geleden gepauzeerd");
            return;
        }

        foreach (var session in sessions)
        {
            try
            {
                if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    continue;

                var played = await session.TryPlayAsync();
                DiagnosticLog.Write($"media: hervat {session.SourceAppUserModelId} → {(played ? "gelukt" : "geweigerd")}");
            }
            catch (Exception)
            {
                // De sessie bestaat niet meer (app gesloten).
            }
        }
    }

    /// <summary>Vergeet gepauzeerde sessies, bijv. als de AirPods niet meer verbonden zijn.</summary>
    public void Forget() => _pausedSessions = [];

    /// <summary>
    /// Stuurt "afspelen" naar de mediasessie die Windows als huidige ziet (de speler die het laatst
    /// actief was). Speelt er al iets, of is er geen speler open, dan gebeurt er niets.
    /// </summary>
    public async Task PlayCurrentAsync()
    {
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_manager.GetSessions().Any(s =>
                    s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing))
            {
                DiagnosticLog.Write("media: afspelen overgeslagen, er speelt al iets");
                return;
            }

            if (_manager.GetCurrentSession() is not { } session)
            {
                DiagnosticLog.Write("media: afspelen overgeslagen, geen speler open");
                return;
            }

            var played = await session.TryPlayAsync();
            DiagnosticLog.Write($"media: afspelen {session.SourceAppUserModelId} → {(played ? "gelukt" : "geweigerd")}");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write($"media: afspelen mislukt: {ex.Message}");
        }
    }
}
