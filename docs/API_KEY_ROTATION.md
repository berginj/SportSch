# API Key Rotation Strategy

This document describes the API key rotation system for securing external API access.

## Overview

The API uses a **dual-key system** for zero-downtime key rotation:
- **Primary Key**: Currently active key for production use
- **Secondary Key**: Backup key for rotation and failover

This allows you to rotate keys without service interruption by following the rotation workflow.

## Architecture

### Components

1. **ApiKeyService** - Manages key generation, validation, and rotation
2. **ApiKeyManagementFunctions** - Admin endpoints for key management
3. **Azure Table Storage** - Persists keys and rotation history
4. **AuditLogger** - Tracks all key operations for security compliance

### Storage

**Tables:**
- `ApiKeys` - Stores current primary and secondary keys
- `ApiKeyHistory` - Tracks all rotation events for audit trail

## Zero-Downtime Rotation Workflow

### Step 1: Initial State
```
Primary Key:   abc123... (in use by all clients)
Secondary Key: def456... (not yet distributed)
```

### Step 2: Distribute Secondary Key
Update 50% of your clients to use the secondary key:
```
50% of clients: abc123... (primary)
50% of clients: def456... (secondary)
```

Both keys are valid simultaneously.

### Step 3: Rotate Keys
Call the rotation endpoint:
```bash
POST /api/admin/api-keys/rotate
```

Result:
```
Primary Key:   def456... (was secondary, now primary)
Secondary Key: xyz789... (newly generated)
```

### Step 4: Update Remaining Clients
Update the remaining 50% of clients to use the new primary key (def456...).

### Step 5: Next Rotation
The cycle repeats - distribute the secondary key (xyz789...) before rotating again.

## API Endpoints

### Get Active API Keys

```http
GET /api/admin/api-keys
Authorization: Function-Key
```

**Response:**
```json
{
  "data": {
    "primaryKey": "abc123...",
    "secondaryKey": "def456...",
    "primaryKeyCreatedUtc": "2026-01-01T00:00:00Z",
    "secondaryKeyCreatedUtc": "2026-01-15T00:00:00Z",
    "lastRotatedBy": "admin@example.com",
    "lastRotatedUtc": "2026-01-15T00:00:00Z"
  }
}
```

### Rotate API Keys

```http
POST /api/admin/api-keys/rotate
Authorization: Function-Key
```

Performs zero-downtime rotation:
- Secondary key becomes primary
- New secondary key is generated

**Response:**
```json
{
  "data": {
    "primaryKey": "def456...",
    "secondaryKey": "xyz789...",
    "primaryKeyCreatedUtc": "2026-01-15T00:00:00Z",
    "secondaryKeyCreatedUtc": "2026-01-17T00:00:00Z",
    "lastRotatedBy": "admin@example.com",
    "lastRotatedUtc": "2026-01-17T00:00:00Z",
    "message": "API keys rotated successfully. Update your clients with the new keys."
  }
}
```

### Regenerate Secondary Key

```http
POST /api/admin/api-keys/regenerate-secondary
Authorization: Function-Key
```

Regenerates only the secondary key (for emergency revocation):
- Primary key remains unchanged
- Secondary key is regenerated

**Use case:** If the secondary key is compromised but the primary key is still secure.

**Response:**
```json
{
  "data": {
    "primaryKey": "abc123...",
    "secondaryKey": "new789...",
    "message": "Secondary API key regenerated successfully."
  }
}
```

### Get Rotation History

```http
GET /api/admin/api-keys/history?limit=10
Authorization: Function-Key
```

**Response:**
```json
{
  "data": [
    {
      "eventId": "20260117140000_abc123",
      "eventType": "ROTATE",
      "performedBy": "admin@example.com",
      "performedUtc": "2026-01-17T14:00:00Z",
      "primaryKeyHash": "hash1...",
      "secondaryKeyHash": "hash2..."
    }
  ]
}
```

## Authorization

All API key management endpoints require:
- **Global Admin** privileges
- Valid authentication token

Only global admins can:
- View active keys
- Rotate keys
- Regenerate keys
- View rotation history

## Security Features

### 1. SHA-256 Hashing
API keys are hashed using SHA-256 before storage in rotation history. This prevents key recovery from audit logs.

### 2. Secure Generation
Keys are generated using `RandomNumberGenerator` (cryptographically secure):
```csharp
var bytes = RandomNumberGenerator.GetBytes(32); // 256 bits
var apiKey = Convert.ToBase64String(bytes);
```

### 3. Audit Logging
All key operations are logged:
- Key viewing (VIEW)
- Key rotation (ROTATE)
- Secondary regeneration (REGENERATE_SECONDARY)
- History viewing (VIEW_HISTORY)

Query audit logs in Application Insights:
```kusto
traces
| where message contains "AUDIT: API key"
| project timestamp, message, customDimensions.UserId, customDimensions.Operation
```

### 4. Dual-Key System
Both keys are always valid, allowing gradual client updates without downtime.

## Rotation Schedule

### Recommended: Every 90 Days

```
Day 0:   Rotate keys (secondary → primary, generate new secondary)
Day 1-30: Distribute new secondary key to 50% of clients
Day 31:  Rotate keys again
Day 32-60: Update remaining clients
Day 61:  Next rotation cycle
```

### Emergency Rotation

If a key is compromised:

