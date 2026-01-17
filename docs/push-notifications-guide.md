# Browser Push Notifications Implementation Guide

This document outlines the steps needed to implement browser push notifications using the Web Push API.

## Overview

Browser push notifications allow GameSwap to send real-time notifications to users even when they don't have the app open. This requires:

1. Service Worker registration
2. Push subscription management
3. VAPID keys for authentication
4. Backend integration with Web Push protocol

## Prerequisites

- HTTPS (required for service workers and push API)
- User permission for notifications
- VAPID keys (Voluntary Application Server Identification)

## Implementation Steps

### 1. Generate VAPID Keys

```bash
# Install web-push globally
npm install -g web-push

# Generate VAPID keys
web-push generate-vapid-keys

# Output will be:
# Public Key: <public-key>
# Private Key: <private-key>
```

Store these in your environment configuration:
- `VAPID_PUBLIC_KEY` - Share with frontend
- `VAPID_PRIVATE_KEY` - Keep secret on backend
- `VAPID_SUBJECT` - mailto:admin@gameswap.app or https://gameswap.app

### 2. Create Service Worker

**File: `public/service-worker.js`**

```javascript
self.addEventListener('push', function(event) {
  const data = event.data.json();

  const options = {
    body: data.message,
    icon: '/icon-192.png',
    badge: '/badge-72.png',
    data: {
      url: data.link || '/',
      notificationId: data.notificationId
    },
    tag: data.notificationId,
    requireInteraction: false,
    actions: data.actions || []
  };

  event.waitUntil(
    self.registration.showNotification(data.title || 'GameSwap', options)
  );
});

self.addEventListener('notificationclick', function(event) {
  event.notification.close();

  const urlToOpen = event.notification.data.url || '/';

  event.waitUntil(
    clients.matchAll({ type: 'window', includeUncontrolled: true })
      .then(function(clientList) {
        // Check if a window is already open
        for (let client of clientList) {
          if (client.url === urlToOpen && 'focus' in client) {
            return client.focus();
          }
        }

        // Open new window if none found
        if (clients.openWindow) {
          return clients.openWindow(urlToOpen);
        }
      })
  );
});
```

### 3. Register Service Worker (Frontend)

**File: `src/lib/pushNotifications.js`**

```javascript
// Check if push notifications are supported
export function isPushSupported() {
  return 'serviceWorker' in navigator && 'PushManager' in window;
}

// Request notification permission
export async function requestNotificationPermission() {
  if (!isPushSupported()) {
    throw new Error('Push notifications not supported');
  }

  const permission = await Notification.requestPermission();
  return permission === 'granted';
}

// Register service worker
export async function registerServiceWorker() {
  if (!isPushSupported()) {
    throw new Error('Service workers not supported');
  }

  try {
    const registration = await navigator.serviceWorker.register('/service-worker.js');
    console.log('Service worker registered:', registration);
    return registration;
  } catch (error) {
    console.error('Service worker registration failed:', error);
    throw error;
  }
}

// Subscribe to push notifications
export async function subscribeToPushNotifications(vapidPublicKey) {
  try {
    const registration = await navigator.serviceWorker.ready;

    // Check if already subscribed
    let subscription = await registration.pushManager.getSubscription();

    if (!subscription) {
      // Create new subscription
      const convertedVapidKey = urlBase64ToUint8Array(vapidPublicKey);

      subscription = await registration.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: convertedVapidKey
      });

      console.log('Push subscription created:', subscription);
    }

    return subscription;
  } catch (error) {
    console.error('Failed to subscribe to push notifications:', error);
    throw error;
  }
}

// Unsubscribe from push notifications
export async function unsubscribeFromPushNotifications() {
  try {
    const registration = await navigator.serviceWorker.ready;
    const subscription = await registration.pushManager.getSubscription();

    if (subscription) {
      await subscription.unsubscribe();
      console.log('Push subscription cancelled');
      return true;
    }

    return false;
  } catch (error) {
    console.error('Failed to unsubscribe:', error);
    throw error;
  }
}

// Helper function to convert VAPID key
function urlBase64ToUint8Array(base64String) {
  const padding = '='.repeat((4 - base64String.length % 4) % 4);
  const base64 = (base64String + padding)
    .replace(/\\-/g, '+')
    .replace(/_/g, '/');

  const rawData = window.atob(base64);
  const outputArray = new Uint8Array(rawData.length);

  for (let i = 0; i < rawData.length; ++i) {
    outputArray[i] = rawData.charCodeAt(i);
  }

  return outputArray;
}
```

