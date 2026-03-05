using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpcomingMedia.Models;
using Microsoft.Extensions.Logging;

namespace JellyfinUpcomingMedia.Services;

/// <summary>
/// Fetches movie and TV show metadata from The Movie Database (TMDb) API v3.
/// </summary>
public class TmdbService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbService> _logger;

    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const string ImageBase = "https://image.tmdb.org/t/p";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TmdbService(IHttpClientFactory httpClientFactory, ILogger<TmdbService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Searches TMDb for movies or TV shows matching the query.
    /// </summary>
    /// <param name="apiKey">TMDb API key (v3).</param>
    /// <param name="query">Search text.</param>
    /// <param name="mediaType">"movie" or "tv".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of search results.</returns>
    public async Task<List<TmdbSearchResult>> SearchAsync(
        string apiKey, string query, string mediaType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("TMDb API key is not configured.");

        var endpoint = mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase)
            ? "search/tv"
            : "search/movie";

        var url = $"{BaseUrl}/{endpoint}?api_key={apiKey}&query={Uri.EscapeDataString(query)}&page=1";

        var client = _httpClientFactory.CreateClient("TmdbClient");
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var results = doc.RootElement.GetProperty("results");

        var output = new List<TmdbSearchResult>();

        foreach (var item in results.EnumerateArray().Take(15))
        {
            var result = new TmdbSearchResult
            {
                TmdbId = item.GetProperty("id").GetInt32(),
                Overview = GetStringOrNull(item, "overview")
            };

            if (mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase))
            {
                result.Title = GetStringOrNull(item, "name") ?? "Unknown";
                result.ReleaseDate = ParseDate(GetStringOrNull(item, "first_air_date"));
                result.MediaType = Models.MediaType.Series;
            }
            else
            {
                result.Title = GetStringOrNull(item, "title") ?? "Unknown";
                result.ReleaseDate = ParseDate(GetStringOrNull(item, "release_date"));
                result.MediaType = Models.MediaType.Movie;
            }

            var posterPath = GetStringOrNull(item, "poster_path");
            result.PosterUrl = posterPath != null ? $"{ImageBase}/w342{posterPath}" : null;

            var backdropPath = GetStringOrNull(item, "backdrop_path");
            result.BackdropUrl = backdropPath != null ? $"{ImageBase}/w780{backdropPath}" : null;

            // Genre IDs → names (simple mapping for the most common ones)
            if (item.TryGetProperty("genre_ids", out var genreIds))
            {
                var genres = genreIds.EnumerateArray()
                    .Select(g => MapGenre(g.GetInt32()))
                    .Where(g => g != null)
                    .ToList();
                result.Genres = genres.Count > 0 ? string.Join(", ", genres) : null;
            }

            output.Add(result);
        }

        _logger.LogInformation("TMDb search for '{Query}' ({Type}) returned {Count} results",
            query, mediaType, output.Count);

        return output;
    }

    /// <summary>
    /// Fetches full details for a single movie or TV show from TMDb.
    /// </summary>
    public async Task<TmdbSearchResult?> GetDetailsAsync(
        string apiKey, int tmdbId, string mediaType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("TMDb API key is not configured.");

        var endpoint = mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase)
            ? $"tv/{tmdbId}"
            : $"movie/{tmdbId}";

        var url = $"{BaseUrl}/{endpoint}?api_key={apiKey}&append_to_response=external_ids,videos";

        var client = _httpClientFactory.CreateClient("TmdbClient");
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("TMDb details request failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement;

        var result = new TmdbSearchResult
        {
            TmdbId = tmdbId,
            Overview = GetStringOrNull(item, "overview")
        };

        if (mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            result.Title = GetStringOrNull(item, "name") ?? "Unknown";
            result.ReleaseDate = ParseDate(GetStringOrNull(item, "first_air_date"));
            result.MediaType = Models.MediaType.Series;
        }
        else
        {
            result.Title = GetStringOrNull(item, "title") ?? "Unknown";
            result.ReleaseDate = ParseDate(GetStringOrNull(item, "release_date"));
            result.MediaType = Models.MediaType.Movie;
        }

        var posterPath = GetStringOrNull(item, "poster_path");
        result.PosterUrl = posterPath != null ? $"{ImageBase}/w342{posterPath}" : null;

        var backdropPath = GetStringOrNull(item, "backdrop_path");
        result.BackdropUrl = backdropPath != null ? $"{ImageBase}/w780{backdropPath}" : null;

        // Genres from detail endpoint come as objects with name property
        if (item.TryGetProperty("genres", out var genresArr))
        {
            var names = genresArr.EnumerateArray()
                .Select(g => GetStringOrNull(g, "name"))
                .Where(n => n != null)
                .ToList();
            result.Genres = names.Count > 0 ? string.Join(", ", names) : null;
        }

        // External IDs
        if (item.TryGetProperty("external_ids", out var extIds))
        {
            result.ImdbId = GetStringOrNull(extIds, "imdb_id");
        }
        else if (item.TryGetProperty("imdb_id", out var imdbProp))
        {
            result.ImdbId = imdbProp.GetString();
        }

        // Trailer — pick the best YouTube trailer from the videos response
        result.TrailerUrl = ExtractBestTrailer(item);

        return result;
    }

    /// <summary>
    /// Fetches just the trailer URL for a movie or TV show from TMDb.
    /// Useful for back-filling trailer data on existing items.
    /// </summary>
    public async Task<string?> FetchTrailerUrlAsync(
        string apiKey, int tmdbId, string mediaType, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var endpoint = mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase)
            ? $"tv/{tmdbId}/videos"
            : $"movie/{tmdbId}/videos";

        var url = $"{BaseUrl}/{endpoint}?api_key={apiKey}";

        var client = _httpClientFactory.CreateClient("TmdbClient");
        var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return ExtractBestTrailerFromVideos(doc.RootElement);
    }

    /// <summary>
    /// Extracts the best YouTube trailer URL from a TMDb detail response (with appended videos).
    /// </summary>
    private static string? ExtractBestTrailer(JsonElement item)
    {
        if (!item.TryGetProperty("videos", out var videos)) return null;
        return ExtractBestTrailerFromVideos(videos);
    }

    /// <summary>
    /// Extracts the best YouTube trailer from a TMDb videos response object.
    /// Priority: Official Trailer → any Trailer → any Teaser, all YouTube only.
    /// </summary>
    private static string? ExtractBestTrailerFromVideos(JsonElement videos)
    {
        if (!videos.TryGetProperty("results", out var results)) return null;

        string? officialTrailer = null;
        string? anyTrailer = null;
        string? anyTeaser = null;

        foreach (var v in results.EnumerateArray())
        {
            var site = GetStringOrNull(v, "site");
            if (site == null || !site.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                continue;

            var type = GetStringOrNull(v, "type") ?? "";
            var name = GetStringOrNull(v, "name") ?? "";
            var key = GetStringOrNull(v, "key");
            if (string.IsNullOrEmpty(key)) continue;

            var ytUrl = $"https://www.youtube.com/watch?v={key}";

            if (type.Equals("Trailer", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("Official", StringComparison.OrdinalIgnoreCase))
                {
                    officialTrailer ??= ytUrl;
                }

                anyTrailer ??= ytUrl;
            }
            else if (type.Equals("Teaser", StringComparison.OrdinalIgnoreCase))
            {
                anyTeaser ??= ytUrl;
            }
        }

        return officialTrailer ?? anyTrailer ?? anyTeaser;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string? GetStringOrNull(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }
        return null;
    }

    private static DateTime? ParseDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return null;
        return DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }

    /// <summary>
    /// Maps common TMDb genre IDs to names (for search results that only have IDs).
    /// </summary>
    private static string? MapGenre(int id) => id switch
    {
        28 => "Action",
        12 => "Adventure",
        16 => "Animation",
        35 => "Comedy",
        80 => "Crime",
        99 => "Documentary",
        18 => "Drama",
        10751 => "Family",
        14 => "Fantasy",
        36 => "History",
        27 => "Horror",
        10402 => "Music",
        9648 => "Mystery",
        10749 => "Romance",
        878 => "Sci-Fi",
        10770 => "TV Movie",
        53 => "Thriller",
        10752 => "War",
        37 => "Western",
        // TV genres
        10759 => "Action & Adventure",
        10762 => "Kids",
        10763 => "News",
        10764 => "Reality",
        10765 => "Sci-Fi & Fantasy",
        10766 => "Soap",
        10767 => "Talk",
        10768 => "War & Politics",
        _ => null
    };
}
