# Field Inventory Import

## Purpose

`Field Inventory Import` adds a safe county-workbook ingest workflow to SportsCH. It parses either a public Google Sheets workbook or an uploaded `.xlsx` workbook into normalized field inventory records, stores those staged results separately from live operational data, and only writes to live inventory storage when a league admin runs an explicit import or upsert action.

This is intentionally deterministic. The parser is built around known workbook patterns and saved review decisions, not a freeform spreadsheet interpreter.

## Architecture

### Frontend

- `src/pages/ManagePage.jsx`
  - adds the `Field Inventory Import` management tab
- `src/manage/FieldInventoryImportManager.jsx`
  - workbook URL input
  - direct `.xlsx` upload
  - tab selection and parser/action overrides
  - preview summary
  - warnings panel
  - review queue
  - staged records table
  - explicit `Stage Results`, `Dry Run Upsert`, `Run Import`, and `Run Upsert` actions

### Backend

- `api/Functions/FieldInventoryImportFunctions.cs`
  - HTTP entry points for URL inspect, upload inspect, preview, staging, review updates, mapping persistence, and commit
- `api/Services/FieldInventoryImportService.cs`
  - Google Sheets URL validation
  - public workbook download via Google export `format=xlsx`
  - uploaded workbook parsing from persisted `.xlsx` bytes
  - workbook inspector
  - deterministic tab classification
  - weekday/weekend/reference parsing
  - field alias resolution
  - review queue generation
  - explicit commit boundary
- `api/Repositories/FieldInventoryImportRepository.cs`
  - persistence for runs, staged records, warnings, review items, alias maps, saved tab classifications, live inventory records, and commit audit rows

## Storage Model

New storage tables:

- `FieldInventoryImportRuns`
- `FieldInventoryStagedRecords`
- `FieldInventoryFieldAliases`
- `FieldInventoryTabClassifications`
- `FieldInventoryWorkbookUploads`
- `FieldInventoryImportWarnings`
- `FieldInventoryReviewQueueItems`
- `FieldInventoryLiveRecords`
- `FieldInventoryCommitRuns`

## Staging vs Live Separation

### Staging

Preview parsing writes to:

- `FieldInventoryImportRuns`
- `FieldInventoryStagedRecords`
- `FieldInventoryWorkbookUploads` for uploaded `.xlsx` sources
- `FieldInventoryImportWarnings`
- `FieldInventoryReviewQueueItems`

This is safe to rerun. It does not mutate scheduling slots, field definitions, or live inventory records.

### Live

Explicit commit writes only to:

- `FieldInventoryLiveRecords`

That keeps this feature demonstrable before deeper operational integration. The live inventory store is still separate from the scheduling slot tables and can be evolved independently.

## Workflow

1. Load workbook metadata from either a public Google Sheets link or an uploaded `.xlsx` workbook.
2. Select tabs and optionally override inferred parser/action.
3. Parse preview into staged records.
4. Review warnings and review queue items.
5. Save reusable field alias or tab classification decisions.
6. Stage the run.
7. Run a dry-run import/upsert preview.
8. Run explicit import/upsert only when the staged results are correct.

## Parser Rules in v1

- `season_weekday_grid`
  - AGSA weekday matrix layout
  - weekday blocks run across the sheet with repeating `Time / Level / Team` columns
  - field names anchor each section down the left side
  - season date ranges come from the tab name and are expanded into concrete dates
  - non-blank team cells create records
  - merged cells extend slot duration across columns
- `weekend_grid`
  - AGSA dual weekend layout
  - Saturday and Sunday sections are parsed independently from the same sheet
  - time headers are read from each section
  - Excel numeric dates are resolved from row context
  - non-blank cells create records
- `reference_grid`
  - warnings only
  - does not produce staged inventory records
  - does not override AGSA tabs
- `ignore`
  - skipped entirely

Key guardrails:

- blank cells do not create available inventory
- text beats color
- outside-group usage is visible in staged results but not treated as AGSA-available
- unmapped fields create review items instead of silent fuzzy mapping

## Local AGSA Validation

The parser is now validated against the real AGSA workbook fixture in:

- `docs/2026 AGSA Spring Field Grid (1).xlsx`

Current local validation covers:

- `Spring 316-522`
- `Spring 525-619`
- `Weekends`

The fixture-backed test lives in:

- `api/GameSwap.Tests/Services/FieldInventoryImportServiceTests.cs`

That test path is the authoritative way to evolve parser behavior without depending on Google Sheets export or workbook permissions.

## Configuration and Use

Requirements:

- user must be a league admin or global admin
- request must include `x-league-id`
- Google Sheets sources must be publicly downloadable through Google Sheets export
- uploaded workbook sources must be valid `.xlsx` files

Typical calls:

- `POST /api/field-inventory/workbook/inspect`
- `POST /api/field-inventory/workbook/upload-inspect`
- `POST /api/field-inventory/preview`
- `PATCH /api/field-inventory/runs/{runId}/stage`
- `POST /api/field-inventory/field-aliases`
- `POST /api/field-inventory/tab-classifications`
- `PATCH /api/field-inventory/runs/{runId}/review-items/{reviewItemId}`
- `POST /api/field-inventory/runs/{runId}/commit`

## Extension Points

Future parser improvements belong in `FieldInventoryImportService` as deterministic parser branches, not as ad hoc UI logic.

The main extension points are:

- additional known parser families
- richer reference-tab conflict detection
- stronger color-to-division inference
- commit adapters that project live inventory into downstream scheduling workflows
- import audit reporting and diffs beyond the current counts

If workbook structure changes, update classification rules first, then add or adjust a parser branch with tests backed by sheet fixtures.
