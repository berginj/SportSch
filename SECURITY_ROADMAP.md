# Security Roadmap

This document outlines the remaining security tasks identified in the security audit and provides prioritization and implementation guidance.

## Completed (Tasks 1-8)

✅ **Critical & High Priority Tasks - COMPLETED**

1. ✅ Store API keys as SHA256 hashes instead of plaintext
2. ✅ Fix dev header bypass (require Development AND localhost)
3. ✅ Add comprehensive security headers (CSP, HSTS, X-Frame-Options, etc.)
4. ✅ Implement distributed rate limiting with Redis
5. ✅ Fix X-Forwarded-For handling to prevent spoofing
6. ✅ Add request size limits (10MB global, 5MB CSV)
7. ✅ Add file type validation for CSV uploads
8. ✅ Enforce HTTPS with HSTS headers

**Completed Date:** 2026-04-17
**Commit:** `e6e2800` - "Implement critical security fixes and enhancements"

---

## Medium Term (1-3 Months)

### 9. Migrate Secrets to Azure Key Vault

**Priority:** Medium
**Effort:** 2-3 days
**Impact:** Improves secrets management, audit trail, and rotation capabilities

**Current State:**
- Secrets stored in environment variables/app settings:
  - `SENDGRID_API_KEY`
  - `AAD_CLIENT_SECRET`
  - `GOOGLE_CLIENT_SECRET`
  - `REDIS_CONNECTION_STRING`

**Implementation Plan:**

1. **Setup Azure Key Vault**
   ```bash
   # Create Key Vault
   az keyvault create \
     --name sportsch-secrets \
     --resource-group <your-rg> \
     --location <your-location>

   # Enable Managed Identity for Function App
   az functionapp identity assign \
     --name <your-function-app> \
     --resource-group <your-rg>
   ```

2. **Grant Function App Access**
   ```bash
   # Get the principal ID from previous command
   az keyvault set-policy \
     --name sportsch-secrets \
     --object-id <principal-id> \
     --secret-permissions get list
   ```

3. **Migrate Secrets to Key Vault**
   ```bash
   # Add secrets to Key Vault
   az keyvault secret set --vault-name sportsch-secrets --name SendGridApiKey --value "<value>"
   az keyvault secret set --vault-name sportsch-secrets --name AadClientSecret --value "<value>"
   az keyvault secret set --vault-name sportsch-secrets --name GoogleClientSecret --value "<value>"
   az keyvault secret set --vault-name sportsch-secrets --name RedisConnectionString --value "<value>"
   ```

4. **Update Application Configuration**

   Add to `Program.cs`:
   ```csharp
   using Azure.Identity;
   using Azure.Security.KeyVault.Secrets;

   // In ConfigureServices
   var keyVaultUrl = new Uri(Environment.GetEnvironmentVariable("KEY_VAULT_URL")
       ?? "https://sportsch-secrets.vault.azure.net/");
   services.AddSingleton(new SecretClient(keyVaultUrl, new DefaultAzureCredential()));
   ```

   Update `EmailService.cs`:
   ```csharp
   public EmailService(
       IConfiguration configuration,
       SecretClient secretClient,
       // ... other params
   )
   {
       // Try Key Vault first, fall back to config
       try
       {
           var secret = await secretClient.GetSecretAsync("SendGridApiKey");
           sendGridApiKey = secret.Value.Value;
       }
       catch
       {
           sendGridApiKey = configuration["SENDGRID_API_KEY"];
       }
   }
   ```

5. **Update Function App Settings**
   ```bash
   # Reference Key Vault secrets in app settings
   az functionapp config appsettings set \
     --name <your-function-app> \
     --resource-group <your-rg> \
     --settings KEY_VAULT_URL=https://sportsch-secrets.vault.azure.net/
   ```

6. **Remove Environment Variables**
   - Remove plaintext secrets from Function App configuration
   - Update documentation to reference Key Vault

**Testing:**
- Verify email sending still works
- Verify OAuth login still works
- Check Application Insights for any Key Vault access errors

