using System.Text.Json.Serialization;

namespace JellyfinUpcomingMedia.Models;

/// <summary>
/// Lightweight result returned from a TMDb search.
/// </summary>
public class TmdbSearchResult
{
    public int TmdbId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Overview { get; set; }

    public string? PosterUrl { get; set; }

    public string? BackdropUrl { get; set; }

    public DateTime? ReleaseDate { get; set; }

    public string? Genres { get; set; }

    public string? ImdbId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MediaType MediaType { get; set; }

    /// <summary>
    /// YouTube trailer URL.
    /// </summary>
    public string? TrailerUrl { get; set; }
}
