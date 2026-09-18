# Operational baseline and local verification

## Running the checks

Prerequisites: Node 22.12+, .NET 8, Functions Core Tools 4.5.0, and Chromium installed with `npx playwright install chromium`.

```text
npm ci
npm run lint
npx vitest run --maxWorkers=2
npm run build
npm run test:e2e
```

Playwright starts Azurite, publishes the API into `.artifacts/e2e-api`, starts Functions on port 7072, seeds two emulator-only leagues, and starts Vite on 5173. The API host does not load the developer's `local.settings.json`; timer functions and external email are disabled. Tests use local development identity headers, never localStorage identity/role values. Production still uses SWA/EasyAuth identity.

Real Table tests use only `UseDevelopmentStorage=true`. With Azurite on its default ports:

```powershell
$env:GAMESWAP_STORAGE_TESTS = "1"
dotnet test api/GameSwap.Tests/GameSwap.Tests.csproj
```

Without that variable, storage tests explicitly report skipped. Deployment workflows set it and start the emulator. Storage tests create unique league partitions and delete only their own rows. Browser fixtures use reserved `e2e-*` identifiers in the local emulator. They must never be seeded into production.

## Provisioning

`TableClients.GetTableAsync` no longer creates tables on each request. `TableStartup` provisions all `Constants.Tables` once per host startup when `GAMESWAP_CREATE_TABLES=true` (the default). A deployment that provisions tables independently can set this false. This removes repeated creation calls but does not prove an improvement to cold starts; measure startup latency and storage permissions separately.

## Signals now emitted

- Backend `api_request` structured traces: function name, HTTP method/status, duration, first HTTP request in a process, invocation ID, correlation ID.
- Frontend `api_request` events: endpoint family (no query parameters or entity IDs), method, status/network failure, correlation ID, elapsed time including response-body read.
- Frontend `calendar_load`: data-load duration, slot/event counts, success/error. This measures the calendar data request stage, not the entire initial navigation or React paint.
- `x-correlation-id` is attached to requests/responses. Only a valid UUID supplied by a client is accepted by backend logging. Existing invocation request IDs remain available in error envelopes.
- Application Insights initialization is lazy; an unconfigured client does not queue events indefinitely.

No production baseline has been collected. First request in a process is a cold-start **proxy**, not a measurement of Azure host startup. Browser/local test durations are not production p95 values.

## Minimal operational queries

API latency and failures by endpoint:

```kusto
traces
| where message startswith "api_request "
| extend endpoint=tostring(customDimensions.Endpoint), duration=todouble(customDimensions.DurationMs), status=toint(customDimensions.StatusCode)
| summarize requests=count(), p50=percentile(duration,50), p95=percentile(duration,95), p99=percentile(duration,99), failures=countif(status >= 500), conflicts=countif(status == 409), denied=countif(status == 403)
  by endpoint, bin(timestamp, 15m)
```

Calendar data-load experience:

```kusto
customEvents
| where name == "calendar_load"
| summarize loads=count(), failed=countif(tostring(customDimensions.outcome) == "error"), p95=percentile(todouble(customMeasurements.durationMs),95)
  by bin(timestamp, 15m)
```

Also chart Functions `requests` failure rate and duration, storage `dependencies` latency/failures, and frontend `exceptions`. Correlate an elevated booking failure rate with 409s and storage 412s before treating legitimate competing acceptances as outages. Do not alert on a single slow local request.

## Measurement protocol

1. Establish seven days of production endpoint latency/error rates and calendar load results after telemetry is deployed. Record league size, visible date range, phone/desktop, warm/first-process-request, and result counts.
2. Compare the same seeded small/medium/large schedules and date windows; record requests, response bytes, storage calls, elapsed API time, first visible schedule item, and bundle sizes. Repeat runs, report distributions, and separate cache-warm from cold runs.
3. Require no correctness/tenant regressions and no increase in failed user workflows. Set numerical latency targets from that baseline; no speculative millisecond targets are asserted here.
4. Verify the retry/recovery story separately with interrupted writes and competing requests. Passing UI tests or a faster response does not establish durable commit or delivery guarantees.

## Remaining operational work

Durable season-operation journal/gate, recovery worker, outbox delivery, bounded calendar projection, full backup/restore, readiness checks, and a deployed operational dashboard remain in the roadmap. Managed SWA deployment has not been changed to add a background worker. Existing timer functions in the API project must not be mistaken for verified production delivery.
