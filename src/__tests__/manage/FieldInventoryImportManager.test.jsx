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
      sourceWorkbookUrl: "https://docs.google.com/spreadsheets/d/test-sheet/edit",
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
        sourceWorkbookUrl: "https://docs.google.com/spreadsheets/d/test-sheet/edit",
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

    fireEvent.click(screen.getByRole("button", { name: "Parse Preview" }));
    expect(await screen.findByText("Preview parsed and stored in staging.")).toBeInTheDocument();
    expect(screen.getByText("Map field 'County Diamond 9'")).toBeInTheDocument();

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
});
