using System;
using System.Text.Json.Serialization;

namespace JellyfinUpcomingMedia.Models;

/// <summary>
/// Represents a single upcoming movie or show entry.
/// </summary>
public class UpcomingItem
{
    /// <summary>
    /// Unique internal identifier for this entry.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Title of the movie or show.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Short overview / description.
    /// </summary>
    public string Overview { get; set; } = string.Empty;

    /// <summary>
    /// TMDb ID for linking back to the source metadata.
    /// </summary>
    public int? TmdbId { get; set; }

    /// <summary>
    /// IMDb ID (if available).
    /// </summary>
    public string? ImdbId { get; set; }

    /// <summary>
    /// Whether this is a Movie or Series.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MediaType MediaType { get; set; } = MediaType.Movie;

    /// <summary>
    /// Full URL to the poster image.
    /// </summary>
    public string? PosterUrl { get; set; }

    /// <summary>
    /// Full URL to the backdrop/banner image.
    /// </summary>
    public string? BackdropUrl { get; set; }

    /// <summary>
    /// The date this media will be available on the server.
    /// </summary>
    public DateTime? AvailableDate { get; set; }

    /// <summary>
    /// The official release / air date from the metadata provider.
    /// </summary>
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// Custom message set by the admin, e.g. "Coming to our server March 15!"
    /// </summary>
    public string? CustomMessage { get; set; }

    /// <summary>
    /// Current status of this entry.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public UpcomingItemStatus Status { get; set; } = UpcomingItemStatus.ComingSoon;

    /// <summary>
    /// Genres (comma-separated or list).
    /// </summary>
    public string? Genres { get; set; }

    /// <summary>
    /// When this entry was added to the list.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// YouTube trailer URL (full URL or video ID).
    /// </summary>
    public string? TrailerUrl { get; set; }

    /// <summary>
    /// Sort order (lower = appears first). Default 0 = auto-sort by date.
    /// </summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// Path to the library folder created for this item (e.g. "D:\Movies\Movie (2026)").
    /// Created automatically when the item is added to the catalogue.
    /// </summary>
    public string? LibraryFolderPath { get; set; }

    /// <summary>
    /// Path to the dummy MKV file created in the Jellyfin library.
    /// </summary>
    public string? DummyFilePath { get; set; }

    /// <summary>
    /// Path to the real media file that will replace the dummy when available.
    /// </summary>
    public string? RealFilePath { get; set; }

    /// <summary>
    /// Whether a dummy file has been created for this item in the Jellyfin library.
    /// </summary>
    public bool DummyCreated { get; set; } = false;
}

/// <summary>
/// The type of media.
/// </summary>
public enum MediaType
{
    Movie,
    Series
}

/// <summary>
/// The lifecycle status of an upcoming item.
/// </summary>
public enum UpcomingItemStatus
{
    /// <summary>Not yet available — shown as "Coming Soon".</summary>
    ComingSoon,

    /// <summary>Now available on the server.</summary>
    Available,

    /// <summary>Past its window — hidden from the home page.</summary>
    Expired
}