**Files to Modify:**
- `api/Program.cs` - Add SecretClient registration
- `api/Services/EmailService.cs` - Use Key Vault
- `staticwebapp.config.json` - May need Key Vault references for OAuth
- `api/GameSwap_Functions.csproj` - Add `Azure.Security.KeyVault.Secrets` package

---

### 10. Add Input Length Validation

**Priority:** Medium
**Effort:** 1 day
**Impact:** Prevents database bloat and potential DoS

**Implementation Plan:**

1. **Add Validation Attributes Package**
   ```xml
   <!-- In GameSwap_Functions.csproj -->
   <PackageReference Include="System.ComponentModel.Annotations" Version="5.0.0" />
   ```

2. **Update Request Models**
   ```csharp
   // In CreateSlot.cs and other function request models
   public record CreateSlotReq(
       [StringLength(100, ErrorMessage = "Division name too long")]
       string? division,

       [StringLength(100, ErrorMessage = "Team ID too long")]
       string? offeringTeamId,

       [StringLength(50, ErrorMessage = "Field key too long")]
       string? fieldKey,

       [StringLength(200, ErrorMessage = "Park name too long")]
       string? parkName,

       [StringLength(200, ErrorMessage = "Field name too long")]
       string? fieldName,

       [StringLength(2000, ErrorMessage = "Notes exceed maximum length of 2000 characters")]
       string? notes,

       // ... other fields
   );
   ```

3. **Add Service Layer Validation**
   ```csharp
   // In SlotService.cs
   public async Task<GameSlotDto> CreateSlotAsync(CreateSlotRequest request, CorrelationContext? context = null)
   {
       // Validate input lengths
       if (request.Notes?.Length > 2000)
       {
           throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST,
               "Notes field exceeds maximum length of 2000 characters");
       }

       if (request.ParkName?.Length > 200)
       {
           throw new ApiGuards.HttpError(400, ErrorCodes.BAD_REQUEST,
               "Park name exceeds maximum length of 200 characters");
       }

       // ... existing logic
   }
   ```

4. **Define Constants**
   ```csharp
   // In api/Storage/Constants.cs
   public static class ValidationLimits
   {
       public const int MaxDivisionLength = 100;
       public const int MaxTeamIdLength = 100;
       public const int MaxFieldKeyLength = 50;
       public const int MaxParkNameLength = 200;
       public const int MaxFieldNameLength = 200;
       public const int MaxNotesLength = 2000;
       public const int MaxEmailLength = 254; // RFC 5321
       public const int MaxUserNameLength = 100;
   }
   ```

**Files to Modify:**
- `api/Functions/CreateSlot.cs`
- `api/Functions/UpdateSlot.cs`
- `api/Functions/TeamsFunctions.cs`
- `api/Functions/FieldsFunctions.cs`
- `api/Services/SlotService.cs`
- `api/Services/RequestService.cs`
- `api/Storage/Constants.cs`

**Testing:**
- Try creating slots with very long notes (>2000 chars)
- Verify appropriate error messages returned
- Test boundary conditions (exactly at limit)

---

### 11. Review Global Admin Privileges

**Priority:** Medium
**Effort:** 2-3 days
**Impact:** Reduces blast radius of compromised global admin account

**Current Risk:**
- Global admins automatically get LeagueAdmin role for ALL leagues
- No additional checks or scoped permissions
- Single compromised account affects entire system

**Implementation Plan:**

1. **Create Granular Permissions**
   ```csharp
   // In api/Storage/Constants.cs
   public static class GlobalPermissions
   {
       public const string ManageGlobalAdmins = "global:admins:manage";
       public const string ViewAllLeagues = "global:leagues:view";
       public const string ManageApiKeys = "global:apikeys:manage";
       public const string ViewAuditLogs = "global:audit:view";
       public const string ManageSystemSettings = "global:settings:manage";
   }
   ```

2. **Add Permission Tracking Table**
   ```csharp
   // Table: GameSwapGlobalAdminPermissions
   // PK: userId
   // RK: permission (e.g., "global:admins:manage")
   // Fields: GrantedBy, GrantedUtc, ExpiresUtc
   ```

