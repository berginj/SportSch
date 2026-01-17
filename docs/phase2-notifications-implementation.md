# Phase 2: Notification System Implementation Plan

**Status:** In Progress
**Start Date:** 2026-01-17

## Overview

Implement a comprehensive notification system with email alerts and in-app notifications to keep users informed of important events without requiring constant app monitoring.

## Goals

1. Email notifications for key events (slot offers, acceptances, game reminders)
2. In-app notification center with unread badge
3. User preferences for notification types and frequency
4. Reliable delivery using Azure Communication Services or similar
5. Foundation for future push notifications

## Architecture

### Backend Components

```
api/
├── Services/
│   ├── INotificationService.cs         (NEW)
│   ├── NotificationService.cs          (NEW)
│   ├── IEmailService.cs                (NEW)
│   └── EmailService.cs                 (NEW)
├── Functions/
│   ├── NotificationsFunctions.cs       (NEW - CRUD for notifications)
│   ├── NotificationPreferencesFunctions.cs (NEW - user preferences)
│   └── EmailQueueProcessor.cs          (NEW - Timer trigger, processes queue)
├── Storage/
│   └── EmailTemplates/
│       ├── new-slot-offer.html         (NEW)
│       ├── slot-accepted.html          (NEW)
│       ├── game-reminder.html          (NEW)
│       ├── schedule-change.html        (NEW)
│       └── _layout.html                (NEW - base template)
└── Models/
    ├── Notification.cs                 (NEW)
    ├── NotificationPreferences.cs      (NEW)
    └── EmailQueueItem.cs               (NEW)
```

### Frontend Components

```
src/
├── components/
│   ├── notifications/
│   │   ├── NotificationBell.jsx        (NEW - TopNav bell icon)
│   │   ├── NotificationDropdown.jsx    (NEW - dropdown list)
│   │   └── NotificationItem.jsx        (NEW - single notification)
│   └── NotificationSettings.jsx        (NEW - preferences page)
├── lib/
│   ├── hooks/
│   │   └── useNotifications.js         (NEW - notification state hook)
│   └── api/
│       └── notifications.js            (NEW - API functions)
└── pages/
    └── NotificationSettingsPage.jsx    (NEW - full settings page)
```

### Database Tables

**GameSwapNotifications**
- PK: `NOTIFICATION|{userId}`
- RK: `{notificationId}` (GUID)
- Fields: type, message, actionUrl, isRead, createdUtc

**GameSwapNotificationPreferences**
- PK: `NOTIFPREF|{userId}`
- RK: `PREFS`
- Fields: emailEnabled, digestFrequency, preferences (JSON with per-type settings)

**GameSwapEmailQueue**
- PK: `EMAILQUEUE`
- RK: `{emailId}` (GUID)
- Fields: toEmail, subject, bodyHtml, bodyText, status, createdUtc, sentUtc, error

## Implementation Steps

### Step 1: Backend Infrastructure (2-3 hours)

#### 1.1: Create Notification Models

**File: `api/Models/Notification.cs`**
```csharp
public class Notification
{
    public string NotificationId { get; set; }
    public string UserId { get; set; }
    public string Type { get; set; } // "NEW_SLOT_OFFER", "SLOT_ACCEPTED", etc.
    public string Message { get; set; }
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public class NotificationPreferences
{
    public string UserId { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public string DigestFrequency { get; set; } = "immediate"; // immediate, daily, weekly
    public Dictionary<string, bool> TypePreferences { get; set; } = new();
}

public class EmailQueueItem
{
    public string EmailId { get; set; }
    public string ToEmail { get; set; }
    public string Subject { get; set; }
    public string BodyHtml { get; set; }
    public string BodyText { get; set; }
    public string Status { get; set; } // pending, sent, failed
    public DateTime CreatedUtc { get; set; }
    public DateTime? SentUtc { get; set; }
    public string? Error { get; set; }
}
```

#### 1.2: Create Notification Service

**File: `api/Services/INotificationService.cs`**
```csharp
public interface INotificationService
{
    Task<Notification> CreateNotificationAsync(string userId, string type, string message, string? actionUrl = null);
    Task<List<Notification>> GetUserNotificationsAsync(string userId, int limit = 50);
    Task MarkAsReadAsync(string userId, string notificationId);
    Task MarkAllAsReadAsync(string userId);
    Task<int> GetUnreadCountAsync(string userId);
}
```

