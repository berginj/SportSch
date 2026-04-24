# calendarmerge

`calendarmerge` is a small Azure Functions v4 service that merges multiple source ICS feeds into one published ICS file in Azure Blob Storage.

After the merge completes, it also publishes a read-only public calendar experience for the world to view through Schedule-X. That public view is available in two continuously refreshed modes:

- a full public calendar
- a games-only public calendar

Those public artifacts keep full location text, but strip attendees and direct contact information from the published events.

It publishes:

- `calendar.ics`
- `calendar-public.ics`
- `calendar-games.ics`
- `schedule-x-full.json`
- `schedule-x-games.json`
- `status.json`

It exposes:

- a timer-triggered refresh job
- an HTTP manual refresh endpoint
- an HTTP health/status endpoint
- an HTTP calendar management endpoint for listing and uploading calendars to sync

The static website entrypoint at `public/index.html` now renders the public read-only Schedule-X viewer and lets visitors switch between the full and games-only calendars.

## Architecture

- Azure Functions handles scheduled and manual refreshes.
- Merge logic is implemented as pure TypeScript library code under `src/lib/`.
- Azure Blob Storage stores the public outputs in `$web/`.
- Uploaded calendars are stored as private blobs plus small manifest files in a separate container/prefix and included in subsequent refreshes.
- `status.json` is written on every run as a sanitized public health document, including the public output URLs.
- Detailed operator diagnostics are written to a private blob and exposed only through the authenticated status detail endpoint.
- `calendar.ics` is only replaced when all feeds succeed, or when there is no previous good calendar and at least one feed succeeds.
- The public Schedule-X JSON feeds and sanitized public ICS feeds are refreshed together with `calendar.ics` when a publish succeeds.
- If some feeds fail and a prior `calendar.ics` already exists, the service keeps that last known good file and records detailed errors in the operator status document.

## Config

The app reads configuration from Azure Functions app settings or `local.settings.json`.

Required:

- `OUTPUT_STORAGE_ACCOUNT`

Supported settings:

| Setting | Required | Default | Notes |
| --- | --- | --- | --- |
| `SOURCE_FEEDS_JSON` | No | `[]` | JSON array of feed objects or URLs for statically configured remote feeds. |
| `OUTPUT_STORAGE_ACCOUNT` | Yes | none | Azure Storage account used for published output. |
| `OUTPUT_CONTAINER` | No | `$web` | Blob container for published files. |
| `OUTPUT_BLOB_PATH` | No | `calendar.ics` | Public merged calendar path. |
| `PUBLIC_CALENDAR_BLOB_PATH` | No | `calendar-public.ics` | Sanitized public ICS path for the full calendar. |
| `PUBLIC_GAMES_CALENDAR_BLOB_PATH` | No | `calendar-games.ics` | Sanitized public ICS path for the games-only calendar. |
| `SCHEDULE_X_FULL_BLOB_PATH` | No | `schedule-x-full.json` | Schedule-X JSON payload for the full public calendar. |
| `SCHEDULE_X_GAMES_BLOB_PATH` | No | `schedule-x-games.json` | Schedule-X JSON payload for the games-only public calendar. |
| `STATUS_BLOB_PATH` | No | `status.json` | Public sanitized status path. |
| `OPERATOR_STATUS_CONTAINER` | No | `sources` | Private container for detailed operator diagnostics. |
| `OPERATOR_STATUS_BLOB_PATH` | No | `_system/status-detail.json` | Private detailed status path. |
| `UPLOADED_SOURCES_CONTAINER` | No | `sources` | Private container used to store uploaded calendars. |
| `UPLOADED_SOURCES_PREFIX` | No | `uploads` | Prefix inside the uploaded calendar container. |
| `REFRESH_LOCK_CONTAINER` | No | `sources` | Private container used for the distributed refresh lease. |
| `REFRESH_LOCK_BLOB_PATH` | No | `_system/refresh.lock` | Private blob used as the distributed refresh lock. |
| `REFRESH_SCHEDULE` | No | `0 */15 * * * *` | Azure Functions NCRONTAB schedule. |
| `FETCH_TIMEOUT_MS` | No | `10000` | Per-request timeout. |
| `FETCH_RETRY_COUNT` | No | `2` | Retry count after the initial attempt. |
| `FETCH_RETRY_DELAY_MS` | No | `750` | Base retry backoff in milliseconds. |
| `MAX_UPLOAD_BYTES` | No | `5242880` | Maximum accepted upload body size in bytes. |
| `MAX_FETCH_BYTES` | No | `5242880` | Maximum remote feed size in bytes. |
| `SERVICE_NAME` | No | `calendarmerge` | Included in logs and status documents. |