3. **Update Authorization Service**
   ```csharp
   // In AuthorizationService.cs
   public async Task<bool> HasGlobalPermissionAsync(string userId, string permission)
   {
       if (!await _membershipRepo.IsGlobalAdminAsync(userId))
           return false;

       // Check specific permission
       var permissionTable = await _tableService.GetTableAsync("GameSwapGlobalAdminPermissions");
       try
       {
           var entity = await permissionTable.GetEntityAsync<TableEntity>(userId, permission);

           // Check expiration
           var expiresUtc = entity.GetDateTimeOffset("ExpiresUtc");
           if (expiresUtc.HasValue && expiresUtc.Value < DateTimeOffset.UtcNow)
               return false;

           return true;
       }
       catch (RequestFailedException ex) when (ex.Status == 404)
       {
           return false; // Permission not granted
       }
   }
   ```

4. **Require MFA for Sensitive Operations**
   ```csharp
   // In ApiKeyManagementFunctions.cs
   [Function("RotateApiKeys")]
   public async Task<HttpResponseData> RotateKeys([HttpTrigger...] HttpRequestData req)
   {
       var me = IdentityUtil.GetMe(req);

       // Check global admin with specific permission
       if (!await _authService.HasGlobalPermissionAsync(me.UserId, GlobalPermissions.ManageApiKeys))
       {
           return ApiResponses.Error(req, HttpStatusCode.Forbidden,
               ErrorCodes.FORBIDDEN, "Missing required permission: global:apikeys:manage");
       }

       // TODO: Add MFA verification check here
       // var mfaToken = req.Headers.TryGetValues("X-MFA-Token", out var tokens) ? tokens.FirstOrDefault() : null;
       // if (!await _mfaService.VerifyTokenAsync(me.UserId, mfaToken))
       //     return ApiResponses.Error(req, 401, "MFA_REQUIRED", "Multi-factor authentication required");

       // ... existing logic
   }
   ```

5. **Enhanced Audit Logging**
   ```csharp
   // Log all global admin actions with detailed context
   await _auditLogger.LogGlobalAdminActionAsync(new GlobalAdminAuditEntry
   {
       UserId = me.UserId,
       Action = "API_KEY_ROTATION",
       Permission = GlobalPermissions.ManageApiKeys,
       SourceIp = GetClientIp(req),
       UserAgent = req.Headers.TryGetValues("User-Agent", out var ua) ? ua.FirstOrDefault() : null,
       Timestamp = DateTimeOffset.UtcNow,
       Success = true
   });
   ```

**Files to Modify:**
- `api/Services/AuthorizationService.cs`
- `api/Services/IAuthorizationService.cs`
- `api/Functions/GlobalAdminsFunctions.cs`
- `api/Functions/ApiKeyManagementFunctions.cs`
- `api/Storage/Constants.cs`
- `api/Services/AuditLogger.cs`

---

### 12. Configure Session Timeouts

**Priority:** Medium
**Effort:** 1 day
**Impact:** Reduces risk of session hijacking

**Implementation Plan:**

1. **Configure EasyAuth Session Timeout**

   Update `staticwebapp.config.json`:
   ```json
   {
     "auth": {
       "identityProviders": { ... },
       "login": {
         "tokenRefreshExtensionHours": 72,
         "cookieExpirationDays": 7
       }
     }
   }
   ```

2. **Add Session Validation Middleware**
   ```csharp
   // In api/Middleware/SessionValidationMiddleware.cs
   public class SessionValidationMiddleware : IFunctionsWorkerMiddleware
   {
       private const int MaxSessionAgeHours = 8; // Force re-auth after 8 hours

       public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
       {
           var req = await context.GetHttpRequestDataAsync();
           if (req == null)
           {
               await next(context);
               return;
           }

           var me = IdentityUtil.GetMe(req);

           // Check session age from custom claim if available
           // Azure AD tokens have 'iat' (issued at) claim
           // Validate session hasn't exceeded max age

           await next(context);
       }
   }
   ```

3. **Add Client-Side Session Monitoring**

   Update `src/lib/api.js`:
   ```javascript
   // Track last activity
   let lastActivityTime = Date.now();
   const SESSION_TIMEOUT_MS = 8 * 60 * 60 * 1000; // 8 hours

   export async function apiFetch(path, options = {}) {
       // Check session timeout
       if (Date.now() - lastActivityTime > SESSION_TIMEOUT_MS) {
           // Redirect to login
           window.location.href = '/.auth/login/aad';
           return;
       }

       lastActivityTime = Date.now();

       // ... existing logic
   }
   ```

