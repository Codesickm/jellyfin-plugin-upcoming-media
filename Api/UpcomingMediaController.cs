using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpcomingMedia.Models;
using JellyfinUpcomingMedia.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyfinUpcomingMedia.Api;

/// <summary>
/// REST API controller for the Upcoming Media plugin.
/// All admin-only endpoints require the "RequiresElevation" policy (Jellyfin admin).
/// The public list endpoint is available to any authenticated user.
/// </summary>
[ApiController]
[Route("UpcomingMedia")]
[Produces(MediaTypeNames.Application.Json)]
public class UpcomingMediaController : ControllerBase
{
    private readonly UpcomingItemStore _store;
    private readonly MetadataSearchService _searchService;
    private readonly NotificationStore _notificationStore;
    private readonly TmdbService _tmdbService;
    private readonly DummyFileService _dummyFileService;
    private readonly ILogger<UpcomingMediaController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpcomingMediaController"/> class.
    /// </summary>
    public UpcomingMediaController(
        UpcomingItemStore store,
        MetadataSearchService searchService,
        NotificationStore notificationStore,
        TmdbService tmdbService,
        DummyFileService dummyFileService,
        ILogger<UpcomingMediaController> logger)
    {
        _store = store;
        _searchService = searchService;
        _notificationStore = notificationStore;
        _tmdbService = tmdbService;
        _dummyFileService = dummyFileService;
        _logger = logger;

        // Wire up notification store so status changes trigger notifications
        _store.NotificationStore ??= _notificationStore;
        // Wire up dummy file service so auto-swap works during status transitions
        _store.DummyFileService ??= _dummyFileService;
    }

    // ══════════════════════════════════════════════════════════════
    //  PUBLIC (any authenticated user)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns all upcoming items visible to users (ComingSoon + Available).
    /// </summary>
    [HttpGet("Items")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetItems([FromQuery] string? status = null)
    {
        // Real-time status check — transitions happen the instant the time passes
        _store.UpdateStatuses();

        UpcomingItemStatus? filter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<UpcomingItemStatus>(status, true, out var parsed))
        {
            filter = parsed;
        }

        var items = _store.GetAll(filter);
        return Ok(items);
    }