1. **Immediate:** Call `/admin/api-keys/regenerate-secondary`
2. **Within 1 hour:** Update all clients to use primary key
3. **Within 24 hours:** Call `/admin/api-keys/rotate`
4. **Within 48 hours:** Update all clients to use new primary key

## Client Implementation

### Using API Keys

Include the API key in requests:

```http
GET /api/slots
x-api-key: abc123def456...
x-league-id: my-league
```

### Handling Rotation

Clients should:
1. **Support both keys:** Try primary first, fallback to secondary
2. **Monitor rotation:** Check `/admin/api-keys` periodically
3. **Auto-update:** When new keys are detected, update configuration

**Example (pseudo-code):**
```javascript
async function callApi(endpoint) {
  try {
    return await fetch(endpoint, {
      headers: { 'x-api-key': PRIMARY_KEY }
    });
  } catch (error) {
    if (error.status === 401) {
      // Try secondary key
      return await fetch(endpoint, {
        headers: { 'x-api-key': SECONDARY_KEY }
      });
    }
    throw error;
  }
}
```

## Monitoring

### Key Metrics

Track in Application Insights:

1. **Failed Authentication Rate**
```kusto
requests
| where resultCode == 401
| summarize count() by bin(timestamp, 5m)
```

2. **Key Age**
```kusto
traces
| where message contains "AUDIT: API key rotation"
| summarize LastRotation = max(timestamp)
| extend DaysSinceRotation = datetime_diff('day', now(), LastRotation)
```

3. **Rotation Events**
```kusto
traces
| where message contains "AUDIT: API key rotation"
| where customDimensions.OperationType == "ROTATE"
| summarize count() by bin(timestamp, 1d)
```

### Alerts

Set up alerts for:
- Keys not rotated in > 90 days
- Failed authentication spike (> 10% of requests)
- Multiple rotation events in short time (potential compromise)

## Testing

### Manual Testing

```bash
# Get current keys
curl -X GET https://your-app.azurewebsites.net/api/admin/api-keys \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"

# Test key validation
curl -X GET https://your-app.azurewebsites.net/api/slots \
  -H "x-api-key: PRIMARY_KEY" \
  -H "x-league-id: test-league"

# Rotate keys
curl -X POST https://your-app.azurewebsites.net/api/admin/api-keys/rotate \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN"

# Verify old secondary key is now primary
curl -X GET https://your-app.azurewebsites.net/api/slots \
  -H "x-api-key: OLD_SECONDARY_KEY" \
  -H "x-league-id: test-league"
```

### Automated Testing

```csharp
[Test]
public async Task ApiKeyRotation_ShouldMaintainAccess()
{
    // Get initial keys
    var initialKeys = await apiKeyService.GetActiveKeysAsync();

    // Both keys should work
    Assert.IsTrue(await apiKeyService.ValidateKeyAsync(initialKeys.PrimaryKey));
    Assert.IsTrue(await apiKeyService.ValidateKeyAsync(initialKeys.SecondaryKey));

    // Rotate
    var rotatedKeys = await apiKeyService.RotateKeysAsync("test-user");

    // Old secondary (now primary) should still work
    Assert.IsTrue(await apiKeyService.ValidateKeyAsync(initialKeys.SecondaryKey));

    // New secondary should work
    Assert.IsTrue(await apiKeyService.ValidateKeyAsync(rotatedKeys.SecondaryKey));

    // Old primary should no longer work
    Assert.IsFalse(await apiKeyService.ValidateKeyAsync(initialKeys.PrimaryKey));
}
```

## Troubleshooting

### "API key invalid" errors after rotation

**Cause:** Clients still using old primary key (rotated out).

**Solution:**
1. Check current keys: `GET /api/admin/api-keys`
2. Verify which key client is using
3. Update client to use current primary or secondary key

### Keys not rotating

**Cause:** Azure Table Storage connection issue.

**Solution:**
1. Check Application Insights logs for errors
2. Verify connection string in `local.settings.json` or App Settings
3. Ensure tables `ApiKeys` and `ApiKeyHistory` exist

### Unauthorized errors for admins

**Cause:** User is not a global admin.

**Solution:**
1. Verify user has GlobalAdmin flag in system
2. Check membership: `GET /api/me`
3. Grant global admin access if needed

## Best Practices

1. **Rotate regularly:** Every 90 days minimum
2. **Never commit keys:** Store in Azure Key Vault, not source control
3. **Log everything:** All key operations should be audited
4. **Test rotation:** Practice the rotation workflow in staging first
5. **Monitor usage:** Track which keys are being used and when
6. **Emergency plan:** Have a documented process for compromised keys
7. **Limit access:** Only global admins should manage keys
8. **Secure transmission:** Always use HTTPS for key distribution

## Integration with CI/CD

Update keys in deployment pipelines:

```yaml
# Azure DevOps Pipeline
- task: AzureKeyVault@2
  inputs:
    azureSubscription: 'Your-Subscription'
    KeyVaultName: 'your-keyvault'
    SecretsFilter: 'ApiPrimaryKey,ApiSecondaryKey'

- script: |
    echo "Primary Key: $(ApiPrimaryKey)" >> .env
    echo "Secondary Key: $(ApiSecondaryKey)" >> .env
```

## Support

For issues with API key rotation:
- Check audit logs in Application Insights
- Review rotation history: `GET /api/admin/api-keys/history`
- Contact global admin for key management access