**Files to Modify:**
- `staticwebapp.config.json`
- `api/Middleware/SessionValidationMiddleware.cs` (new)
- `api/Program.cs`
- `src/lib/api.js`

---

## Long Term (3-6 Months)

### 13. Reduce Error Verbosity in Production

**Priority:** Low
**Effort:** 1 day
**Impact:** Prevents information disclosure

**Implementation Plan:**

1. **Add Environment Detection**
   ```csharp
   // In api/Storage/ApiResponses.cs
   private static bool IsProduction()
   {
       var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT")
           ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
           ?? "Production";
       return !env.Equals("Development", StringComparison.OrdinalIgnoreCase);
   }

   public static HttpResponseData Error(
       HttpRequestData req,
       HttpStatusCode status,
       string code,
       string message,
       object? details = null)
   {
       // In production, sanitize error messages
       if (IsProduction() && (int)status >= 400 && (int)status < 500)
       {
           // Remove technical details from 4xx errors in production
           details = null;

           // Sanitize message to remove internal paths/technical details
           message = SanitizeErrorMessage(message);
       }

       return HttpUtil.Json(req, status, new
       {
           error = new
           {
               code,
               message,
               details = BuildErrorDetails(req, status, details),
           }
       });
   }

   private static string SanitizeErrorMessage(string message)
   {
       // Remove file paths
       message = Regex.Replace(message, @"[A-Z]:\\[^:]+", "[path]");
       message = Regex.Replace(message, @"/[/\w]+/[/\w]+", "[path]");

       // Remove internal IPs
       message = Regex.Replace(message, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", "[ip]");

       return message;
   }
   ```

**Files to Modify:**
- `api/Storage/ApiResponses.cs`

---

### 14. Implement CSRF Tokens

**Priority:** Low
**Effort:** 2 days
**Impact:** Defense in depth (EasyAuth already provides protection)

**Note:** Azure Static Web Apps EasyAuth already provides CSRF protection through:
- SameSite cookies
- CORS restrictions
- Origin validation

However, for defense in depth:

**Implementation Plan:**

1. **Generate CSRF Tokens**
   ```csharp
   // In api/Services/CsrfTokenService.cs
   public class CsrfTokenService
   {
       public string GenerateToken(string userId)
       {
           var tokenBytes = RandomNumberGenerator.GetBytes(32);
           var token = Convert.ToBase64String(tokenBytes);

           // Store in Redis with user association
           // Key: csrf:{userId}, Value: token, TTL: 1 hour

           return token;
       }

       public async Task<bool> ValidateTokenAsync(string userId, string token)
       {
           // Retrieve from Redis and compare
           // Use constant-time comparison
       }
   }
   ```

2. **Add CSRF Middleware**
   ```csharp
   // Only validate for state-changing operations (POST, PUT, PATCH, DELETE)
   ```

**Files to Create:**
- `api/Services/CsrfTokenService.cs`
- `api/Middleware/CsrfValidationMiddleware.cs`

---

### 15. Fix Timing Attacks in Key Comparison

**Priority:** Low
**Effort:** 1 hour
**Impact:** Prevents side-channel attacks

**Implementation Plan:**

Update `api/Services/ApiKeyService.cs`:
```csharp
private static bool ConstantTimeEquals(string a, string b)
{
    if (a == null || b == null)
        return a == b;

    if (a.Length != b.Length)
        return false;

    int result = 0;
    for (int i = 0; i < a.Length; i++)
    {
        result |= a[i] ^ b[i];
    }

    return result == 0;
}

public async Task<bool> ValidateKeyAsync(string apiKey)
{
    if (string.IsNullOrWhiteSpace(apiKey))
        return false;

    try
    {
        var keys = await GetActiveKeysAsync();
        var providedKeyHash = HashApiKey(apiKey);

        // Use constant-time comparison
        return ConstantTimeEquals(providedKeyHash, keys.PrimaryKey) ||
               ConstantTimeEquals(providedKeyHash, keys.SecondaryKey);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to validate API key");
        return false;
    }
}
```

