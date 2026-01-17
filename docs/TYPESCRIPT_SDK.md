# TypeScript SDK Generation

This document describes how to generate and use the TypeScript API client from the OpenAPI specification.

## Overview

The TypeScript SDK is automatically generated from the OpenAPI/Swagger spec exposed by the Azure Functions API. It provides type-safe access to all documented API endpoints.

## Prerequisites

- Node.js >= 22.12.0
- API running locally or accessible OpenAPI spec file

## Quick Start

### 1. Start the API

```bash
cd api
func start
```

The API should be running at `http://localhost:7071` and expose the OpenAPI spec at `/api/swagger.json`.

### 2. Generate the TypeScript Client

```bash
npm run generate:api
```

This will:
- Fetch the OpenAPI spec from `http://localhost:7071/api/swagger.json`
- Generate TypeScript types and services
- Output to `src/generated-api/`

### 3. Import and Use

```typescript
import { SportSchApiClient, SlotsService, LeaguesService } from './generated-api';

// Configure the base URL (once at app startup)
SportSchApiClient.OpenAPI.BASE = 'http://localhost:7071';

// Optional: Add custom headers
SportSchApiClient.OpenAPI.HEADERS = async () => {
  return {
    'x-league-id': localStorage.getItem('leagueId') || '',
    'x-user-id': getUserId(),
  };
};

// Use the generated services
async function getSlots(leagueId: string) {
  const response = await SlotsService.getSlots({
    leagueId,
    division: '10U',
    status: 'Pending',
  });

  return response.data.items;
}

async function createSlot(data: CreateSlotRequest) {
  const response = await SlotsService.createSlot({
    requestBody: data,
  });

  return response.data;
}
```

## Generated Structure

```
src/generated-api/
├── index.ts               # Main exports
├── core/                  # HTTP client core
│   ├── OpenAPI.ts        # Configuration (BASE URL, HEADERS)
│   ├── request.ts        # Fetch implementation
│   └── ...
├── models/               # TypeScript interfaces/types
│   ├── CreateSlotRequest.ts
│   ├── SlotDto.ts
│   ├── TeamDto.ts
│   └── ...
└── services/             # API service classes
    ├── SlotsService.ts
    ├── TeamsService.ts
    ├── LeaguesService.ts
    └── ...
```

## Configuration

### Base URL

Set the API base URL before making requests:

```typescript
import { SportSchApiClient } from './generated-api';

// Development
SportSchApiClient.OpenAPI.BASE = 'http://localhost:7071';

// Production
SportSchApiClient.OpenAPI.BASE = 'https://your-app.azurewebsites.net';
```

### Custom Headers

Add authentication or league context headers:

```typescript
SportSchApiClient.OpenAPI.HEADERS = async () => {
  const token = await getAuthToken();
  const leagueId = getCurrentLeagueId();

  return {
    'Authorization': `Bearer ${token}`,
    'x-league-id': leagueId,
    'x-user-id': getUserId(),
    'x-correlation-id': generateCorrelationId(),
  };
};
```

### Error Handling

```typescript
import { ApiError } from './generated-api';

try {
  const slots = await SlotsService.getSlots({ leagueId: 'my-league' });
} catch (error) {
  if (error instanceof ApiError) {
    console.error('API Error:', error.status, error.body);

    if (error.status === 429) {
      // Handle rate limiting
      const retryAfter = error.headers?.['retry-after'];
      console.log(`Rate limited. Retry after ${retryAfter} seconds`);
    }
  }
}
```

## Advanced Usage

### Using with React Query

```typescript
import { useQuery } from '@tanstack/react-query';
import { SlotsService } from './generated-api';

function useSlots(leagueId: string, division?: string) {
  return useQuery({
    queryKey: ['slots', leagueId, division],
    queryFn: () => SlotsService.getSlots({ leagueId, division }),
    select: (response) => response.data.items,
  });
}
```

### Type-Safe Request Building

The generated SDK provides full TypeScript types:

```typescript
import type { CreateSlotRequest, SlotDto } from './generated-api';

const request: CreateSlotRequest = {
  division: '10U',
  gameDate: '2026-06-01',
  startMin: 600,
  endMin: 720,
  parkCode: 'Park1',
  fieldCode: 'Field1',
  offeringTeamId: 'Team1',
};

const slot: SlotDto = await SlotsService.createSlot({
  requestBody: request,
});
```

## Regenerating the Client

Regenerate the client whenever the API changes:

```bash
# After adding new endpoints or modifying existing ones
npm run generate:api
```

### Using a Custom Spec URL

```bash
# Production spec
node scripts/generate-api-client.js https://your-app.azurewebsites.net/api/swagger.json

# Local spec file
node scripts/generate-api-client.js ./api-spec.json
```

## Integration with Existing API Client

If you have an existing API client (`src/lib/api.js`), you can gradually migrate:

1. **Keep existing `apiFetch` for now**
2. **Use generated client for new features**
3. **Gradually replace old calls**

Example migration:

```typescript
// Old way (src/lib/api.js)
const slots = await apiFetch('/api/slots?division=10U');

// New way (generated client)
import { SlotsService } from './generated-api';
const response = await SlotsService.getSlots({ division: '10U' });
const slots = response.data.items;
```

## Troubleshooting

### "Failed to fetch OpenAPI spec"

**Solution:**
1. Ensure API is running: `cd api && func start`
2. Verify spec URL is accessible: `curl http://localhost:7071/api/swagger.json`
3. Check that OpenAPI attributes are on all functions

### "Module not found" errors

**Solution:**
1. Ensure `openapi-typescript-codegen` is installed: `npm install`
2. Verify output directory exists: `ls src/generated-api`
3. Re-run generation: `npm run generate:api`

### Type errors in generated code

**Solution:**
1. Check TypeScript version: `npx tsc --version`
2. Update generator: `npm update openapi-typescript-codegen`
3. Regenerate client: `npm run generate:api`

## Best Practices

1. **Don't commit generated code** - The `src/generated-api/` directory is in `.gitignore`. Regenerate in CI/CD.

2. **Configure once** - Set `OpenAPI.BASE` and `OpenAPI.HEADERS` at app startup in a central location.

3. **Use TypeScript** - Leverage the generated types for compile-time safety.

4. **Handle errors consistently** - Catch `ApiError` and provide user-friendly messages.

5. **Batch updates** - Regenerate client after multiple API changes, not after each change.

## CI/CD Integration

Add to your CI pipeline:

```yaml
# .github/workflows/ci.yml
- name: Generate API Client
  run: |
    cd api && func start &
    sleep 10  # Wait for API to start
    npm run generate:api

- name: Type Check
  run: npm run type-check
```

## Support

For issues with SDK generation:
- Verify OpenAPI spec is valid: Check Swagger UI at `http://localhost:7071/api/swagger/ui`
- Review generation script: `scripts/generate-api-client.js`
- Check OpenAPI package version: `npm list openapi-typescript-codegen`
