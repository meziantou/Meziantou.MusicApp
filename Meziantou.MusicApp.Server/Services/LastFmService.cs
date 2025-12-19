using System.Security.Cryptography;
using Meziantou.MusicApp.Server.Models;
using Microsoft.Extensions.Options;

namespace Meziantou.MusicApp.Server.Services;

public sealed class LastFmService
{
    private const string LastFmApiUrl = "https://ws.audioscrobbler.com/2.0/";
    private readonly LastFmSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<LastFmService> _logger;

    public LastFmService(IOptions<LastFmSettings> settings, IHttpClientFactory httpClientFactory, ILogger<LastFmService> logger)
    {
        _settings = settings.Value;
        _httpClient = httpClientFactory.CreateClient("LastFm");
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrEmpty(_settings.ApiKey) &&
        !string.IsNullOrEmpty(_settings.ApiSecret) &&
        !string.IsNullOrEmpty(_settings.SessionKey);

    /// <summary>Scrobbles a track to Last.fm (submission = true means the track was played, false means "now playing")</summary>
    public async Task<bool> ScrobbleAsync(Song song, bool submission, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("Last.fm is not configured, skipping scrobble");
            return false;
        }

        try
        {
            if (submission)
            {
                return await SubmitScrobbleAsync(song, cancellationToken);
            }
            else
            {
                return await UpdateNowPlayingAsync(song, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scrobble to Last.fm: {Title} by {Artist}", song.Title, song.Artist);
            return false;
        }
    }

    /// <summary>Updates the "Now Playing" status on Last.fm</summary>
    private async Task<bool> UpdateNowPlayingAsync(Song song, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.updateNowPlaying",
            ["artist"] = song.Artist,
            ["track"] = song.Title,
            ["api_key"] = _settings.ApiKey,
            ["sk"] = _settings.SessionKey,
        };

        if (!string.IsNullOrEmpty(song.Album))
        {
            parameters["album"] = song.Album;
        }

        if (song.Duration > 0)
        {
            parameters["duration"] = song.Duration.ToString(CultureInfo.InvariantCulture);
        }

        if (song.Track.HasValue)
        {
            parameters["trackNumber"] = song.Track.Value.ToString(CultureInfo.InvariantCulture);
        }

        parameters["api_sig"] = GenerateApiSignature(parameters);
        parameters["format"] = "json";

        using var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(LastFmApiUrl, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Updated Now Playing on Last.fm: {Title} by {Artist}", song.Title, song.Artist);
            return true;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Failed to update Now Playing on Last.fm: {StatusCode} - {Response}", response.StatusCode, responseBody);
        return false;
    }

    /// <summary>Submits a scrobble to Last.fm (track has finished playing)</summary>
    private async Task<bool> SubmitScrobbleAsync(Song song, CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["method"] = "track.scrobble",
            ["artist"] = song.Artist,
            ["track"] = song.Title,
            ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture),
            ["api_key"] = _settings.ApiKey,
            ["sk"] = _settings.SessionKey,
        };

        if (!string.IsNullOrEmpty(song.Album))
        {
            parameters["album"] = song.Album;
        }

        if (song.Duration > 0)
        {
            parameters["duration"] = song.Duration.ToString(CultureInfo.InvariantCulture);
        }

        if (song.Track.HasValue)
        {
            parameters["trackNumber"] = song.Track.Value.ToString(CultureInfo.InvariantCulture);
        }

        parameters["api_sig"] = GenerateApiSignature(parameters);
        parameters["format"] = "json";

        using var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync(LastFmApiUrl, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Scrobbled to Last.fm: {Title} by {Artist}", song.Title, song.Artist);
            return true;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Failed to scrobble to Last.fm: {StatusCode} - {Response}", response.StatusCode, responseBody);
        return false;
    }

    /// <summary>Generates the API signature required by Last.fm (MD5 is required by the Last.fm API)</summary>
    [SuppressMessage("Security", "CA5351:Do Not Use Broken Cryptographic Algorithms", Justification = "MD5 is required by Last.fm API")]
    private string GenerateApiSignature(Dictionary<string, string> parameters)
    {
        // Sort parameters alphabetically and concatenate
        var sortedParams = parameters
            .Where(p => p.Key != "format") // Exclude format from signature
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + p.Value);

        var signatureBase = string.Concat(sortedParams) + _settings.ApiSecret;

        // Generate MD5 hash (required by Last.fm API)
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(signatureBase));
        return Convert.ToHexStringLower(hash);
    }
}
