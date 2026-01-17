# OpenAPI / Swagger Documentation

This document describes the OpenAPI/Swagger API documentation available for the SportSch API.

## Accessing the API Documentation

The API automatically exposes OpenAPI/Swagger endpoints via the `Microsoft.Azure.Functions.Worker.Extensions.OpenApi` package.

### Endpoints

**Local Development:**
- OpenAPI JSON spec: `http://localhost:7071/api/swagger.json`
- Swagger UI: `http://localhost:7071/api/swagger/ui`

**Production:**
- OpenAPI JSON spec: `https://your-function-app.azurewebsites.net/api/swagger.json`
- Swagger UI: `https://your-function-app.azurewebsites.net/api/swagger/ui`

## Coverage

The API documentation covers **42 of 43 user-facing functions** (98% coverage):

### Documented Endpoints

**Slots (3)**
- CreateSlot, GetSlots, CancelSlot

**Slot Requests (3)**
- CreateSlotRequest, ApproveSlotRequest, GetSlotRequests

**Availability Rules (9)**
- Create, Get, Update, Deactivate, Exceptions (CRUD), Preview

**Fields (4)**
- List, Create, Update, Delete

**Access Requests (5)**
- Create, ListMy, List, Approve, Deny

**Divisions (1)**
- GetDivisions

**Teams (4)**
- GetTeams, CreateTeam, PatchTeam, DeleteTeam

**Memberships (3)**
- ListMemberships, CreateMembership, PatchMembership

**Authentication (1)**
- GetMe

**Leagues (4)**
- ListLeagues, GetLeague, PatchLeague, PatchLeagueSeason

**Events (4)**
- GetEvents, CreateEvent, PatchEvent, DeleteEvent

### Excluded Functions

The following functions are intentionally excluded from public documentation:
- Admin functions (AdminWipe, AdminMigrateFields)
- Debug functions (DebugFunctions, Ping, StorageHealth)
- Internal functions (ClaimPracticeSlot, ClearAvailabilitySlots)

## Generating Client SDKs

You can generate TypeScript, C#, Python, or other client SDKs from the OpenAPI spec:

### TypeScript
```bash
# Using OpenAPI Generator
npx @openapitools/openapi-generator-cli generate \
  -i http://localhost:7071/api/swagger.json \
  -g typescript-fetch \
  -o ./src/generated-api

# Or using Swagger Codegen
swagger-codegen generate \
  -i http://localhost:7071/api/swagger.json \
  -l typescript-fetch \
  -o ./src/generated-api
```

### C#
```bash
# Using NSwag
nswag openapi2csclient \
  /input:http://localhost:7071/api/swagger.json \
  /output:Generated/ApiClient.cs \
  /namespace:GameSwap.ApiClient
```

### Python
```bash
openapi-generator-cli generate \
  -i http://localhost:7071/api/swagger.json \
  -g python \
  -o ./python-client
```

## API Schema Features

All documented endpoints include:
- **Operation metadata**: Summary, description, tags
- **Security requirements**: x-league-id header, authentication
- **Parameters**: Path, query, and header parameters with descriptions
- **Request bodies**: JSON schemas for POST/PATCH/PUT operations
- **Response schemas**: Status codes with body types and descriptions
- **Error responses**: Standardized error format with codes

## Authentication

Most endpoints require authentication via:
- `x-user-id` header: User identifier (from authentication provider)
- `x-league-id` header: League context for multi-tenant operations

## Rate Limiting

API includes rate limiting (100 requests/minute per user/IP). Rate limit info is exposed in response headers:
- `X-RateLimit-Limit`: Maximum requests allowed
- `X-RateLimit-Remaining`: Requests remaining in window
- `X-RateLimit-Reset`: Unix timestamp when window resets

See `docs/PRODUCTION_READINESS.md` for details.

## Example: Using Swagger UI

1. Start the API locally:
   ```bash
   cd api
   func start
   ```

2. Open Swagger UI in your browser:
   ```
   http://localhost:7071/api/swagger/ui
   ```

3. Explore endpoints, schemas, and try requests directly from the UI

## Maintaining Documentation

When adding new functions:

1. Add OpenAPI using statements:
   ```csharp
   using Microsoft.Azure.Functions.Worker.Extensions.OpenApi.Extensions;
   using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
   using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
   using Microsoft.OpenApi.Models;
   ```

2. Add attributes to your function:
   ```csharp
   [Function("MyFunction")]
   [OpenApiOperation(operationId: "MyFunction", tags: new[] { "Category" },
       Summary = "Short description",
       Description = "Detailed description")]
   [OpenApiSecurity("league_id_header", SecuritySchemeType.ApiKey,
       In = OpenApiSecurityLocationType.Header, Name = "x-league-id")]
   [OpenApiParameter(name: "param", In = ParameterLocation.Query,
       Required = false, Type = typeof(string), Description = "Parameter description")]
   [OpenApiRequestBody(contentType: "application/json", bodyType: typeof(RequestDto),
       Required = true, Description = "Request body description")]
   [OpenApiResponseWithBody(statusCode: HttpStatusCode.OK,
       contentType: "application/json", bodyType: typeof(ResponseDto),
       Description = "Success response")]
   public async Task<HttpResponseData> Run(...)
   ```

## Support

For issues with API documentation:
- Verify the OpenAPI extension package is installed: `Microsoft.Azure.Functions.Worker.Extensions.OpenApi`
- Check that functions have proper OpenAPI attributes
- Review Application Insights logs for runtime errors
