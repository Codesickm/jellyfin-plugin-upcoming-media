using System;
using System.Text.Json.Serialization;

namespace JellyfinUpcomingMedia.Models;

/// <summary>
/// A user's subscription to be notified when a Coming Soon item becomes Available.
/// </summary>
public class NotificationSubscription
{
    /// <summary>
    /// The Jellyfin user ID who subscribed.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The upcoming item ID they want to be notified about.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// The title of the item (for display in the notification).
    /// </summary>
    public string ItemTitle { get; set; } = string.Empty;

    /// <summary>
    /// When the subscription was created.
    /// </summary>
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the user has been notified (item became Available).
    /// </summary>
    public bool Notified { get; set; } = false;

    /// <summary>
    /// When the notification was triggered (item became Available).
    /// </summary>
    public DateTime? NotifiedAt { get; set; }

    /// <summary>
    /// Whether the user has dismissed/seen this notification.
    /// </summary>
    public bool Dismissed { get; set; } = false;
}
