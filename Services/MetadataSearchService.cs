using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpcomingMedia.Models;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace JellyfinUpcomingMedia.Services;

/// <summary>
/// Searches for movies and TV shows using Jellyfin's built-in metadata providers
/// (TMDb, etc.) — no external API key needed.
/// </summary>
public class MetadataSearchService
{
    private readonly IProviderManager _providerManager;
    private readonly ILogger<MetadataSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetadataSearchService"/> class.
    /// </summary>
    public MetadataSearchService(IProviderManager providerManager, ILogger<MetadataSearchService> logger)
    {
        _providerManager = providerManager;
        _logger = logger;
    }

    /// <summary>
    /// Searches for movies or TV shows using Jellyfin's provider infrastructure.
    /// </summary>
    /// <param name="query">The search text.</param>
    /// <param name="mediaType">"movie" or "tv".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of search results.</returns>
    public async Task<List<TmdbSearchResult>> SearchAsync(
        string query, string mediaType, CancellationToken cancellationToken = default)
    {
        IEnumerable<RemoteSearchResult> results;

        if (mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            var searchQuery = new RemoteSearchQuery<SeriesInfo>
            {
                SearchInfo = new SeriesInfo { Name = query },
                IncludeDisabledProviders = true
            };
            results = await _providerManager
                .GetRemoteSearchResults<Series, SeriesInfo>(searchQuery, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var searchQuery = new RemoteSearchQuery<MovieInfo>
            {
                SearchInfo = new MovieInfo { Name = query },
                IncludeDisabledProviders = true
            };
            results = await _providerManager
                .GetRemoteSearchResults<Movie, MovieInfo>(searchQuery, cancellationToken)
                .ConfigureAwait(false);
        }

        var output = results.Take(15).Select(r => MapResult(r, mediaType)).ToList();

        _logger.LogInformation(
            "Metadata search for '{Query}' ({Type}) returned {Count} results",
            query, mediaType, output.Count);

        return output;
    }

    /// <summary>
    /// Fetches details for a specific item by its TMDb ID using Jellyfin's providers.
    /// </summary>
    public async Task<TmdbSearchResult?> GetDetailsByTmdbIdAsync(
        int tmdbId, string mediaType, CancellationToken cancellationToken = default)
    {
        IEnumerable<RemoteSearchResult> results;

        if (mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase))
        {
            var searchQuery = new RemoteSearchQuery<SeriesInfo>
            {
                SearchInfo = new SeriesInfo
                {
                    ProviderIds = new Dictionary<string, string>
                    {
                        ["Tmdb"] = tmdbId.ToString()
                    }
                },
                IncludeDisabledProviders = true
            };
            results = await _providerManager
                .GetRemoteSearchResults<Series, SeriesInfo>(searchQuery, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var searchQuery = new RemoteSearchQuery<MovieInfo>
            {
                SearchInfo = new MovieInfo
                {
                    ProviderIds = new Dictionary<string, string>
                    {
                        ["Tmdb"] = tmdbId.ToString()
                    }
                },
                IncludeDisabledProviders = true
            };
            results = await _providerManager
                .GetRemoteSearchResults<Movie, MovieInfo>(searchQuery, cancellationToken)
                .ConfigureAwait(false);
        }

        var first = results.FirstOrDefault();
        if (first is null)
        {
            _logger.LogWarning("No details found for TMDb ID {TmdbId} ({Type})", tmdbId, mediaType);
            return null;
        }

        return MapResult(first, mediaType);
    }

    /// <summary>
    /// Maps a Jellyfin <see cref="RemoteSearchResult"/> to our <see cref="TmdbSearchResult"/> model.
    /// </summary>
    private static TmdbSearchResult MapResult(RemoteSearchResult r, string mediaType)
    {
        var isTv = mediaType.Equals("tv", StringComparison.OrdinalIgnoreCase);

        int tmdbId = 0;
        if (r.ProviderIds?.TryGetValue("Tmdb", out var tmdbStr) == true)
        {
            int.TryParse(tmdbStr, out tmdbId);
        }

        string? imdbId = null;
        r.ProviderIds?.TryGetValue("Imdb", out imdbId);

        DateTime? releaseDate = null;
        if (r.PremiereDate.HasValue)
        {
            releaseDate = r.PremiereDate.Value;
        }
        else if (r.ProductionYear.HasValue)
        {
            releaseDate = new DateTime(r.ProductionYear.Value, 1, 1);
        }

        return new TmdbSearchResult
        {
            TmdbId = tmdbId,
            Title = r.Name ?? "Unknown",
            Overview = r.Overview,
            PosterUrl = r.ImageUrl,
            BackdropUrl = null, // RemoteSearchResult only provides one image
            ReleaseDate = releaseDate,
            ImdbId = imdbId,
            MediaType = isTv ? Models.MediaType.Series : Models.MediaType.Movie,
            Genres = null // Not available from search results
        };
    }
}
