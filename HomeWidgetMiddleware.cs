using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace JellyfinUpcomingMedia;

/// <summary>
/// Injects a script tag into Jellyfin's index.html so the
/// Upcoming-Media home widget loads on every page.
/// Strips Accept-Encoding so the response body is readable UTF-8.
/// </summary>
public class HomeWidgetMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly string ScriptTag =
        $"<script src=\"/UpcomingMedia/HomeWidget?v={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}\" defer></script>";

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeWidgetMiddleware"/> class.
    /// </summary>
    public HomeWidgetMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Processes the HTTP request.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Intercept index.html and also bare /web/ or /web paths (reverse proxy / tunnel).
        var isIndexHtml = path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase);

        if (!isIndexHtml)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Strip Accept-Encoding so downstream won't compress the body.
        var savedEncoding = context.Request.Headers.AcceptEncoding;
        context.Request.Headers.AcceptEncoding = StringValues.Empty;

        // Capture the original response body.
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
            context.Request.Headers.AcceptEncoding = savedEncoding;
        }

        // Only touch successful text/html responses.
        // 304 Not Modified and other non-200 responses must pass through as-is.
        var statusCode = context.Response.StatusCode;
        var contentType = context.Response.ContentType ?? string.Empty;

        if (statusCode != 200
            || !contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            buffer.Seek(0, SeekOrigin.Begin);
            // 304 responses must NOT have a body written.
            if (statusCode != 304 && buffer.Length > 0)
            {
                await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            }

            return;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync()
            .ConfigureAwait(false);

        // Inject the widget script before </head> if not already present.
        if (!html.Contains("/UpcomingMedia/HomeWidget", StringComparison.Ordinal)
            && html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("</head>", ScriptTag + "\n</head>", StringComparison.OrdinalIgnoreCase);
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.Remove("Content-Encoding");
        // Prevent Cloudflare / reverse proxies from caching the non-injected version
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
    }
}