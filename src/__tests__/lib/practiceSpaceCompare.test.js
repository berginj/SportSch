import { describe, expect, it } from "vitest";
import { buildPracticeSpaceComparison, derivePracticeSpaceDateRange, filterPracticeSpaceComparison } from "../../lib/practiceSpaceCompare";

describe("practiceSpaceCompare", () => {
  it("builds aligned, imported-only, and manual-only comparison rows", () => {
    const importedRows = [
      {
        recordId: "live-1",
        date: "2026-04-05",
        startTime: "09:00",
        endTime: "10:30",
        fieldId: "park1/field1",
        fieldName: "Barcroft #3",
        availabilityStatus: "available",
        utilizationStatus: "not_used",
        rawAssignedDivision: "Ponytail",
      },
      {
        recordId: "live-2",
        date: "2026-04-06",
        startTime: "09:00",
        endTime: "10:30",
        fieldId: "park1/field1",
        fieldName: "Barcroft #3",
        availabilityStatus: "available",
        utilizationStatus: "not_used",
      },
    ];
    const manualSlots = [
      {
        slotId: "slot-1",
        gameDate: "2026-04-05",
        startTime: "09:00",
        endTime: "10:30",
        fieldKey: "park1/field1",
        displayName: "Barcroft #3",
        division: "Ponytail",
        isAvailability: true,
      },
      {
        slotId: "slot-2",
        gameDate: "2026-04-07",
        startTime: "09:00",
        endTime: "10:30",
        fieldKey: "park1/field2",
        displayName: "Key #1",
        division: "Majors",
        isAvailability: true,
      },
    ];

    const result = buildPracticeSpaceComparison(importedRows, manualSlots);

    expect(result.summary.alignedCount).toBe(1);
    expect(result.summary.importedOnlyCount).toBe(1);
    expect(result.summary.manualOnlyCount).toBe(1);
    expect(result.summary.issueCount).toBe(2);
  });

  it("derives date range and filters by compare state and issue", () => {
    const comparison = buildPracticeSpaceComparison(
      [
        {
          recordId: "live-1",
          date: "2026-04-05",
          startTime: "09:00",
          endTime: "10:30",
          fieldId: "park1/field1",
          fieldName: "Barcroft #3",
          availabilityStatus: "available",
          utilizationStatus: "not_used",
        },
      ],
      []
    );

    const range = derivePracticeSpaceDateRange(
      [{ date: "2026-04-05" }],
      [{ gameDate: "2026-04-07" }]
    );
    expect(range).toEqual({ dateFrom: "2026-04-05", dateTo: "2026-04-07" });

    const filtered = filterPracticeSpaceComparison(comparison.items, {
      compareState: "imported_only",
      issue: "manual_missing",
    });
    expect(filtered).toHaveLength(1);
  });
});