    /// <summary>
    /// Returns a single upcoming item by ID.
    /// </summary>
    [HttpGet("Items/{id:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetItem([FromRoute] Guid id)
    {
        var item = _store.GetById(id);
        return item is null ? NotFound() : Ok(item);
    }

    // ══════════════════════════════════════════════════════════════
    //  ADMIN ONLY
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Searches for movies or TV shows using Jellyfin's built-in metadata providers.
    /// </summary>
    [HttpGet("Search")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery, Required] string query,
        [FromQuery] string type = "movie",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = await _searchService.SearchAsync(query, type, cancellationToken);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata search failed for query '{Query}'", query);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "Metadata search failed.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Fetches full details from metadata providers and adds the item to the upcoming list.
    /// </summary>
    [HttpPost("AddFromTmdb")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> AddFromTmdb(
        [FromQuery, Required] int tmdbId,
        [FromQuery] string type = "movie",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var details = await _searchService.GetDetailsByTmdbIdAsync(tmdbId, type, cancellationToken);
            if (details is null)
            {
                return BadRequest(new { error = "Could not fetch details from metadata providers." });
            }

            var item = new UpcomingItem
            {
                Title = details.Title,
                Overview = details.Overview ?? string.Empty,
                TmdbId = details.TmdbId,
                ImdbId = details.ImdbId,
                MediaType = details.MediaType,
                PosterUrl = details.PosterUrl,
                BackdropUrl = details.BackdropUrl,
                ReleaseDate = details.ReleaseDate,
                AvailableDate = details.ReleaseDate,
                Genres = details.Genres,
                Status = UpcomingItemStatus.ComingSoon
            };

            // Try to auto-fetch trailer from TMDb if API key is configured
            var apiKey = Plugin.Instance?.Configuration.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey) && item.TmdbId.HasValue)
            {
                try
                {
                    item.TrailerUrl = await _tmdbService.FetchTrailerUrlAsync(
                        apiKey, item.TmdbId.Value,
                        type, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception tex)
                {
                    _logger.LogWarning(tex, "Failed to fetch trailer for TMDb ID {TmdbId}", tmdbId);
                }
            }

            var created = _store.Add(item);

            // Auto-create library folder if library path is configured
            var libraryPath = Plugin.Instance?.Configuration.UpcomingMediaLibraryPath;
            if (!string.IsNullOrWhiteSpace(libraryPath))
            {
                var folderPath = _dummyFileService.CreateLibraryFolder(created, libraryPath);
                if (folderPath != null)
                {
                    created.LibraryFolderPath = folderPath;
                    _store.Update(created);
                    _logger.LogInformation("Auto-created library folder for '{Title}': {Path}", created.Title, folderPath);
                }
            }

            return CreatedAtAction(nameof(GetItem), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add item from metadata (TMDb ID {TmdbId})", tmdbId);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "Metadata request failed.", detail = ex.Message });
        }
    }

    /// <summary>
    /// Manually creates a new upcoming item (without TMDb lookup).
    /// </summary>
    [HttpPost("Items")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public IActionResult CreateItem([FromBody] UpcomingItem item)
    {
        var created = _store.Add(item);

        // Auto-create library folder if library path is configured
        var libraryPath = Plugin.Instance?.Configuration.UpcomingMediaLibraryPath;
        if (!string.IsNullOrWhiteSpace(libraryPath))
        {
            var folderPath = _dummyFileService.CreateLibraryFolder(created, libraryPath);
            if (folderPath != null)
            {
                created.LibraryFolderPath = folderPath;
                _store.Update(created);
                _logger.LogInformation("Auto-created library folder for '{Title}': {Path}", created.Title, folderPath);
            }
        }

        return CreatedAtAction(nameof(GetItem), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates an existing upcoming item.
    /// </summary>
    [HttpPut("Items/{id:guid}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult UpdateItem([FromRoute] Guid id, [FromBody] UpcomingItem item)
    {
        // Check if status is changing to Available so we can trigger notifications
        var existing = _store.GetById(id);
        item.Id = id;
        var updated = _store.Update(item);
        if (updated is null) return NotFound();

        // If status changed to Available, trigger notifications
        if (existing != null
            && existing.Status != UpcomingItemStatus.Available
            && updated.Status == UpcomingItemStatus.Available)
        {
            _notificationStore.MarkItemAvailable(id);
        }

        return Ok(updated);
    }

    /// <summary>
    /// Deletes an upcoming item. Also cleans up any dummy file.
    /// </summary>
    [HttpDelete("Items/{id:guid}")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteItem([FromRoute] Guid id)
    {
        var item = _store.GetById(id);
        if (item != null)
        {
            // Clean up dummy file
            if (item.DummyCreated && !string.IsNullOrWhiteSpace(item.DummyFilePath))
            {
                _dummyFileService.DeleteDummyFile(item.DummyFilePath);
            }

            // Clean up empty library folder
            if (!string.IsNullOrWhiteSpace(item.LibraryFolderPath)
                && System.IO.Directory.Exists(item.LibraryFolderPath)
                && !System.IO.Directory.EnumerateFileSystemEntries(item.LibraryFolderPath).Any())
            {
                try
                {
                    System.IO.Directory.Delete(item.LibraryFolderPath);
                    _logger.LogInformation("Removed empty library folder: {Path}", item.LibraryFolderPath);
                }
                catch { /* best effort */ }
            }
        }

        return _store.Delete(id) ? NoContent() : NotFound();
    }

    // ══════════════════════════════════════════════════════════════
    //  DUMMY FILE MANAGEMENT (admin only)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a dummy MKV file in the Jellyfin library for the given item.
    /// Jellyfin will automatically pick it up during the next library scan
    /// and fetch full metadata (poster, backdrop, cast, etc.).
    /// </summary>
    [HttpPost("Items/{id:guid}/CreateDummy")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult CreateDummy([FromRoute] Guid id)
    {
        var item = _store.GetById(id);
        if (item is null) return NotFound();

        if (item.DummyCreated)
            return Ok(new { dummyFilePath = item.DummyFilePath, message = "Dummy file already exists." });

        var libraryPath = Plugin.Instance?.Configuration.UpcomingMediaLibraryPath;
        if (string.IsNullOrWhiteSpace(libraryPath))
            return BadRequest(new { error = "Library path is not configured in plugin settings." });

        var dummyPath = _dummyFileService.CreateDummyFile(item, libraryPath);
        if (dummyPath is null)
            return BadRequest(new { error = "Failed to create dummy file. Check logs and library path." });

        item.DummyFilePath = dummyPath;
        item.DummyCreated = true;
        item.LibraryFolderPath ??= System.IO.Path.GetDirectoryName(dummyPath);
        _store.Update(item);

        _logger.LogInformation("Dummy file created for '{Title}': {Path}", item.Title, dummyPath);
        return Ok(new { dummyFilePath = dummyPath, message = "Dummy file created. Run a Jellyfin library scan to pick it up." });
    }

    /// <summary>
    /// Deletes the dummy file for an upcoming item.
    /// </summary>
    [HttpDelete("Items/{id:guid}/Dummy")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult DeleteDummy([FromRoute] Guid id)
    {
        var item = _store.GetById(id);
        if (item is null) return NotFound();

        if (!item.DummyCreated)
            return Ok(new { message = "No dummy file to delete." });

        _dummyFileService.DeleteDummyFile(item.DummyFilePath);
        item.DummyFilePath = null;
        item.DummyCreated = false;
        _store.Update(item);

        return Ok(new { message = "Dummy file deleted." });
    }

    /// <summary>
    /// Manually triggers the file swap: removes the .real extension from the real file.
    /// If a dummy file exists, it is deleted first.
    /// </summary>
    [HttpPost("Items/{id:guid}/SwapFile")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SwapFile([FromRoute] Guid id)
    {
        var item = _store.GetById(id);
        if (item is null) return NotFound();

        // Auto-detect the .real file if not set or the saved path no longer exists
        if (string.IsNullOrWhiteSpace(item.RealFilePath) || !System.IO.File.Exists(item.RealFilePath))
        {
            var found = _dummyFileService.FindRealFile(item);
            if (found != null)
            {
                item.RealFilePath = found;
            }
            else
            {
                item.RealFilePath = null; // clear stale path
            }
        }

        if (string.IsNullOrWhiteSpace(item.RealFilePath))
            return BadRequest(new { error = "No .real file found. Drop your movie file into the item's library folder and rename it to end with .real" });

        var success = _dummyFileService.SwapFiles(item);
        if (!success)
            return BadRequest(new { error = "File swap failed. Check server logs." });

        _store.Update(item);
        _logger.LogInformation("File swap completed for '{Title}'", item.Title);
        return Ok(new { message = "File activated! Run a library scan for Jellyfin to pick it up.", realFilePath = item.RealFilePath });
    }

    /// <summary>
    /// Scans the item's library folder for a .real file.
    /// Does not perform the swap — just returns what was found.
    /// </summary>
    [HttpGet("Items/{id:guid}/ScanRealFile")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult ScanRealFile([FromRoute] Guid id)
    {
        var item = _store.GetById(id);
        if (item is null) return NotFound();

        if (string.IsNullOrWhiteSpace(item.LibraryFolderPath) && string.IsNullOrWhiteSpace(item.DummyFilePath))
            return Ok(new { found = false, message = "No library folder exists yet. Add the item again or create a dummy file first." });

        var foundPath = _dummyFileService.FindRealFile(item);
        var folder = item.LibraryFolderPath ?? System.IO.Path.GetDirectoryName(item.DummyFilePath);
        return Ok(new
        {
            found = foundPath != null,
            realFilePath = foundPath,
            libraryFolder = folder,
            message = foundPath != null
                ? "Found .real file ready to activate."
                : $"No .real file found. Drop your movie in: {folder} and add .real to the filename."
        });
    }

    /// <summary>
    /// Fetches the YouTube trailer URL from TMDb for an existing upcoming item.
    /// Requires the TMDb API key to be configured in plugin settings.
    /// </summary>
    [HttpPost("Items/{id:guid}/FetchTrailer")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> FetchTrailer(
        [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var item = _store.GetById(id);
        if (item is null) return NotFound();

        if (!item.TmdbId.HasValue)
            return BadRequest(new { error = "Item has no TMDb ID — cannot fetch trailer." });

        var apiKey = Plugin.Instance?.Configuration.TmdbApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return BadRequest(new { error = "TMDb API key is not configured in plugin settings." });

        var mediaTypeStr = item.MediaType == Models.MediaType.Series ? "tv" : "movie";

        try
        {
            var trailerUrl = await _tmdbService.FetchTrailerUrlAsync(
                apiKey, item.TmdbId.Value, mediaTypeStr, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(trailerUrl))
                return Ok(new { trailerUrl = (string?)null, message = "No trailer found on TMDb." });

            item.TrailerUrl = trailerUrl;
            _store.Update(item);

            return Ok(new { trailerUrl, message = "Trailer fetched and saved." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch trailer for item {Id}", id);
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "Failed to fetch trailer.", detail = ex.Message });
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  HOME WIDGET SCRIPT (anonymous – needed before auth on SPA)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Subscribe the current user to notifications for a Coming Soon item.
    /// </summary>
    [HttpPost("Notifications/Subscribe/{itemId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult Subscribe([FromRoute] Guid itemId)
    {
        var item = _store.GetById(itemId);
        if (item is null) return NotFound();

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        _notificationStore.Subscribe(userId, itemId, item.Title);
        return Ok(new { subscribed = true, itemId });
    }

    /// <summary>
    /// Unsubscribe the current user from notifications for an item.
    /// </summary>
    [HttpDelete("Notifications/Subscribe/{itemId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Unsubscribe([FromRoute] Guid itemId)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        _notificationStore.Unsubscribe(userId, itemId);
        return Ok(new { subscribed = false, itemId });
    }

    /// <summary>
    /// Get all item IDs the current user is subscribed to.
    /// </summary>
    [HttpGet("Notifications/Subscriptions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetSubscriptions()
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var subs = _notificationStore.GetUserSubscriptions(userId);
        return Ok(subs);
    }

    /// <summary>
    /// Get pending notifications for the current user (items that became Available).
    /// </summary>
    [HttpGet("Notifications/Pending")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetPendingNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var pending = _notificationStore.GetPendingNotifications(userId);
        return Ok(pending);
    }

    /// <summary>
    /// Dismiss a specific notification.
    /// </summary>
    [HttpPost("Notifications/Dismiss/{itemId:guid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DismissNotification([FromRoute] Guid itemId)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        _notificationStore.DismissNotification(userId, itemId);
        return Ok(new { dismissed = true });
    }

    /// <summary>
    /// Dismiss all pending notifications for the current user.
    /// </summary>
    [HttpPost("Notifications/DismissAll")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DismissAllNotifications()
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        _notificationStore.DismissAll(userId);
        return Ok(new { dismissed = true });
    }

    private Guid GetCurrentUserId()
    {
        // Jellyfin puts the user ID in the claims
        var claim = User.FindFirst("Jellyfin-UserId")
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (claim != null && Guid.TryParse(claim.Value, out var uid))
        {
            return uid;
        }
        return Guid.Empty;
    }

    /// <summary>
    /// Serves the home-page widget JavaScript file.
    /// </summary>
    [HttpGet("HomeWidget")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetHomeWidget()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "JellyfinUpcomingMedia.Pages.homeWidget.js";
        var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
        {
            _logger.LogWarning("Embedded resource {Resource} not found", resourceName);
            return NotFound("Widget script not found.");
        }

        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        return File(stream, "application/javascript");
    }
}
