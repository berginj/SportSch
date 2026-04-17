# API Key Management Guide

**CRITICAL: Read this document before deploying security updates**

This guide explains the new secure API key system and the required migration steps after deploying the security fixes.

---

## What Changed

### Before (INSECURE)
- API keys stored in **plaintext** in Azure Table Storage
- Keys could be retrieved at any time
- If database was compromised, keys were exposed

### After (SECURE) ✅
- API keys stored as **SHA256 hashes** in Azure Table Storage
- Plaintext keys shown **only once** during generation/rotation
- Database compromise does not reveal usable keys
- Keys must be saved immediately when generated

---

## CRITICAL: Post-Deployment Steps

### ⚠️ BREAKING CHANGE WARNING

**After deploying commit `e6e2800`, all existing API keys will STOP WORKING immediately.**

This is because:
1. Old keys are stored as plaintext in the database
2. New validation code expects SHA256 hashes
3. Plaintext keys cannot validate against the hash-based system

### Required Actions (Within 1 hour of deployment)

#### Step 1: Deploy the Changes
```bash
# Already done - commit e6e2800 is pushed
# Azure will automatically deploy via CI/CD
```

#### Step 2: Wait for Deployment to Complete
- Monitor Azure Function App deployment logs
- Confirm deployment succeeded
- Verify app is running (check Application Insights)

#### Step 3: Rotate ALL API Keys IMMEDIATELY

**You MUST rotate keys immediately. This is not optional.**

1. **Access the API Key Rotation Endpoint**
   - Only global administrators can access this endpoint
   - Use authenticated session (Azure AD or Google OAuth)

2. **Rotate the Keys**
   ```bash
   # Method 1: Using curl (authenticated)
   curl -X POST https://your-api-url/api/admin/api-keys/rotate \
     -H "Cookie: StaticWebAppsAuthCookie=your-auth-cookie" \
     -H "Content-Type: application/json"

   # Method 2: Using browser DevTools
   # 1. Login to the application as a global admin
   # 2. Open browser DevTools (F12)
   # 3. Go to Console tab
   # 4. Run:
   fetch('/api/admin/api-keys/rotate', {
     method: 'POST',
     credentials: 'include'
   }).then(r => r.json()).then(console.log)
   ```

3. **IMMEDIATELY SAVE THE RESPONSE**

   The response will look like:
   ```json
   {
     "data": {
       "primaryKey": "[Hash: 8f3b2c1d4e5f...]",
       "secondaryKey": "xK8mP2vQ9nL3jR7wC5fY1hT6bN4sM8gD0pA==",
       "primaryKeyCreatedUtc": "2026-04-17T10:30:00Z",
       "secondaryKeyCreatedUtc": "2026-04-17T14:45:00Z",
       "lastRotatedBy": "user@example.com",
       "lastRotatedUtc": "2026-04-17T14:45:00Z",
       "message": "API keys rotated successfully. Update your clients with the new keys."
     }
   }
   ```

   **CRITICAL NOTES:**
   - ✅ `secondaryKey` is shown in plaintext - **SAVE THIS IMMEDIATELY**
   - ⚠️ `primaryKey` shows only a hash preview - you cannot use this
   - ⚠️ The secondary key will **NEVER be shown again**
   - ⚠️ If you lose it, you must rotate again

4. **Update Client Applications**

   If you have any external systems using API keys:
   ```javascript
   // Update your API client configuration
   const API_KEY = "xK8mP2vQ9nL3jR7wC5fY1hT6bN4sM8gD0pA=="; // New secondary key

   fetch('/api/some-endpoint', {
     headers: {
       'X-API-Key': API_KEY
     }
   });
   ```

5. **Test the New Key**
   ```bash
   # Verify the new key works
   curl https://your-api-url/api/some-endpoint \
     -H "X-API-Key: xK8mP2vQ9nL3jR7wC5fY1hT6bN4sM8gD0pA=="
   ```