**File: `api/Services/NotificationService.cs`**
```csharp
public class NotificationService : INotificationService
{
    private readonly TableServiceClient _tableService;
    private readonly IEmailService _emailService;
    private readonly ILogger<NotificationService> _logger;

    public async Task<Notification> CreateNotificationAsync(string userId, string type, string message, string? actionUrl = null)
    {
        var notification = new Notification
        {
            NotificationId = Guid.NewGuid().ToString(),
            UserId = userId,
            Type = type,
            Message = message,
            ActionUrl = actionUrl,
            IsRead = false,
            CreatedUtc = DateTime.UtcNow
        };

        // Save to table storage
        var table = await TableClients.GetTableAsync(_tableService, "GameSwapNotifications");
        var entity = new TableEntity($"NOTIFICATION|{userId}", notification.NotificationId)
        {
            ["Type"] = notification.Type,
            ["Message"] = notification.Message,
            ["ActionUrl"] = notification.ActionUrl,
            ["IsRead"] = notification.IsRead,
            ["CreatedUtc"] = notification.CreatedUtc
        };
        await table.AddEntityAsync(entity);

        // Queue email if user has email notifications enabled
        await _emailService.QueueNotificationEmailAsync(userId, notification);

        return notification;
    }

    // ... other methods
}
```

#### 1.3: Create Email Service (Stub for now)

**File: `api/Services/IEmailService.cs`**
```csharp
public interface IEmailService
{
    Task QueueNotificationEmailAsync(string userId, Notification notification);
    Task QueueEmailAsync(string toEmail, string subject, string bodyHtml, string bodyText);
    Task<bool> SendEmailAsync(EmailQueueItem email);
}
```

**File: `api/Services/EmailService.cs`** (Stub implementation)
```csharp
public class EmailService : IEmailService
{
    private readonly TableServiceClient _tableService;
    private readonly ILogger<EmailService> _logger;

    public async Task QueueNotificationEmailAsync(string userId, Notification notification)
    {
        // TODO: Load user preferences
        // TODO: Check if user has email enabled for this notification type
        // TODO: Generate email from template
        // TODO: Queue for sending
        _logger.LogInformation("Email queued for user {UserId}, notification {NotificationId}", userId, notification.NotificationId);
    }

    public async Task QueueEmailAsync(string toEmail, string subject, string bodyHtml, string bodyText)
    {
        var email = new EmailQueueItem
        {
            EmailId = Guid.NewGuid().ToString(),
            ToEmail = toEmail,
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = bodyText,
            Status = "pending",
            CreatedUtc = DateTime.UtcNow
        };

        var table = await TableClients.GetTableAsync(_tableService, "GameSwapEmailQueue");
        var entity = new TableEntity("EMAILQUEUE", email.EmailId)
        {
            ["ToEmail"] = email.ToEmail,
            ["Subject"] = email.Subject,
            ["BodyHtml"] = email.BodyHtml,
            ["BodyText"] = email.BodyText,
            ["Status"] = email.Status,
            ["CreatedUtc"] = email.CreatedUtc
        };
        await table.AddEntityAsync(entity);
    }

    public async Task<bool> SendEmailAsync(EmailQueueItem email)
    {
        // TODO: Integrate with Azure Communication Services or SendGrid
        _logger.LogInformation("Sending email to {ToEmail}: {Subject}", email.ToEmail, email.Subject);
        return true;
    }
}
```

#### 1.4: Create Notification Functions

**File: `api/Functions/NotificationsFunctions.cs`**
```csharp
public class NotificationsFunctions
{
    private readonly INotificationService _notificationService;
    private readonly ILogger _log;

    public NotificationsFunctions(INotificationService notificationService, ILoggerFactory loggerFactory)
    {
        _notificationService = notificationService;
        _log = loggerFactory.CreateLogger<NotificationsFunctions>();
    }

    [Function("GetNotifications")]
    public async Task<HttpResponseData> GetNotifications([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequestData req)
    {
        var me = IdentityUtil.GetMe(req);
        var notifications = await _notificationService.GetUserNotificationsAsync(me.UserId);
        return ApiResponses.Ok(req, notifications);
    }

    [Function("MarkNotificationRead")]
    public async Task<HttpResponseData> MarkRead([HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "notifications/{notificationId}/read")] HttpRequestData req, string notificationId)
    {
        var me = IdentityUtil.GetMe(req);
        await _notificationService.MarkAsReadAsync(me.UserId, notificationId);
        return ApiResponses.Ok(req, new { success = true });
    }

    [Function("MarkAllNotificationsRead")]
    public async Task<HttpResponseData> MarkAllRead([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/read-all")] HttpRequestData req)
    {
        var me = IdentityUtil.GetMe(req);
        await _notificationService.MarkAllAsReadAsync(me.UserId);
        return ApiResponses.Ok(req, new { success = true });
    }

    [Function("GetUnreadCount")]
    public async Task<HttpResponseData> GetUnreadCount([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/unread-count")] HttpRequestData req)
    {
        var me = IdentityUtil.GetMe(req);
        var count = await _notificationService.GetUnreadCountAsync(me.UserId);
        return ApiResponses.Ok(req, new { count });
    }
}
```