### 4. Create Push Subscription UI Component

**File: `src/components/PushNotificationToggle.jsx`**

```javascript
import { useState, useEffect } from 'react';
import { apiFetch } from '../lib/api';
import {
  isPushSupported,
  requestNotificationPermission,
  registerServiceWorker,
  subscribeToPushNotifications,
  unsubscribeFromPushNotifications
} from '../lib/pushNotifications';

export default function PushNotificationToggle({ leagueId }) {
  const [isSubscribed, setIsSubscribed] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    checkSubscriptionStatus();
  }, []);

  async function checkSubscriptionStatus() {
    if (!isPushSupported()) return;

    try {
      const registration = await navigator.serviceWorker.ready;
      const subscription = await registration.pushManager.getSubscription();
      setIsSubscribed(!!subscription);
    } catch (err) {
      console.error('Failed to check subscription status:', err);
    }
  }

  async function handleToggle() {
    setIsLoading(true);
    setError('');

    try {
      if (isSubscribed) {
        // Unsubscribe
        await unsubscribeFromPushNotifications();

        // Notify backend to remove subscription
        await apiFetch('/api/notifications/push/unsubscribe', {
          method: 'POST'
        });

        setIsSubscribed(false);
      } else {
        // Request permission
        const granted = await requestNotificationPermission();
        if (!granted) {
          setError('Notification permission denied');
          return;
        }

        // Register service worker
        await registerServiceWorker();

        // Get VAPID public key from backend
        const config = await apiFetch('/api/notifications/push/config');
        const vapidPublicKey = config.data.publicKey;

        // Subscribe
        const subscription = await subscribeToPushNotifications(vapidPublicKey);

        // Send subscription to backend
        await apiFetch('/api/notifications/push/subscribe', {
          method: 'POST',
          body: JSON.stringify({
            subscription: subscription.toJSON()
          })
        });

        setIsSubscribed(true);
      }
    } catch (err) {
      setError(err.message || 'Failed to toggle push notifications');
    } finally {
      setIsLoading(false);
    }
  }

  if (!isPushSupported()) {
    return (
      <div className="text-sm text-gray-600">
        Push notifications are not supported in this browser.
      </div>
    );
  }

  return (
    <div>
      <label className="flex items-center gap-3 cursor-pointer">
        <input
          type="checkbox"
          checked={isSubscribed}
          onChange={handleToggle}
          disabled={isLoading}
          className="w-5 h-5"
        />
        <div>
          <div className="font-semibold">Browser Push Notifications</div>
          <div className="text-sm text-gray-600">
            Receive notifications even when the app is closed
          </div>
        </div>
      </label>

      {error && <div className="text-sm text-red-600 mt-2">{error}</div>}
    </div>
  );
}
```

### 5. Backend - Push Subscription Management

**Add to `INotificationService` interface:**

```csharp
Task StorePushSubscriptionAsync(string userId, string leagueId, PushSubscription subscription);
Task RemovePushSubscriptionAsync(string userId, string leagueId);
Task<List<PushSubscription>> GetUserPushSubscriptionsAsync(string userId, string leagueId);
```

**Create model `PushSubscription.cs`:**

```csharp
public class PushSubscription
{
    public string Endpoint { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public PushSubscriptionKeys Keys { get; set; }
}

public class PushSubscriptionKeys
{
    public string P256dh { get; set; }
    public string Auth { get; set; }
}
```

**Create Azure Functions:**

```csharp
// GET /api/notifications/push/config - Get VAPID public key
[Function("GetPushConfig")]
public HttpResponseData GetPushConfig(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/push/config")] HttpRequestData req)
{
    var publicKey = _configuration["VAPID_PUBLIC_KEY"];
    return ApiResponses.Ok(req, new { publicKey });
}

// POST /api/notifications/push/subscribe - Store push subscription
[Function("SubscribeToPush")]
public async Task<HttpResponseData> SubscribeToPush(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/push/subscribe")] HttpRequestData req)
{
    var leagueId = ApiGuards.RequireLeagueId(req);
    var me = IdentityUtil.GetMe(req);

    var body = await HttpUtil.ReadJsonAsync<SubscribeRequest>(req);
    // Store subscription in table storage

    return ApiResponses.Ok(req, new { success = true });
}

// POST /api/notifications/push/unsubscribe - Remove push subscription
[Function("UnsubscribeFromPush")]
public async Task<HttpResponseData> UnsubscribeFromPush(
    [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/push/unsubscribe")] HttpRequestData req)
{
    var leagueId = ApiGuards.RequireLeagueId(req);
    var me = IdentityUtil.GetMe(req);

    // Remove subscription from table storage

    return ApiResponses.Ok(req, new { success = true });
}
```

