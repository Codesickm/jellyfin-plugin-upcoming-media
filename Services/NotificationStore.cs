using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using JellyfinUpcomingMedia.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace JellyfinUpcomingMedia.Services;

/// <summary>
/// Persists notification subscriptions as a JSON file.
/// When an item becomes Available, marks matching subscriptions as notified.
/// Users can then poll for pending notifications.
/// </summary>
public class NotificationStore
{
    private readonly string _dataFilePath;
    private readonly ILogger<NotificationStore> _logger;
    private readonly object _lock = new();
    private List<NotificationSubscription> _subs;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationStore"/> class.
    /// </summary>
    public NotificationStore(IApplicationPaths appPaths, ILogger<NotificationStore> logger)
    {
        _logger = logger;
        var pluginDataDir = Path.Combine(appPaths.PluginConfigurationsPath, "UpcomingMedia");
        Directory.CreateDirectory(pluginDataDir);
        _dataFilePath = Path.Combine(pluginDataDir, "notification_subscriptions.json");
        _subs = LoadFromDisk();
    }

    /// <summary>
    /// Subscribe a user to notifications for an upcoming item.
    /// </summary>
    public void Subscribe(Guid userId, Guid itemId, string itemTitle)
    {
        lock (_lock)
        {
            // Don't duplicate
            if (_subs.Any(s => s.UserId == userId && s.ItemId == itemId))
            {
                return;
            }

            _subs.Add(new NotificationSubscription
            {
                UserId = userId,
                ItemId = itemId,
                ItemTitle = itemTitle,
                SubscribedAt = DateTime.UtcNow
            });
            SaveToDisk();
            _logger.LogInformation("User {UserId} subscribed to notifications for '{Title}' ({ItemId})", userId, itemTitle, itemId);
        }
    }

    /// <summary>
    /// Unsubscribe a user from notifications for an item.
    /// </summary>
    public bool Unsubscribe(Guid userId, Guid itemId)
    {
        lock (_lock)
        {
            var removed = _subs.RemoveAll(s => s.UserId == userId && s.ItemId == itemId);
            if (removed > 0)
            {
                SaveToDisk();
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Check if a user is subscribed to a specific item.
    /// </summary>
    public bool IsSubscribed(Guid userId, Guid itemId)
    {
        lock (_lock)
        {
            return _subs.Any(s => s.UserId == userId && s.ItemId == itemId);
        }
    }

    /// <summary>
    /// Get all item IDs a user is subscribed to.
    /// </summary>
    public IReadOnlyList<Guid> GetUserSubscriptions(Guid userId)
    {
        lock (_lock)
        {
            return _subs
                .Where(s => s.UserId == userId)
                .Select(s => s.ItemId)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Called when an item's status changes to Available.
    /// Marks all subscriptions for that item as Notified.
    /// </summary>
    public void MarkItemAvailable(Guid itemId)
    {
        lock (_lock)
        {
            var affected = _subs.Where(s => s.ItemId == itemId && !s.Notified).ToList();
            foreach (var sub in affected)
            {
                sub.Notified = true;
                sub.NotifiedAt = DateTime.UtcNow;
            }
            if (affected.Count > 0)
            {
                SaveToDisk();
                _logger.LogInformation("Marked {Count} subscriptions as notified for item {ItemId}", affected.Count, itemId);
            }
        }
    }

    /// <summary>
    /// Get pending (notified but not dismissed) notifications for a user.
    /// </summary>
    public IReadOnlyList<NotificationSubscription> GetPendingNotifications(Guid userId)
    {
        lock (_lock)
        {
            return _subs
                .Where(s => s.UserId == userId && s.Notified && !s.Dismissed)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Dismiss (acknowledge) a notification for a user.
    /// </summary>
    public void DismissNotification(Guid userId, Guid itemId)
    {
        lock (_lock)
        {
            var sub = _subs.FirstOrDefault(s => s.UserId == userId && s.ItemId == itemId);
            if (sub != null)
            {
                sub.Dismissed = true;
                SaveToDisk();
            }
        }
    }

    /// <summary>
    /// Dismiss all pending notifications for a user.
    /// </summary>
    public void DismissAll(Guid userId)
    {
        lock (_lock)
        {
            var pending = _subs.Where(s => s.UserId == userId && s.Notified && !s.Dismissed);
            foreach (var sub in pending)
            {
                sub.Dismissed = true;
            }
            SaveToDisk();
        }
    }

    private List<NotificationSubscription> LoadFromDisk()
    {
        try
        {
            if (File.Exists(_dataFilePath))
            {
                var json = File.ReadAllText(_dataFilePath);
                return JsonSerializer.Deserialize<List<NotificationSubscription>>(json, JsonOptions)
                       ?? new List<NotificationSubscription>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load notification subscriptions from {Path}", _dataFilePath);
        }
        return new List<NotificationSubscription>();
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_subs, JsonOptions);
            File.WriteAllText(_dataFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save notification subscriptions to {Path}", _dataFilePath);
        }
    }
}
