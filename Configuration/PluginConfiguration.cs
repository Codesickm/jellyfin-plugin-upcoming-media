using MediaBrowser.Model.Plugins;

namespace JellyfinUpcomingMedia.Configuration;

/// <summary>
/// Plugin configuration — stored automatically by Jellyfin as XML.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets whether to show the "Upcoming" section on the home screen.
    /// </summary>
    public bool ShowOnHomePage { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of upcoming items displayed on the home page.
    /// </summary>
    public int MaxItemsOnHomePage { get; set; } = 20;

    /// <summary>
    /// Gets or sets whether the scheduled reminder task is enabled.
    /// </summary>
    public bool EnableReminders { get; set; } = true;

    /// <summary>
    /// Gets or sets the TMDb API key (v3) for fetching trailers.
    /// Optional — trailers can also be entered manually.
    /// </summary>
    public string? TmdbApiKey { get; set; }

    /// <summary>
    /// Gets or sets the path to the Jellyfin library folder where dummy files are created.
    /// This should be a folder already registered as a Movie/TV library in Jellyfin.
    /// </summary>
    public string? UpcomingMediaLibraryPath { get; set; }
}