### 6. Backend - Send Push Notifications

**Install NuGet package:**

```bash
dotnet add package WebPush
```

**Create `IPushNotificationService`:**

```csharp
public interface IPushNotificationService
{
    Task SendPushNotificationAsync(string userId, string leagueId, string title, string message, string? link = null);
}
```

**Implementation:**

```csharp
using WebPush;

public class PushNotificationService : IPushNotificationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly WebPushClient _webPushClient;

    public PushNotificationService(IConfiguration configuration, ILogger<PushNotificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var vapidPublicKey = _configuration["VAPID_PUBLIC_KEY"];
        var vapidPrivateKey = _configuration["VAPID_PRIVATE_KEY"];
        var vapidSubject = _configuration["VAPID_SUBJECT"] ?? "mailto:admin@gameswap.app";

        _webPushClient = new WebPushClient();
        _webPushClient.SetVapidDetails(vapidSubject, vapidPublicKey, vapidPrivateKey);
    }

    public async Task SendPushNotificationAsync(string userId, string leagueId, string title, string message, string? link = null)
    {
        try
        {
            // Get user's push subscriptions
            var subscriptions = await GetUserPushSubscriptionsAsync(userId, leagueId);

            foreach (var subscription in subscriptions)
            {
                var payload = new
                {
                    title = title,
                    message = message,
                    link = link,
                    timestamp = DateTime.UtcNow
                };

                var pushSubscription = new WebPush.PushSubscription(
                    subscription.Endpoint,
                    subscription.Keys.P256dh,
                    subscription.Keys.Auth
                );

                try
                {
                    await _webPushClient.SendNotificationAsync(
                        pushSubscription,
                        System.Text.Json.JsonSerializer.Serialize(payload)
                    );

                    _logger.LogInformation("Sent push notification to user {UserId}", userId);
                }
                catch (WebPushException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone)
                {
                    // Subscription expired, remove it
                    _logger.LogWarning("Push subscription expired for user {UserId}, removing", userId);
                    await RemovePushSubscriptionAsync(userId, subscription.Endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send push notification to user {UserId}", userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send push notifications to user {UserId}", userId);
        }
    }
}
```

### 7. Integration with Existing Notification System

**Update `SlotService` and `RequestService`:**

```csharp
private readonly IPushNotificationService _pushService;

// In CreateSlotAsync, after creating in-app notification:
await _pushService.SendPushNotificationAsync(
    coachUserId,
    context.LeagueId,
    "New Game Slot",
    message,
    "#calendar"
);
```

## Security Considerations

1. **VAPID Keys**: Never expose private key to frontend
2. **Permission**: Always request user permission before subscribing
3. **HTTPS**: Required for service workers and push API
4. **Rate Limiting**: Implement rate limits on subscription endpoints
5. **Validation**: Validate subscription data before storing

## Testing

1. **Local Testing**: Use `localhost` or HTTPS tunnel (ngrok)
2. **Browser DevTools**: Check Application > Service Workers
3. **Push Notification Tester**: Use tools like [web-push-testing](https://web-push-testing.xyz/)

## Browser Support

- Chrome: ✅ Full support
- Firefox: ✅ Full support
- Safari: ⚠️ Limited support (iOS 16.4+)
- Edge: ✅ Full support

## Resources

- [MDN Web Push API](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
- [web-push NPM](https://www.npmjs.com/package/web-push)
- [Service Worker Cookbook](https://serviceworke.rs/)
- [Push Notifications Guide](https://web.dev/push-notifications-overview/)

## Next Steps

1. Generate VAPID keys
2. Set up service worker
3. Create frontend subscription UI
4. Implement backend subscription storage
5. Integrate with NotificationService
6. Test across browsers
7. Monitor subscription health and cleanup expired subscriptions