#### 1.5: Register Services in Program.cs

```csharp
// Add to ConfigureServices
services.AddScoped<INotificationService, NotificationService>();
services.AddScoped<IEmailService, EmailService>();
```

### Step 2: Frontend Infrastructure (2-3 hours)

#### 2.1: Create Notification Hook

**File: `src/lib/hooks/useNotifications.js`**
```javascript
import { useState, useEffect, useCallback } from 'react';
import { apiFetch } from '../api';

export function useNotifications() {
  const [notifications, setNotifications] = useState([]);
  const [unreadCount, setUnreadCount] = useState(0);
  const [loading, setLoading] = useState(false);

  const loadNotifications = useCallback(async () => {
    setLoading(true);
    try {
      const data = await apiFetch('/api/notifications');
      setNotifications(Array.isArray(data) ? data : []);
    } catch (err) {
      console.error('Failed to load notifications:', err);
    } finally {
      setLoading(false);
    }
  }, []);

  const loadUnreadCount = useCallback(async () => {
    try {
      const result = await apiFetch('/api/notifications/unread-count');
      setUnreadCount(result?.count || 0);
    } catch (err) {
      console.error('Failed to load unread count:', err);
    }
  }, []);

  const markAsRead = useCallback(async (notificationId) => {
    try {
      await apiFetch(`/api/notifications/${notificationId}/read`, { method: 'PATCH' });
      setNotifications(prev => prev.map(n =>
        n.notificationId === notificationId ? { ...n, isRead: true } : n
      ));
      setUnreadCount(prev => Math.max(0, prev - 1));
    } catch (err) {
      console.error('Failed to mark as read:', err);
    }
  }, []);

  const markAllAsRead = useCallback(async () => {
    try {
      await apiFetch('/api/notifications/read-all', { method: 'POST' });
      setNotifications(prev => prev.map(n => ({ ...n, isRead: true })));
      setUnreadCount(0);
    } catch (err) {
      console.error('Failed to mark all as read:', err);
    }
  }, []);

  useEffect(() => {
    loadUnreadCount();
    const interval = setInterval(loadUnreadCount, 30000); // Poll every 30s
    return () => clearInterval(interval);
  }, [loadUnreadCount]);

  return {
    notifications,
    unreadCount,
    loading,
    loadNotifications,
    loadUnreadCount,
    markAsRead,
    markAllAsRead,
  };
}
```

#### 2.2: Create Notification Bell Component

**File: `src/components/notifications/NotificationBell.jsx`**
```javascript
import { useState } from 'react';
import { useNotifications } from '../../lib/hooks/useNotifications';
import NotificationDropdown from './NotificationDropdown';

export default function NotificationBell() {
  const { unreadCount } = useNotifications();
  const [isOpen, setIsOpen] = useState(false);

  return (
    <div className="relative">
      <button
        className="relative p-2 rounded-full hover:bg-gray-100 transition-colors"
        onClick={() => setIsOpen(!isOpen)}
        aria-label="Notifications"
      >
        <svg
          className="w-6 h-6 text-gray-700"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
          />
        </svg>
        {unreadCount > 0 && (
          <span className="absolute top-0 right-0 inline-flex items-center justify-center px-2 py-1 text-xs font-bold leading-none text-white bg-red-600 rounded-full">
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
        )}
      </button>

      {isOpen && (
        <NotificationDropdown onClose={() => setIsOpen(false)} />
      )}
    </div>
  );
}
```

#### 2.3: Create Notification Dropdown Component

