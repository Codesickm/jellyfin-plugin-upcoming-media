using JellyfinUpcomingMedia.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace JellyfinUpcomingMedia;

/// <summary>
/// Registers plugin services into the Jellyfin DI container.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IServerApplicationHost appHost)
    {
        services.AddSingleton<UpcomingItemStore>();
        services.AddSingleton<MetadataSearchService>();
        services.AddSingleton<NotificationStore>();
        services.AddSingleton<LibraryScanService>();
        services.AddSingleton<TmdbService>();
        services.AddSingleton<DummyFileService>();
        services.AddSingleton<IStartupFilter, UpcomingMediaStartupFilter>();
    }
}

/// <summary>
/// Inserts the <see cref="HomeWidgetMiddleware"/> into the ASP.NET Core pipeline
/// so it can inject the widget script into index.html.
/// </summary>
public class UpcomingMediaStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return builder =>
        {
            builder.UseMiddleware<HomeWidgetMiddleware>();
            next(builder);
        };
    }
}
