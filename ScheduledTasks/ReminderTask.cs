using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JellyfinUpcomingMedia.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace JellyfinUpcomingMedia.ScheduledTasks;

/// <summary>
/// Jellyfin scheduled task that automatically transitions upcoming items
/// from ComingSoon → Available → Expired based on their dates,
/// and scans the library to auto-detect newly added items.
/// Runs every 6 hours by default.
/// </summary>
public class ReminderTask : IScheduledTask
{
    private readonly UpcomingItemStore _store;
    private readonly LibraryScanService _libraryScan;
    private readonly NotificationStore _notificationStore;
    private readonly DummyFileService _dummyFileService;
    private readonly ILogger<ReminderTask> _logger;

    public ReminderTask(UpcomingItemStore store, LibraryScanService libraryScan, NotificationStore notificationStore, DummyFileService dummyFileService, ILogger<ReminderTask> logger)
    {
        _store = store;
        _libraryScan = libraryScan;
        _notificationStore = notificationStore;
        _dummyFileService = dummyFileService;
        _logger = logger;

        // Ensure notification store is always wired up so status transitions trigger notifications
        _store.NotificationStore ??= _notificationStore;
        // Ensure dummy file service is wired up for auto-swap
        _store.DummyFileService ??= _dummyFileService;
    }

    /// <inheritdoc />
    public string Name => "Upcoming Media — Update Statuses";

    /// <inheritdoc />
    public string Key => "UpcomingMediaReminderTask";

    /// <inheritdoc />
    public string Description =>
        "Checks upcoming media dates and transitions items from 'Coming Soon' to 'Available' "
        + "when their date arrives, and from 'Available' to 'Expired' after 30 days.";

    /// <inheritdoc />
    public string Category => "Upcoming Media";

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => Plugin.Instance?.Configuration.EnableReminders ?? true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Upcoming Media reminder task starting…");
        progress.Report(0);

        if (!(Plugin.Instance?.Configuration.EnableReminders ?? true))
        {
            _logger.LogInformation("Reminders are disabled in plugin configuration. Skipping.");
            progress.Report(100);
            return Task.CompletedTask;
        }

        // Step 1: Date-based status transitions
        var changed = _store.UpdateStatuses();
        _logger.LogInformation("{Count} item(s) updated by date-based transition.", changed);
        progress.Report(50);

        // Step 2: Library scan — auto-detect items added to Jellyfin
        try
        {
            var detected = _libraryScan.ScanAndUpdate();
            _logger.LogInformation("{Count} item(s) auto-detected in library.", detected);
            changed += detected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Library scan failed, continuing with date-based transitions only.");
        }

        _logger.LogInformation(
            "Upcoming Media reminder task completed. {Count} total item(s) updated.", changed);

        progress.Report(100);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // Run every 6 hours
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(6).Ticks
            }
        };
    }
}
