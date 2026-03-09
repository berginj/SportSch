import { describe, expect, it } from "vitest";
import { buildPracticeSpaceComparison, derivePracticeSpaceDateRange, filterPracticeSpaceComparison } from "../../lib/practiceSpaceCompare";

describe("practiceSpaceCompare", () => {
  it("builds normalization items from slot states", () => {
    const importedRows = [
      {
        recordId: "live-1",
        date: "2026-04-05",
        fieldId: "park1/field1",
        fieldName: "Barcroft #3",
        mappingIssues: ["division_unmapped"],
      },
    ];

    const slots = [
      {
        practiceSlotKey: "slot-1",
        liveRecordId: "live-1",
        slotId: "canon-1",
        division: "PONY",
        date: "2026-04-05",
        dayOfWeek: "Sunday",
        startTime: "09:00",
        endTime: "10:30",
        fieldId: "park1/field1",
        fieldName: "Barcroft #3",
        bookingPolicyLabel: "Commissioner review",
        normalizationState: "ready",
        normalizationIssues: ["policy_unmapped"],
      },
      {
        practiceSlotKey: "slot-2",
        liveRecordId: "live-1",
        slotId: "canon-2",
        division: "PONY",
        date: "2026-04-06",
        dayOfWeek: "Monday",
        startTime: "09:00",
        endTime: "10:30",
        fieldId: "park1/field1",
        fieldName: "Barcroft #3",
        bookingPolicyLabel: "Auto-approve",
        normalizationState: "normalized",
        normalizationIssues: [],
      },
    ];

    const result = buildPracticeSpaceComparison(importedRows, slots, {
      normalizedBlocks: 1,
      missingBlocks: 1,
      conflictBlocks: 0,
      blockedBlocks: 0,
    });

    expect(result.summary.candidateCount).toBe(2);
    expect(result.summary.normalizedCount).toBe(1);
    expect(result.summary.missingCount).toBe(1);
    expect(result.summary.issueCount).toBe(1);
    expect(result.items[0].compareState).toBe("missing");
  });

  it("derives date range and filters by state and issue", () => {
    const items = buildPracticeSpaceComparison([], [
      {
        practiceSlotKey: "slot-1",
        slotId: "canon-1",
        division: "PONY",
        date: "2026-04-05",
        dayOfWeek: "Sunday",
        startTime: "09:00",
        endTime: "10:30",
        fieldId: "park1/field1",
        fieldName: "Barcroft #3",
        bookingPolicyLabel: "Commissioner review",
        normalizationState: "conflict",
        normalizationIssues: ["manual_overlap"],
      },
    ]).items;

    const range = derivePracticeSpaceDateRange([{ date: "2026-04-05" }], [{ date: "2026-04-07" }]);
    expect(range).toEqual({ dateFrom: "2026-04-05", dateTo: "2026-04-07" });

    const filtered = filterPracticeSpaceComparison(items, {
      compareState: "conflict",
      issue: "manual_overlap",
    });
    expect(filtered).toHaveLength(1);
  });
});