6. **Store the Key Securely**
   - **DO NOT** commit to source control
   - **DO NOT** share via email/Slack
   - **DO** store in password manager (1Password, LastPass, etc.)
   - **DO** document where it's stored
   - **CONSIDER** moving to Azure Key Vault (see SECURITY_ROADMAP.md #9)

---

## Understanding the Dual-Key System

### Why Two Keys?

The system uses **primary** and **secondary** keys to enable **zero-downtime rotation**:

```
Initial State:
├── Primary Key:   abc123 (active)
└── Secondary Key: def456 (active)

After Rotation:
├── Primary Key:   def456 (was secondary, now primary)
└── Secondary Key: ghi789 (newly generated)
```

### Rotation Strategy

**Recommended Rotation Schedule:**
- **Regular Rotation:** Every 90 days
- **Emergency Rotation:** Immediately if compromised
- **Compliance Rotation:** Per your organization's security policy

**How to Rotate Without Downtime:**

1. **Day 0**: You have Primary (A) and Secondary (B) in use
2. **Day 1**: Rotate keys
   - Primary becomes B (old secondary)
   - Secondary becomes C (new key)
   - Save key C immediately
3. **Day 2-7**: Gradually update clients to use key C
4. **Day 8**: Key A is no longer valid (was rotated out)
5. **Repeat every 90 days**

---

## API Key Operations

### 1. View Current Key Status (Metadata Only)

**Endpoint:** `GET /api/admin/api-keys`

**Authorization:** Global Admin only

**Response:**
```json
{
  "data": {
    "message": "API keys are stored securely as hashes. Plaintext keys are only shown once during generation/rotation.",
    "primaryKeyHash": "8f3b2c1d4e5f6a7b...",
    "secondaryKeyHash": "9g4c3d2e5f6a7b8c...",
    "primaryKeyCreatedUtc": "2026-01-15T10:00:00Z",
    "secondaryKeyCreatedUtc": "2026-04-17T14:45:00Z",
    "lastRotatedBy": "admin@example.com",
    "lastRotatedUtc": "2026-04-17T14:45:00Z",
    "note": "If you need to view keys, you must rotate or regenerate them."
  }
}
```

**What You Can Learn:**
- ✅ When keys were created
- ✅ Who last rotated them
- ✅ Partial hash for identification
- ❌ **Cannot retrieve actual keys**

---

### 2. Rotate Keys (Zero-Downtime)

**Endpoint:** `POST /api/admin/api-keys/rotate`

**Authorization:** Global Admin only

**What Happens:**
1. Old secondary becomes new primary (already in hash form)
2. New secondary is generated (shown in plaintext)
3. Rotation event logged to audit trail
4. Old primary is discarded

**Response:**
```json
{
  "data": {
    "primaryKey": "[Hash: 8f3b2c1d4e5f...]",
    "secondaryKey": "NEW_KEY_PLAINTEXT_HERE",
    "secondaryKeyCreatedUtc": "2026-04-17T14:45:00Z",
    "message": "API keys rotated successfully. Update your clients with the new keys."
  }
}
```

**CRITICAL:** Save `secondaryKey` immediately - it will never be shown again!

---

### 3. Regenerate Secondary Key (Emergency)

**Endpoint:** `POST /api/admin/api-keys/regenerate-secondary`

**Authorization:** Global Admin only

**Use Case:**
- Secondary key was compromised
- Need to invalidate secondary without rotating
- Lost secondary key and need a new one

**What Happens:**
1. Primary key remains unchanged
2. New secondary key generated (shown in plaintext)
3. Old secondary key immediately invalidated

**Response:**
```json
{
  "data": {
    "primaryKey": "[Hash: 8f3b2c1d4e5f...]",
    "secondaryKey": "NEW_SECONDARY_KEY_HERE",
    "message": "Secondary API key regenerated successfully."
  }
}
```

**CRITICAL:** Save `secondaryKey` immediately!

---

### 4. View Rotation History (Audit Trail)

**Endpoint:** `GET /api/admin/api-keys/history?limit=10`

**Authorization:** Global Admin only

**Response:**
```json
{
  "data": [
    {
      "eventId": "20260417144530_a1b2c3d4",
      "eventType": "ROTATE",
      "performedBy": "admin@example.com",
      "performedUtc": "2026-04-17T14:45:30Z",
      "primaryKeyHash": "8f3b2c1d4e5f6a7b...",
      "secondaryKeyHash": "9g4c3d2e5f6a7b8c..."
    },
    {
      "eventId": "20260115100000_e5f6g7h8",
      "eventType": "INITIALIZE",
      "performedBy": "System",
      "performedUtc": "2026-01-15T10:00:00Z",
      "primaryKeyHash": "7e2d1c0b9a8f7e6d...",
      "secondaryKeyHash": "6d1c0b9a8f7e6d5c..."
    }
  ]
}
```

**Use Cases:**
- Compliance auditing
- Security incident investigation
- Track who rotated keys and when

---

## Security Best Practices

### ✅ DO

1. **Rotate keys regularly** (every 90 days minimum)
2. **Save keys immediately** when generated (they're shown only once)
3. **Store keys in a secure password manager** or Key Vault
4. **Use the secondary key** for new integrations
5. **Keep primary key** as backup during transitions
6. **Monitor rotation history** for unauthorized changes
7. **Test new keys** before deploying to production
8. **Document key locations** (which systems use which key)

### ❌ DON'T

1. **Don't commit keys to source control** (.env files, config files)
2. **Don't share keys via email/Slack/chat**
3. **Don't reuse keys across environments** (dev/staging/prod)
4. **Don't skip rotation** because "it's inconvenient"
5. **Don't ignore rotation history alerts**
6. **Don't assume you can retrieve keys later** (they're shown once!)
7. **Don't use keys in client-side code** (browser JavaScript)
8. **Don't log API keys** in application logs

---

## Emergency Procedures

### Scenario 1: API Key Compromised

**Immediate Actions (within 15 minutes):**

1. **Regenerate the compromised key**
   ```bash
   # If secondary was compromised
   POST /api/admin/api-keys/regenerate-secondary

   # If primary was compromised, rotate to make it secondary, then regenerate
   POST /api/admin/api-keys/rotate
   POST /api/admin/api-keys/regenerate-secondary
   ```

2. **Update all legitimate clients** with new key

3. **Check audit logs** for unauthorized usage
   ```bash
   GET /api/admin/api-keys/history?limit=50
   ```

4. **Monitor Application Insights** for failed authentication attempts

5. **Document the incident** (when, how, what was accessed)

---

### Scenario 2: Lost API Key

**Problem:** You need the key but didn't save it.

**Solution:** You must rotate or regenerate to get a new key.

```bash
# Option A: Rotate (recommended if you can update clients)
POST /api/admin/api-keys/rotate
# Save the new secondary key

# Option B: Regenerate secondary (if you want to keep primary)
POST /api/admin/api-keys/regenerate-secondary
# Save the new secondary key
```

**Important:** There is no way to retrieve the original key. It's hashed in the database.

---

### Scenario 3: Rotation Failed or Database Corruption

**Problem:** Key rotation endpoint returns an error.

**Troubleshooting:**

1. **Check Azure Function App logs**
   ```bash
   az functionapp logs tail --name <function-app-name> --resource-group <rg-name>
   ```

2. **Verify Table Storage is accessible**
   - Check Azure Portal → Storage Account → Tables
   - Confirm `ApiKeys` table exists

3. **Check Application Insights** for exceptions
   - Look for errors in `ApiKeyService.cs`

4. **Worst case: Manual initialization**
   ```bash
   # Delete the corrupted entry
   # Next rotation will re-initialize
   ```

**Escalation:** If unable to resolve within 30 minutes, contact Azure support.

---

## Monitoring & Alerts

### Recommended Alerts

Set up Application Insights alerts for:

1. **Unauthorized key access attempts**
   ```kusto
   traces
   | where message contains "Failed to validate API key"
   | summarize count() by bin(timestamp, 5m)
   | where count_ > 10
   ```

2. **API key rotation events**
   ```kusto
   traces
   | where message contains "API keys rotated"
   | project timestamp, message, userId
   ```

3. **Failed rotation attempts**
   ```kusto
   exceptions
   | where outerMethod contains "RotateKeysAsync"
   | project timestamp, problemId, outerMessage
   ```

### Audit Checklist (Monthly)

- [ ] Review rotation history for unauthorized changes
- [ ] Verify all keys were rotated within the last 90 days
- [ ] Check for failed rotation attempts
- [ ] Confirm keys are not committed to source control
- [ ] Verify external systems are using current keys
- [ ] Test emergency rotation procedure

---

## Migration Checklist

Use this checklist for the post-deployment migration:

### Pre-Deployment
- [ ] Read this entire document
- [ ] Identify all systems using API keys
- [ ] Prepare to update clients immediately after rotation
- [ ] Schedule deployment during low-traffic period
- [ ] Have global admin credentials ready
- [ ] Open password manager to store new key

### Deployment
- [ ] Deploy security updates (commit e6e2800)
- [ ] Wait for deployment to complete (check Azure Portal)
- [ ] Verify Function App is running

### Post-Deployment (IMMEDIATE - within 1 hour)
- [ ] Login as global admin
- [ ] Call rotation endpoint: `POST /api/admin/api-keys/rotate`
- [ ] **SAVE the secondary key immediately** (in password manager)
- [ ] Screenshot or copy the entire response
- [ ] Test the new key with a test request
- [ ] Update all client systems with new key
- [ ] Verify clients can authenticate successfully
- [ ] Check Application Insights for errors

### Verification (within 24 hours)
- [ ] Confirm no authentication errors in logs
- [ ] Verify rotation history shows your rotation
- [ ] Test key retrieval (should show only hashes)
- [ ] Document key storage location
- [ ] Update runbooks with new procedures
- [ ] Schedule next rotation (90 days)

### Cleanup (within 1 week)
- [ ] Remove old plaintext keys from documentation
- [ ] Update API documentation with new procedures
- [ ] Train team on new key management process
- [ ] Set up monitoring alerts
- [ ] Consider migrating to Key Vault (see SECURITY_ROADMAP.md)

---

## FAQs

**Q: Can I retrieve my API key after rotation?**
A: No. Keys are shown only once in plaintext. After that, only hashes are stored. You must rotate again to get a new key.

**Q: What happens to my existing API keys after deployment?**
A: They will stop working immediately. The old plaintext keys cannot validate against the new hash-based system.

**Q: How long does key rotation take?**
A: The rotation itself is instant. Updating all client systems may take hours depending on your setup.

**Q: Can I rotate keys automatically on a schedule?**
A: Not currently. You must manually rotate via the API. This is intentional to require human approval for security operations.

**Q: What if I rotate keys but forget to save them?**
A: You must rotate again to get a new key. There's no way to retrieve the previous key.

**Q: How do I know which key (primary or secondary) I'm using?**
A: The validation system accepts both. Use the rotation history to see when each was created. Generally, use the most recently generated key (secondary after rotation).

**Q: Can I still use the old validation endpoint?**
A: No. The API has changed. All validation now uses hash comparison.

**Q: What's the performance impact of SHA256 hashing?**
A: Negligible. Hash computation takes microseconds and is done once per request.

**Q: Should I rotate both keys or just one?**
A: Use the standard rotation endpoint (`/rotate`) which handles the dual-key dance automatically. Only use `/regenerate-secondary` in emergencies.

**Q: How do I migrate to Azure Key Vault later?**
A: See SECURITY_ROADMAP.md task #9. Key Vault is recommended for enhanced security and automated rotation.

---

## Support & Contact

**For security incidents:**
- Rotate keys immediately (don't wait for approval)
- Document the incident
- Notify security team

**For questions about key management:**
- Review this document first
- Check Application Insights logs
- Review rotation history

**For feature requests:**
- File an issue in the repository
- Reference this document in your request

---

**Document Version:** 1.0
**Last Updated:** 2026-04-17
**Next Review:** 2026-05-17 (30 days)
**Related Documents:**
- SECURITY_ROADMAP.md (remaining security tasks)
- Security audit report (see commit e6e2800 message)
