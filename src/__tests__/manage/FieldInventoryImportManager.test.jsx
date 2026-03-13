import { beforeEach, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import FieldInventoryImportManager from "../../manage/FieldInventoryImportManager";
import * as api from "../../lib/api";

vi.mock("../../lib/api", () => ({
  apiFetch: vi.fn(),
}));

function installApiMock() {
  const previewResponse = {
    run: {
      id: "run-1",
      sourceType: "google_sheet",
      sourceWorkbookUrl: "https://docs.google.com/spreadsheets/d/test-sheet/edit",
      uploadedWorkbookId: null,
      sourceWorkbookName: null,
      sourceWorkbookTitle: "Spring 2026 County Inventory",
      seasonLabel: "Spring 2026",
      status: "parsed",
      selectedTabs: [
        { tabName: "Spring 3/16-5/22", parserType: "season_weekday_grid", actionType: "ingest", selected: true },
      ],
      summaryCounts: {
        parsedRecords: 1,
        warnings: 1,
        reviewItems: 1,
        unmappedFields: 1,
        selectedTabs: 1,
        importedRecords: 0,
        skippedRecords: 0,
      },
    },
    records: [
      {
        id: "record-1",
        importRunId: "run-1",
        fieldId: null,
        fieldName: null,
        rawFieldName: "County Diamond 9",
        date: "2026-03-16",
        dayOfWeek: "Monday",
        startTime: "18:00",
        endTime: "19:00",
        slotDurationMinutes: 60,
        availabilityStatus: "available",
        utilizationStatus: "not_used",
        usageType: null,
        usedBy: "AGSA",
        assignedGroup: null,
        assignedDivision: null,
        assignedTeamOrEvent: null,
        sourceWorkbookUrl: "https://docs.google.com/spreadsheets/d/test-sheet/edit",
        sourceTab: "Spring 3/16-5/22",
        sourceCellRange: "B3",
        sourceValue: "Available",
        sourceColor: null,
        parserType: "season_weekday_grid",
        confidence: "low",
        warningFlags: ["unmapped_field"],
        reviewStatus: "needs_review",
      },
    ],
    warnings: [
      {
        id: "warning-1",
        importRunId: "run-1",
        severity: "info",
        code: "reference_tab",
        message: "Reference tab loaded for visibility only.",
        sourceTab: "County Grid",
        sourceCellRange: "",
        relatedRecordId: null,
      },
    ],
    reviewItems: [
      {
        id: "review-1",
        importRunId: "run-1",
        itemType: "field_mapping",
        severity: "non_blocking",
        title: "Map field 'County Diamond 9'",
        description: "Map this raw field name to a canonical field.",
        sourceTab: "Spring 3/16-5/22",
        sourceCellRange: "B3",
        rawValue: "County Diamond 9",
        suggestedResolution: {},
        chosenResolution: {},
        status: "open",
        saveDecisionForFuture: false,
      },
    ],
    canonicalFields: [
      {
        fieldId: "park-b/field-9",
        canonicalFieldName: "Park B > Diamond 9",
        fieldName: "Diamond 9",
        parkName: "Park B",
      },
    ],
    unmappedFieldNames: ["County Diamond 9"],
    commitPreview: null,
  };

  const mappedPreview = {
    ...previewResponse,
    run: {
      ...previewResponse.run,
      status: "staged",
      summaryCounts: {
        ...previewResponse.run.summaryCounts,
        unmappedFields: 0,
        reviewItems: 0,
      },
    },
    records: [
      {
        ...previewResponse.records[0],
        fieldId: "park-b/field-9",
        fieldName: "Park B > Diamond 9",
        confidence: "medium",
        warningFlags: [],
        reviewStatus: "none",
      },
    ],
    reviewItems: [],
    unmappedFieldNames: [],
  };

  api.apiFetch.mockImplementation((url, options = {}) => {
    if (url === "/api/field-inventory/workbook/inspect") {
      return Promise.resolve({
        sourceType: "google_sheet",
        sourceWorkbookUrl: "https://docs.google.com/spreadsheets/d/test-sheet/edit",
        uploadedWorkbookId: null,
        sourceWorkbookName: null,
        spreadsheetId: "test-sheet",
        sourceWorkbookTitle: "Spring 2026 County Inventory",
        tabs: [
          {
            tabName: "Spring 3/16-5/22",
            index: 0,
            isHidden: false,
            inferredParserType: "season_weekday_grid",
            inferredActionType: "ingest",
            confidence: "high",
            reason: "AGSA weekday inventory tab",
            nonEmptyCellCount: 12,
            mergedRangeCount: 1,
          },
          {
            tabName: "County Grid",
            index: 1,
            isHidden: true,
            inferredParserType: "ignore",
            inferredActionType: "ignore",
            confidence: "high",
            reason: "Hidden workbook tab",
            nonEmptyCellCount: 8,
            mergedRangeCount: 0,
          },
        ],
      });
    }
    if (url === "/api/field-inventory/workbook/upload-inspect") {
      return Promise.resolve({
        sourceType: "uploaded_workbook",
        sourceWorkbookUrl: "uploaded://upload-1/spring-2026-county-inventory-xlsx",
        uploadedWorkbookId: "upload-1",
        sourceWorkbookName: "Spring-2026-County-Inventory.xlsx",
        spreadsheetId: "upload-1",
        sourceWorkbookTitle: "Spring 2026 County Inventory",
        tabs: [
          {
            tabName: "Spring 3/16-5/22",
            index: 0,
            isHidden: false,
            inferredParserType: "season_weekday_grid",
            inferredActionType: "ingest",
            confidence: "high",
            reason: "AGSA weekday inventory tab",
            nonEmptyCellCount: 12,
            mergedRangeCount: 1,
          },
        ],
      });
    }
    if (url === "/api/field-inventory/preview") {
      return Promise.resolve(previewResponse);
    }
    if (url === "/api/field-inventory/field-aliases") {
      return Promise.resolve(mappedPreview);
    }
    if (url === "/api/field-inventory/runs/run-1/stage") {
      return Promise.resolve(mappedPreview);
    }
    if (url === "/api/field-inventory/runs/run-1/commit") {
      const body = JSON.parse(options.body || "{}");
      return Promise.resolve({
        ...mappedPreview,
        commitPreview: {
          mode: body.mode,
          dryRun: body.dryRun,
          createCount: 1,
          updateCount: 0,
          deleteCount: 0,
          unchangedCount: 0,
          skippedUnmappedCount: 0,
          seasonLabel: "Spring 2026",
        },
      });
    }
    throw new Error(`Unexpected apiFetch call: ${url}`);
  });
}

describe("FieldInventoryImportManager", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    installApiMock();
  });

  it("loads a workbook, parses a preview, saves a mapping, stages, and dry-runs commit", async () => {
    render(<FieldInventoryImportManager leagueId="league-1" />);

    fireEvent.change(screen.getByLabelText(/Google Sheets URL/i), {
      target: { value: "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0" },
    });
    fireEvent.change(screen.getByLabelText(/Season label/i), {
      target: { value: "Spring 2026" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Load Workbook" }));
    expect(await screen.findByText("Workbook loaded. Select tabs and parse a preview.")).toBeInTheDocument();
    expect(screen.getByText("Will Ingest")).toBeInTheDocument();
    expect(screen.getByText("Ignored")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Parse Preview" }));
    expect(await screen.findByText("Preview parsed and stored in staging.")).toBeInTheDocument();
    expect(screen.getByText("Map field 'County Diamond 9'")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Calendar View" })).toBeInTheDocument();

    fireEvent.change(screen.getByDisplayValue("Select canonical field"), {
      target: { value: "park-b/field-9" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save Mapping" }));

    await waitFor(() => {
      expect(screen.getByText("Saved mapping for County Diamond 9.")).toBeInTheDocument();
      expect(screen.getByText("No review items.")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole("button", { name: "Stage Results" }));
    expect(await screen.findByText("Preview marked as staged. Live inventory is still unchanged.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Dry Run Upsert" }));
    expect(await screen.findByText("upsert dry run completed.")).toBeInTheDocument();
    expect(screen.getByText("Commit Preview")).toBeInTheDocument();
    expect(screen.getByText("Season: Spring 2026")).toBeInTheDocument();
  });

  it("explains anonymous access when workbook load fails with Google authorization status", async () => {
    api.apiFetch.mockRejectedValueOnce(Object.assign(new Error("Workbook export failed with status 401."), {
      status: 502,
      code: "WORKBOOK_LOAD_FAILED",
      originalMessage: "Workbook export failed with status 401.",
    }));

    render(<FieldInventoryImportManager leagueId="league-1" />);

    fireEvent.change(screen.getByLabelText(/Google Sheets URL/i), {
      target: { value: "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Load Workbook" }));

    expect(await screen.findByText(/Anyone with the link can view/i)).toBeInTheDocument();
  });

  it("uploads an xlsx workbook and uses the uploaded workbook id for preview", async () => {
    render(<FieldInventoryImportManager leagueId="league-1" />);

    const file = new File(["xlsx-bytes"], "Spring-2026-County-Inventory.xlsx", {
      type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    });

    fireEvent.change(screen.getByLabelText(/Workbook file/i), {
      target: { files: [file] },
    });

    fireEvent.click(screen.getByRole("button", { name: "Upload Workbook" }));

    expect(await screen.findByText("Workbook uploaded. Select tabs and parse a preview.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Parse Preview" }));

    await waitFor(() => {
      expect(api.apiFetch).toHaveBeenCalledWith("/api/field-inventory/preview", expect.objectContaining({
        method: "POST",
        body: expect.stringContaining("\"uploadedWorkbookId\":\"upload-1\""),
      }));
    });
  });

  it("shows backend request ids and response text for preview failures", async () => {
    api.apiFetch.mockImplementation((url) => {
      if (url === "/api/field-inventory/workbook/inspect") {
        return Promise.resolve({
          sourceType: "google_sheet",
          sourceWorkbookUrl: "https://docs.google.com/spreadsheets/d/test-sheet/edit",
          uploadedWorkbookId: null,
          sourceWorkbookName: null,
          spreadsheetId: "test-sheet",
          sourceWorkbookTitle: "Spring 2026 County Inventory",
          tabs: [
            {
              tabName: "Weekends",
              index: 0,
              isHidden: false,
              inferredParserType: "weekend_grid",
              inferredActionType: "ingest",
              confidence: "high",
              reason: "AGSA weekend inventory tab",
              nonEmptyCellCount: 12,
              mergedRangeCount: 1,
            },
          ],
        });
      }
      if (url === "/api/field-inventory/preview") {
        return Promise.reject(Object.assign(new Error("Field inventory import failed."), {
          status: 500,
          code: "INTERNAL_ERROR",
          details: { requestId: "req-500" },
          middlewareRequestId: "mid-500",
          responseText: "Object reference not set to an instance of an object.",
        }));
      }
      if (url.startsWith("/api/field-inventory/diagnostics/")) {
        return Promise.resolve([
          {
            id: "diag-1",
            clientRequestId: "client-1",
            stage: "workbook_loaded",
            status: "ok",
            message: "Workbook loaded with 14 sheet(s).",
            runId: "run-1",
            createdAt: "2026-03-09T01:00:00Z",
          },
          {
            id: "diag-2",
            clientRequestId: "client-1",
            stage: "preview_persisting",
            status: "info",
            message: "Persisting preview with 0 record(s), 2 warning(s), and 1 review item(s).",
            runId: "run-1",
            createdAt: "2026-03-09T01:00:01Z",
          },
        ]);
      }
      throw new Error(`Unexpected apiFetch call: ${url}`);
    });

    render(<FieldInventoryImportManager leagueId="league-1" />);

    fireEvent.change(screen.getByLabelText(/Google Sheets URL/i), {
      target: { value: "https://docs.google.com/spreadsheets/d/test-sheet/edit#gid=0" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Load Workbook" }));
    expect(await screen.findByText("Workbook loaded. Select tabs and parse a preview.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Parse Preview" }));

    expect(await screen.findByText("Field inventory request failed")).toBeInTheDocument();
    expect(screen.getByText("Request ID: req-500")).toBeInTheDocument();
    expect(screen.getByText("Middleware ID: mid-500")).toBeInTheDocument();
    expect(screen.getByText(/Object reference not set to an instance of an object/i)).toBeInTheDocument();
    expect(screen.getByText(/\[ok\] workbook_loaded: Workbook loaded with 14 sheet\(s\)\./i)).toBeInTheDocument();
    expect(screen.getByText(/\[info\] preview_persisting: Persisting preview with 0 record\(s\), 2 warning\(s\), and 1 review item\(s\)\./i)).toBeInTheDocument();
  });
});
