using System;
using System.Linq;
using Jellyfin.Data.Enums;
using JellyfinUpcomingMedia.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Logging;

namespace JellyfinUpcomingMedia.Services;

/// <summary>
/// Scans the Jellyfin library to detect if upcoming "Coming Soon" items
/// have been added to the server, and auto-transitions them to "Available".
/// </summary>
public class LibraryScanService
{
    private readonly ILibraryManager _libraryManager;
    private readonly UpcomingItemStore _store;
    private readonly NotificationStore? _notificationStore;
    private readonly ILogger<LibraryScanService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryScanService"/> class.
    /// </summary>
    public LibraryScanService(
        ILibraryManager libraryManager,
        UpcomingItemStore store,
        NotificationStore notificationStore,
        ILogger<LibraryScanService> logger)
    {
        _libraryManager = libraryManager;
        _store = store;
        _notificationStore = notificationStore;
        _logger = logger;
    }

    /// <summary>
    /// Scans all "Coming Soon" items and checks if they exist in the Jellyfin library.
    /// Returns the number of items that were auto-transitioned to Available.
    /// </summary>
    public int ScanAndUpdate()
    {
        var comingSoon = _store.GetAll(UpcomingItemStatus.ComingSoon);
        if (comingSoon.Count == 0)
        {
            _logger.LogDebug("No Coming Soon items to scan.");
            return 0;
        }

        _logger.LogInformation("Scanning library for {Count} Coming Soon item(s)…", comingSoon.Count);
        var transitioned = 0;

        foreach (var upcoming in comingSoon)
        {
            try
            {
                var found = FindInLibrary(upcoming);
                if (found != null)
                {
                    _logger.LogInformation(
                        "Auto-detected '{Title}' in library (Jellyfin ID: {JellyfinId}). Transitioning to Available.",
                        upcoming.Title, found.Id);

                    // Update the item
                    upcoming.Status = UpcomingItemStatus.Available;
                    upcoming.AvailableDate ??= DateTime.UtcNow;
                    _store.Update(upcoming);

                    // Trigger notifications
                    _notificationStore?.MarkItemAvailable(upcoming.Id);

                    transitioned++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error scanning for '{Title}' in library.", upcoming.Title);
            }
        }

        if (transitioned > 0)
        {
            _logger.LogInformation("{Count} item(s) auto-transitioned to Available.", transitioned);
        }

        return transitioned;
    }

    /// <summary>
    /// Attempts to find the upcoming item in the Jellyfin library by TMDB ID, IMDB ID, or title.
    /// </summary>
    private BaseItem? FindInLibrary(UpcomingItem upcoming)
    {
        // Strategy 1: Match by TMDB provider ID (most reliable)
        if (upcoming.TmdbId.HasValue)
        {
            var tmdbMatch = FindByProviderId("Tmdb", upcoming.TmdbId.Value.ToString(), upcoming.MediaType);
            if (tmdbMatch != null) return tmdbMatch;
        }

        // Strategy 2: Match by IMDB provider ID
        if (!string.IsNullOrEmpty(upcoming.ImdbId))
        {
            var imdbMatch = FindByProviderId("Imdb", upcoming.ImdbId, upcoming.MediaType);
            if (imdbMatch != null) return imdbMatch;
        }

        // Strategy 3: Search by title (fuzzy fallback)
        var titleMatch = FindByTitle(upcoming.Title, upcoming.MediaType);
        if (titleMatch != null) return titleMatch;

        return null;
    }

    /// <summary>
    /// Searches for a library item by provider ID (e.g., Tmdb or Imdb).
    /// </summary>
    private BaseItem? FindByProviderId(string providerName, string providerId, Models.MediaType mediaType)
    {
        try
        {
            var query = new InternalItemsQuery
            {
                HasAnyProviderId = new Dictionary<string, string>
                {
                    { providerName, providerId }
                },
                Recursive = true,
                Limit = 1,
                IncludeItemTypes = mediaType == Models.MediaType.Series
                    ? new[] { BaseItemKind.Series }
                    : new[] { BaseItemKind.Movie }
            };

            var results = _libraryManager.GetItemList(query);
            return results.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Provider ID search failed for {Provider}:{Id}", providerName, providerId);
            return null;
        }
    }

    /// <summary>
    /// Searches for a library item by exact or near-exact title match.
    /// </summary>
    private BaseItem? FindByTitle(string title, Models.MediaType mediaType)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        try
        {
            var query = new InternalItemsQuery
            {
                SearchTerm = title,
                Recursive = true,
                Limit = 10,
                IncludeItemTypes = mediaType == Models.MediaType.Series
                    ? new[] { BaseItemKind.Series }
                    : new[] { BaseItemKind.Movie }
            };

            var results = _libraryManager.GetItemList(query);

            // Exact title match (case-insensitive)
            var exact = results.FirstOrDefault(r =>
                string.Equals(r.Name, title, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // Close match — title starts with or contains the search term
            var close = results.FirstOrDefault(r =>
                r.Name != null && r.Name.Contains(title, StringComparison.OrdinalIgnoreCase));

            return close;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Title search failed for '{Title}'", title);
            return null;
        }
    }
}
