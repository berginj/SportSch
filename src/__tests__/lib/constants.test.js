import { describe, it, expect } from "vitest";
import { ErrorCodes, ERROR_MESSAGES } from "../../lib/constants";

describe("constants error-code mapping", () => {
  it("does not collapse unknown entries onto an undefined key", () => {
    expect(Object.prototype.hasOwnProperty.call(ERROR_MESSAGES, "undefined")).toBe(false);
  });

  it("mirrors the backend error codes used by the frontend error map", () => {
    const expectedCodes = [
      "INVALID_INPUT",
      "DIVISION_MISMATCH",
      "INVALID_WORKBOOK_URL",
      "WORKBOOK_LOAD_FAILED",
      "WORKBOOK_FILE_REQUIRED",
      "FIELD_ALIAS_REQUIRED",
      "REVIEW_REQUIRED",
      "PRACTICE_SPACE_NOT_FOUND",
      "PRACTICE_SPACE_NOT_REQUESTABLE",
      "PRACTICE_SPACE_FULL",
      "PRACTICE_REQUEST_NOT_FOUND",
      "PRACTICE_POLICY_REQUIRED",
      "PRACTICE_NORMALIZATION_CONFLICT",
      "PRACTICE_MOVE_NOT_ALLOWED",
      "SLOT_NOT_OPEN",
      "SLOT_UNASSIGNED",
      "SLOT_ASSIGNED",
      "SLOT_CANCELLED",
      "DOUBLE_BOOKING",
      "REQUEST_NOT_PENDING",
      "SCHEDULE_BLOCKED",
    ];

    expectedCodes.forEach((code) => {
      expect(ErrorCodes[code]).toBe(code);
      expect(typeof ERROR_MESSAGES[ErrorCodes[code]]).toBe("string");
      expect(ERROR_MESSAGES[ErrorCodes[code]].length).toBeGreaterThan(0);
    });
  });
});
