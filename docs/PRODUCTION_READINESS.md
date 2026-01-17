# Production Readiness Features

This document describes the production-ready features implemented in the API.

## Rate Limiting

**Location:** `api/Middleware/RateLimitingMiddleware.cs`

### Configuration
- **Limit:** 100 requests per minute per user/IP
- **Window:** 60-second sliding window
- **Algorithm:** Sliding window with in-memory storage

### How It Works
1. Identifies requests by user ID (from `x-user-id` header) or IP address
2. Tracks request timestamps in a 60-second sliding window
3. Rejects requests exceeding the limit with HTTP 429 (Too Many Requests)
4. Adds rate limit headers to all responses:
   - `X-RateLimit-Limit`: Maximum requests allowed
   - `X-RateLimit-Remaining`: Requests remaining in current window
   - `X-RateLimit-Reset`: Unix timestamp when the window resets
   - `Retry-After`: Seconds to wait before retrying (on 429 responses)

### Response on Rate Limit Exceeded
```json
HTTP/1.1 429 Too Many Requests
X-RateLimit-Limit: 100
X-RateLimit-Remaining: 0
X-RateLimit-Reset: 1737140400
Retry-After: 60

{
  "error": {
    "code": "RATE_LIMIT_EXCEEDED",
    "message": "Rate limit exceeded. Maximum 100 requests per minute allowed."
  }
}
```

### Scaling Considerations
- Current implementation uses in-memory storage
- For distributed systems (multiple instances), replace with Redis:
  - Use `StackExchange.Redis` for distributed rate limiting
  - Store windows in Redis with TTL
  - Use Redis Sorted Sets for efficient window management

### Adjusting Limits
To change rate limits, modify constants in `RateLimitingMiddleware.cs`:
```csharp
private const int MaxRequestsPerMinute = 100;  // Requests per window
private const int WindowSizeSeconds = 60;       // Window duration
```

## CORS Configuration

**Location:** `api/host.json`

### Allowed Origins
```json
"allowedOrigins": [
  "https://your-production-domain.com",
  "https://www-your-production-domain.com",
  "http://localhost:5173",
  "http://127.0.0.1:5173"
]
```

**⚠️ IMPORTANT:** Update these domains with your actual production URLs before deployment.

### Allowed Methods
- GET, POST, PUT, PATCH, DELETE, OPTIONS

### Custom Headers
**Request Headers Allowed:**
- `Content-Type`
- `Authorization`
- `x-league-id` - League context header
- `x-user-id` - User identification
- `x-correlation-id` - Request tracing

**Response Headers Exposed:**
- `X-RateLimit-Limit`
- `X-RateLimit-Remaining`
- `X-RateLimit-Reset`

### Credentials
- `allowCredentials: true` - Allows cookies and authentication headers
- `maxAge: 3600` - Preflight cache duration (1 hour)

## Audit Logging

**Location:** `api/Services/AuditLogger.cs`

### Features
- Structured logging with correlation IDs
- Logs all sensitive operations:
  - Slot creation and cancellation
  - Request approvals and denials
  - Role changes
  - Bulk operations
  - Data exports
  - Field modifications
  - Membership changes
  - Configuration updates

### Query in Application Insights
```kusto
traces
| where message contains "AUDIT:"
| where customDimensions.CorrelationId == "your-correlation-id"
| order by timestamp desc
```

### Log Levels
- `LogInformation` - Standard operations (slot creation, approvals)
- `LogWarning` - Critical events (role changes, exports, config changes)

## Security Best Practices

### Implemented
✅ Rate limiting to prevent abuse
✅ CORS configuration for origin control
✅ Audit logging for compliance
✅ Correlation IDs for distributed tracing
✅ Structured error responses
✅ ETag concurrency control
✅ Input validation on all endpoints

### Recommended Next Steps
- [ ] Add API key rotation strategy
- [ ] Implement request signing for critical operations
- [ ] Add anomaly detection for suspicious patterns
- [ ] Set up Application Insights alerts
- [ ] Configure Azure AD authentication
- [ ] Add data encryption at rest
- [ ] Implement field-level authorization

## Monitoring

### Key Metrics to Track
1. **Rate Limiting**
   - Rate limit hits per user/IP
   - 429 response rate
   - Average requests per user

2. **Performance**
   - Response times (p50, p95, p99)
   - Function execution duration
   - Cold start frequency

3. **Security**
   - Failed authentication attempts
   - Unauthorized access attempts (403s)
   - Suspicious patterns

4. **Errors**
   - 5xx error rate
   - Exception frequency by type
   - Failed dependency calls

### Application Insights Queries

**Rate Limit Monitoring:**
```kusto
requests
| where resultCode == "429"
| summarize count() by bin(timestamp, 5m), tostring(customDimensions.Identifier)
| render timechart
```

**Audit Trail:**
```kusto
traces
| where message startswith "AUDIT:"
| project timestamp, message,
    UserId = tostring(customDimensions.UserId),
    LeagueId = tostring(customDimensions.LeagueId),
    CorrelationId = tostring(customDimensions.CorrelationId)
```

**Performance Baseline:**
```kusto
requests
| where success == true
| summarize
    p50 = percentile(duration, 50),
    p95 = percentile(duration, 95),
    p99 = percentile(duration, 99)
  by operation_Name
```

## Deployment Checklist

Before deploying to production:

- [ ] Update CORS `allowedOrigins` with actual domain names
- [ ] Configure Application Insights connection string
- [ ] Set up Azure Storage connection string
- [ ] Enable Azure AD authentication
- [ ] Configure environment-specific settings
- [ ] Test rate limiting with load testing tool
- [ ] Verify audit logging is working
- [ ] Set up monitoring alerts
- [ ] Document incident response procedures
- [ ] Perform security review
- [ ] Load test with production-like traffic

## Testing Rate Limiting

```bash
# Test rate limit with curl
for i in {1..110}; do
  curl -i http://localhost:7071/api/slots \
    -H "x-league-id: test-league" \
    -H "x-user-id: test-user-$i"
  sleep 0.1
done
```

Expected: First 100 requests succeed, subsequent requests return 429.

## Support

For questions or issues:
- Check Application Insights logs
- Review audit trail for security events
- Monitor rate limit headers in responses
- Contact development team with correlation IDs