**File: `src/components/notifications/NotificationDropdown.jsx`**
```javascript
import { useEffect } from 'react';
import { useNotifications } from '../../lib/hooks/useNotifications';

export default function NotificationDropdown({ onClose }) {
  const { notifications, loading, loadNotifications, markAsRead, markAllAsRead } = useNotifications();

  useEffect(() => {
    loadNotifications();
  }, [loadNotifications]);

  useEffect(() => {
    // Close dropdown when clicking outside
    const handleClick = (e) => {
      if (!e.target.closest('.notification-dropdown')) {
        onClose();
      }
    };
    document.addEventListener('click', handleClick);
    return () => document.removeEventListener('click', handleClick);
  }, [onClose]);

  return (
    <div className="notification-dropdown absolute right-0 mt-2 w-80 bg-white rounded-lg shadow-lg border border-gray-200 max-h-96 overflow-hidden z-50">
      <div className="flex items-center justify-between p-3 border-b border-gray-200">
        <h3 className="font-semibold text-gray-900">Notifications</h3>
        {notifications.length > 0 && (
          <button
            className="text-xs text-blue-600 hover:text-blue-700"
            onClick={markAllAsRead}
          >
            Mark all read
          </button>
        )}
      </div>

      <div className="overflow-y-auto max-h-80">
        {loading && (
          <div className="p-4 text-center text-gray-500">Loading...</div>
        )}

        {!loading && notifications.length === 0 && (
          <div className="p-4 text-center text-gray-500">
            No notifications yet
          </div>
        )}

        {!loading && notifications.map((notification) => (
          <div
            key={notification.notificationId}
            className={`p-3 border-b border-gray-100 hover:bg-gray-50 cursor-pointer ${
              !notification.isRead ? 'bg-blue-50' : ''
            }`}
            onClick={() => {
              markAsRead(notification.notificationId);
              if (notification.actionUrl) {
                window.location.href = notification.actionUrl;
              }
            }}
          >
            <div className="flex items-start gap-2">
              {!notification.isRead && (
                <div className="w-2 h-2 bg-blue-600 rounded-full mt-2 flex-shrink-0" />
              )}
              <div className="flex-1 min-w-0">
                <p className="text-sm text-gray-900">{notification.message}</p>
                <p className="text-xs text-gray-500 mt-1">
                  {formatTimeAgo(notification.createdUtc)}
                </p>
              </div>
            </div>
          </div>
        ))}
      </div>

      <div className="p-2 border-t border-gray-200">
        <a
          href="#/notifications"
          className="block text-center text-sm text-blue-600 hover:text-blue-700 py-1"
          onClick={onClose}
        >
          View all notifications
        </a>
      </div>
    </div>
  );
}

function formatTimeAgo(utcString) {
  const date = new Date(utcString);
  const seconds = Math.floor((new Date() - date) / 1000);

  const intervals = {
    year: 31536000,
    month: 2592000,
    week: 604800,
    day: 86400,
    hour: 3600,
    minute: 60
  };

  for (const [unit, secondsInUnit] of Object.entries(intervals)) {
    const interval = Math.floor(seconds / secondsInUnit);
    if (interval >= 1) {
      return `${interval} ${unit}${interval !== 1 ? 's' : ''} ago`;
    }
  }

  return 'just now';
}
```

#### 2.4: Integrate Notification Bell into TopNav

**File: `src/components/TopNav.jsx`** (Add to account section)
```javascript
import NotificationBell from './notifications/NotificationBell';

// In the render, add before the email display:
<NotificationBell />
<div className="whoami" title={email}>
  {email || "Signed in"}
</div>
```

### Step 3: Trigger Notifications on Events (1-2 hours)

Update existing functions to create notifications when events occur:

**Example: In CreateSlot.cs (after slot creation)**
```csharp
// Notify coaches in the division about new slot offer
await _notificationService.CreateNotificationAsync(
    userId: coachUserId,
    type: "NEW_SLOT_OFFER",
    message: $"New game slot available in {division}: {gameDate} at {fieldName}",
    actionUrl: $"/#calendar?division={division}"
);
```

**Example: In ApproveSlotRequest.cs (after approval)**
```csharp
// Notify offering coach that their slot was accepted
await _notificationService.CreateNotificationAsync(
    userId: offeringCoachUserId,
    type: "SLOT_ACCEPTED",
    message: $"Your slot offer was accepted by {requestingTeamId}",
    actionUrl: $"/#calendar?slotId={slotId}"
);
```

### Step 4: Testing (1 hour)

1. Test notification creation via API
2. Test notification bell displays unread count
3. Test dropdown shows notifications
4. Test mark as read functionality
5. Test mark all as read
6. Test notification polling (30s interval)

## Future Enhancements (Phase 2.5+)

- Email template implementation with HTML/text versions
- Email queue processor (Azure Function Timer Trigger)
- Azure Communication Services integration
- Notification preferences page
- Digest emails (daily/weekly)
- Push notifications (browser/mobile)
- Notification categories and filtering

## Success Metrics

- Notification delivery within 5 seconds of event
- Unread badge updates within 30 seconds
- Zero notification loss (all events create notifications)
- < 100ms response time for GetUnreadCount endpoint
- Users can mark notifications read/unread easily

## Risks & Mitigations

**Risk:** Notification spam overwhelming users
**Mitigation:** Start with essential notifications only, add preferences UI in Phase 2.5

**Risk:** High API call volume for polling
**Mitigation:** 30s poll interval, consider WebSocket for future

**Risk:** Email delivery delays
**Mitigation:** Queue-based system with retry logic (Phase 2.5)

---

**Next Steps:**
1. Create backend services and models
2. Create notification functions
3. Build frontend components
4. Integrate into TopNav
5. Add notification triggers to existing functions
6. Test end-to-end workflow