Example `SOURCE_FEEDS_JSON`:

```json
[
  {
    "id": "school",
    "name": "School Calendar",
    "url": "https://example.com/school.ics"
  },
  {
    "id": "athletics",
    "name": "Athletics",
    "url": "https://example.com/athletics.ics"
  }
]
```

Use `[]` when you want an upload-only deployment and plan to add calendars through the API instead of environment settings.

## Local Dev

Prerequisites:

- Node.js 20+
- npm
- Azure Functions Core Tools v4
- Azure CLI
- Either Azurite or a real `AzureWebJobsStorage` connection string for local Functions runtime storage

Setup:

```powershell
Copy-Item local.settings.example.json local.settings.json
npm ci
npm run build
```

Run tests:

```powershell
npm test
```

Run locally:

```powershell
func start
```

Local endpoints:

- `GET http://localhost:7071/api/status`
- `GET http://localhost:7071/api/status/detail?code=...`
- `POST http://localhost:7071/api/refresh`
- `GET http://localhost:7071/api/calendars`
- `POST http://localhost:7071/api/calendars`
- `DELETE http://localhost:7071/api/calendars/{id}`

## Provision Azure

The bootstrap script assumes you are already signed in with `az login`.

Set the requested placeholders:

```powershell
$env:AZ_SUBSCRIPTION_ID = "AZ_SUBSCRIPTION_ID"
$env:AZ_LOCATION = "AZ_LOCATION"
$env:AZ_RESOURCE_GROUP = "AZ_RESOURCE_GROUP"
$env:AZ_STORAGE_ACCOUNT = "AZ_STORAGE_ACCOUNT"
$env:AZ_FUNCTIONAPP_NAME = "AZ_FUNCTIONAPP_NAME"
$env:AZ_APPINSIGHTS_NAME = "AZ_APPINSIGHTS_NAME"
$env:SOURCE_FEEDS_JSON = "[]"
$env:MAX_UPLOAD_BYTES = "5242880"
$env:MAX_FETCH_BYTES = "5242880"
```

Provision infrastructure:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\azure\bootstrap.ps1
```

What the bootstrap script does:

- runs `az group create`
- deploys `infra/main.bicep`
- creates the Function App with `az functionapp create`
- sets app settings with `az functionapp config appsettings set`
- grants the Function App managed identity `Storage Blob Data Contributor`
- enables blob static website hosting with `az storage blob service-properties update`
- uploads `public/index.html`

## Deploy Functions

Build, package, and zip-deploy the Functions app:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\azure\deploy-functions.ps1
```

That script:

- runs `npm ci`
- runs `npm run build`
- creates a clean deployment package under `.artifacts/`
- installs production dependencies into the staged package
- deploys with `az functionapp deployment source config-zip`
- uploads the latest `public/index.html` to the static website container

## CI/CD

This repo now includes a repo-root workflow at `../.github/workflows/calendarmerge-functions.yml` that:

- installs dependencies
- runs tests
- builds the deployment zip
- logs into Azure with GitHub OIDC
- deploys the backend with `az functionapp deployment source config-zip`

Configure these GitHub secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Configure these GitHub repository variables:

- `AZ_RESOURCE_GROUP`
- `AZ_FUNCTIONAPP_NAME`

## Public URLs

Discover the static website base URL:

```powershell
$web = az storage account show `
  --resource-group $env:AZ_RESOURCE_GROUP `
  --name $env:AZ_STORAGE_ACCOUNT `
  --query "primaryEndpoints.web" `
  --output tsv
```

Public output URLs:

- `$($web.TrimEnd('/'))/calendar.ics`
- `$($web.TrimEnd('/'))/calendar-public.ics`
- `$($web.TrimEnd('/'))/calendar-games.ics`
- `$($web.TrimEnd('/'))/schedule-x-full.json`
- `$($web.TrimEnd('/'))/schedule-x-games.json`
- `$($web.TrimEnd('/'))/status.json`
- `$($web.TrimEnd('/'))/index.html`

Blob paths written by the app:

- `$web/calendar.ics`
- `$web/calendar-public.ics`
- `$web/calendar-games.ics`
- `$web/schedule-x-full.json`
- `$web/schedule-x-games.json`
- `$web/status.json`

Function endpoints:

- `https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/status`
- `https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/status/detail`
- `https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/refresh`
- `https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/calendars`
- `https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/calendars/{id}`

Manual refresh uses Function auth. Retrieve a key and invoke it like this:

```powershell
$refreshKey = az functionapp keys list `
  --resource-group $env:AZ_RESOURCE_GROUP `
  --name $env:AZ_FUNCTIONAPP_NAME `
  --query "functionKeys.default" `
  --output tsv

Invoke-WebRequest `
  -Method POST `
  -Uri "https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/refresh?code=$refreshKey"
```

## Calendar Upload API

The calendar management API uses Function auth. Reuse the same function key from the manual refresh example and call `/api/calendars`.

Write actions are explicit:

- `action=create` creates a new uploaded calendar and returns `409` if the id already exists.
- `action=replace` updates an existing uploaded calendar and returns `404` if the id does not exist.
- `action=upsert` creates or replaces and is the default when `action` is omitted.

Uploads larger than `MAX_UPLOAD_BYTES` are rejected with `413`. When a refresh is already running, upload-triggered refresh returns `202` with `refresh.inFlight=true`.
Delete uses the same refresh contract and removes only uploaded calendars, never statically configured remote feeds.

List currently configured and uploaded sources:

```powershell
Invoke-WebRequest `
  -Uri "https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/calendars?code=$refreshKey"
```

Upload raw ICS text and refresh immediately:

```powershell
Invoke-WebRequest `
  -Method POST `
  -Uri "https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/calendars?id=school-events&name=School%20Events&action=create&code=$refreshKey" `
  -ContentType "text/calendar" `
  -InFile ".\school-events.ics"
```

Upload JSON instead:

```powershell
$payload = @{
  id = "school-events"
  name = "School Events"
  action = "replace"
  calendarText = [System.IO.File]::ReadAllText(".\school-events.ics")
  refresh = $true
} | ConvertTo-Json

Invoke-WebRequest `
  -Method POST `
  -Uri "https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/calendars?code=$refreshKey" `
  -ContentType "application/json" `
  -Body $payload
```

Set `refresh=false` if you want to upload first and let the timer job pick it up later.

Delete an uploaded calendar and refresh immediately:

```powershell
Invoke-WebRequest `
  -Method DELETE `
  -Uri "https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/calendars/school-events?code=$refreshKey"
```

Delete first and let the timer refresh later:

```powershell
Invoke-WebRequest `
  -Method DELETE `
  -Uri "https://$env:AZ_FUNCTIONAPP_NAME.azurewebsites.net/api/calendars/school-events?refresh=false&code=$refreshKey"
```

## Testing Notes

The unit tests cover:

- duplicate raw UIDs across feeds
- deterministic fallback dedupe when UID is missing
- all-day event preservation
- cancelled event precedence
- malformed ICS input rejection
- public event sanitization for attendee and organizer fields
- games-only public filtering for Schedule-X output

## Rollback And Troubleshooting

- Partial feed failures do not overwrite an existing `calendar.ics`; check `/api/status/detail` for per-feed errors and keep using public `status.json` for a sanitized health summary.
- Full failures write a sanitized public `status.json`, write detailed operator diagnostics privately, and keep the existing `calendar.ics` untouched.
- If publishing fails because the Function App cannot write blobs, confirm the managed identity still has `Storage Blob Data Contributor` on the storage account.
- If local runs fail on storage bindings, set `AzureWebJobsStorage` in `local.settings.json` to Azurite or a real storage connection string.
- To roll back code, redeploy a previous commit with `scripts/azure/deploy-functions.ps1` and trigger a manual refresh after the deployment completes.
