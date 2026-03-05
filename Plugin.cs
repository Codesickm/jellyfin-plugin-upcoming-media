using System;
using System.Collections.Generic;
using JellyfinUpcomingMedia.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace JellyfinUpcomingMedia;

/// <summary>
/// The main plugin entry point for Upcoming Media.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Plugin unique identifier.
    /// </summary>
    public static readonly Guid PluginId = new("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Upcoming Media";

    /// <inheritdoc />
    public override string Description =>
        "Manage and display upcoming movies & shows with metadata from TMDb. "
        + "Admins can set release dates, reminders, and 'Coming Soon' labels.";

    /// <inheritdoc />
    public override Guid Id => PluginId;

    /// <summary>
    /// Returns the web pages served by this plugin.
    /// </summary>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return new[]
        {
            // Admin config page (HTML + JS)
            new PluginPageInfo
            {
                Name = "UpcomingMediaConfigPage",
                EmbeddedResourcePath = $"{ns}.Pages.configPage.html"
            },
            new PluginPageInfo
            {
                Name = "UpcomingMediaConfigPageJS",
                EmbeddedResourcePath = $"{ns}.Pages.configPage.js"
            },
            // Public upcoming page (HTML + JS)
            new PluginPageInfo
            {
                Name = "UpcomingMediaPage",
                EmbeddedResourcePath = $"{ns}.Pages.upcomingPage.html"
            },
            new PluginPageInfo
            {
                Name = "UpcomingMediaPageJS",
                EmbeddedResourcePath = $"{ns}.Pages.upcomingPage.js"
            }
        };
    }
}
