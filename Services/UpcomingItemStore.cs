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
/// Persists upcoming items as a JSON file in the plugin data directory.
/// </summary>
public class UpcomingItemStore
{
    private readonly string _dataFilePath;
    private readonly ILogger<UpcomingItemStore> _logger;
    private readonly object _lock = new();
    private List<UpcomingItem> _items;

    /// <summary>
    /// Optional reference to NotificationStore — set after DI resolves both singletons.
    /// </summary>
    public NotificationStore? NotificationStore { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public UpcomingItemStore(IApplicationPaths appPaths, ILogger<UpcomingItemStore> logger)
    {
        _logger = logger;

        var pluginDataDir = Path.Combine(appPaths.PluginConfigurationsPath, "UpcomingMedia");
        Directory.CreateDirectory(pluginDataDir);
        _dataFilePath = Path.Combine(pluginDataDir, "upcoming_items.json");

        _items = LoadFromDisk();
    }

    /// <summary>
    /// Gets all items, optionally filtered by status.
    /// </summary>
    public IReadOnlyList<UpcomingItem> GetAll(UpcomingItemStatus? statusFilter = null)
    {
        lock (_lock)
        {
            var query = _items.AsEnumerable();
            if (statusFilter.HasValue)
            {
                query = query.Where(i => i.Status == statusFilter.Value);
            }

            return query
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.AvailableDate ?? i.ReleaseDate ?? DateTime.MaxValue)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <summary>
    /// Gets a single item by its ID.
    /// </summary>
    public UpcomingItem? GetById(Guid id)
    {
        lock (_lock)
        {
            return _items.FirstOrDefault(i => i.Id == id);
        }
    }

    /// <summary>
    /// Adds a new upcoming item and persists to disk.
    /// </summary>
    public UpcomingItem Add(UpcomingItem item)
    {
        lock (_lock)
        {
            item.Id = Guid.NewGuid();
            item.CreatedAt = DateTime.UtcNow;
            _items.Add(item);
            SaveToDisk();
        }

        _logger.LogInformation("Added upcoming item: {Title} ({Id})", item.Title, item.Id);
        return item;
    }

    /// <summary>
    /// Updates an existing item. Returns null if not found.
    /// </summary>
    public UpcomingItem? Update(UpcomingItem updated)
    {
        lock (_lock)
        {
            var index = _items.FindIndex(i => i.Id == updated.Id);
            if (index < 0) return null;

            _items[index] = updated;
            SaveToDisk();
        }

        _logger.LogInformation("Updated upcoming item: {Title} ({Id})", updated.Title, updated.Id);
        return updated;
    }

    /// <summary>
    /// Deletes an item by ID. Returns true if found and removed.
    /// </summary>
    public bool Delete(Guid id)
    {
        lock (_lock)
        {
            var removed = _items.RemoveAll(i => i.Id == id);
            if (removed > 0)
            {
                SaveToDisk();
                _logger.LogInformation("Deleted upcoming item {Id}", id);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Optional reference to DummyFileService — set after DI resolves both singletons.
    /// </summary>
    public DummyFileService? DummyFileService { get; set; }

    /// <summary>
    /// Bulk-update statuses based on current date (used by the scheduled task).
    /// Also attempts to auto-swap dummy files when items become Available.
    /// </summary>
    public int UpdateStatuses()
    {
        var now = DateTime.UtcNow;
        var changed = 0;

        lock (_lock)
        {
            foreach (var item in _items)
            {
                var targetDate = item.AvailableDate ?? item.ReleaseDate;

                if (item.Status == UpcomingItemStatus.ComingSoon
                    && targetDate.HasValue
                    && targetDate.Value <= now)
                {
                    item.Status = UpcomingItemStatus.Available;
                    changed++;
                    // Trigger notifications for this item
                    NotificationStore?.MarkItemAvailable(item.Id);

                    // Auto-swap: activate .real file if present (works with or without dummy)
                    if (DummyFileService != null && !string.IsNullOrWhiteSpace(item.LibraryFolderPath))
                    {
                        try
                        {
                            // If no real file path or saved path no longer exists, re-scan for .real file
                            if (string.IsNullOrWhiteSpace(item.RealFilePath) || !File.Exists(item.RealFilePath))
                            {
                                item.RealFilePath = DummyFileService.FindRealFile(item);
                            }

                            if (!string.IsNullOrWhiteSpace(item.RealFilePath))
                            {
                                DummyFileService.SwapFiles(item);
                                _logger.LogInformation("Auto-activated .real file for '{Title}'", item.Title);
                            }
                            else
                            {
                                _logger.LogWarning("Item '{Title}' became Available but no .real file found in '{Folder}'.", item.Title, item.LibraryFolderPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Auto-swap failed for '{Title}'", item.Title);
                        }
                    }
                }
                else if (item.Status == UpcomingItemStatus.Available
                         && targetDate.HasValue
                         && targetDate.Value < now.AddDays(-30))
                {
                    item.Status = UpcomingItemStatus.Expired;
                    changed++;
                }
            }

            if (changed > 0)
            {
                SaveToDisk();
            }
        }

        return changed;
    }

    // ── Private helpers ──────────────────────────────────────────────

    private List<UpcomingItem> LoadFromDisk()
    {
        try
        {
            if (File.Exists(_dataFilePath))
            {
                var json = File.ReadAllText(_dataFilePath);
                return JsonSerializer.Deserialize<List<UpcomingItem>>(json, JsonOptions)
                       ?? new List<UpcomingItem>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load upcoming items from {Path}", _dataFilePath);
        }

        return new List<UpcomingItem>();
    }

    private void SaveToDisk()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items, JsonOptions);
            File.WriteAllText(_dataFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save upcoming items to {Path}", _dataFilePath);
        }
    }
}