**Files to Modify:**
- `api/Services/ApiKeyService.cs`

---

### 16. Strengthen Content Security Policy

**Priority:** Low
**Effort:** 1-2 days
**Impact:** Additional XSS protection

**Current CSP:** Allows `unsafe-inline` for scripts and styles

**Implementation Plan:**

1. **Remove unsafe-inline**
   - Move inline scripts to external files
   - Use nonces or hashes for required inline scripts

2. **Update staticwebapp.config.json:**
   ```json
   {
     "globalHeaders": {
       "Content-Security-Policy": "default-src 'self'; script-src 'self' 'nonce-{RANDOM}' https://www.googletagmanager.com; style-src 'self' 'nonce-{RANDOM}'; img-src 'self' data: https:; connect-src 'self' https://*.azurestaticapps.net https://*.fifthseasonadvisors.com https://*.applicationinsights.azure.com; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; upgrade-insecure-requests"
     }
   }
   ```

3. **Generate nonces in index.html dynamically**

**Files to Modify:**
- `staticwebapp.config.json`
- `index.html` (move inline scripts)

---

### 17. Add Log Sanitization

**Priority:** Low
**Effort:** 1 day
**Impact:** Prevents log injection attacks

**Implementation Plan:**

1. **Create Log Sanitizer**
   ```csharp
   // In api/Services/LogSanitizer.cs
   public static class LogSanitizer
   {
       public static string Sanitize(string input)
       {
           if (string.IsNullOrEmpty(input))
               return input;

           // Remove newlines to prevent log injection
           input = input.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

           // Remove control characters
           input = Regex.Replace(input, @"[\x00-\x1F\x7F]", "");

           // Truncate to reasonable length
           if (input.Length > 500)
               input = input.Substring(0, 497) + "...";

           return input;
       }
   }
   ```

2. **Update Logging Statements**
   ```csharp
   // Before
   _logger.LogWarning("Failed login for user {UserId}", userId);

   // After
   _logger.LogWarning("Failed login for user {UserId}", LogSanitizer.Sanitize(userId));
   ```

3. **Use Structured Logging**
   ```csharp
   // Prefer structured logging over string interpolation
   _logger.LogInformation("Slot created: {SlotId} by {UserId} in {LeagueId}",
       slotId, userId, leagueId);
   ```

**Files to Create:**
- `api/Services/LogSanitizer.cs`

**Files to Modify:**
- All logging statements in services and functions

---

## Summary of Remaining Tasks

| Task | Priority | Effort | Timeline | Risk Reduction |
|------|----------|--------|----------|----------------|
| 9. Migrate to Key Vault | Medium | 2-3 days | 1-3 months | High |
| 10. Input Length Validation | Medium | 1 day | 1-3 months | Medium |
| 11. Review Global Admin | Medium | 2-3 days | 1-3 months | Medium |
| 12. Session Timeouts | Medium | 1 day | 1-3 months | Medium |
| 13. Reduce Error Verbosity | Low | 1 day | 3-6 months | Low |
| 14. CSRF Tokens | Low | 2 days | 3-6 months | Low |
| 15. Fix Timing Attacks | Low | 1 hour | 3-6 months | Low |
| 16. Strengthen CSP | Low | 1-2 days | 3-6 months | Low |
| 17. Log Sanitization | Low | 1 day | 3-6 months | Low |

**Total Remaining Effort:** ~12-15 days
**Recommended Completion:** Within 6 months

---

## Monitoring & Maintenance

After implementing all tasks:

1. **Security Scanning**
   - Run weekly dependency scans: `npm audit`, `dotnet list package --vulnerable`
   - Use GitHub Dependabot for automated alerts

2. **Penetration Testing**
   - Annual third-party penetration test
   - Quarterly internal security reviews

3. **Compliance Checks**
   - GDPR compliance review
   - SOC 2 readiness if applicable

4. **Incident Response**
   - Document security incident procedures
   - Test incident response quarterly

---

**Last Updated:** 2026-04-17
**Next Review Date:** 2026-07-17 (3 months)
