// src/lib/constants.js

// Canonical league scoping
export const LEAGUE_STORAGE_KEY = "gameswap_leagueId";
export const LEAGUE_HEADER_NAME = "x-league-id";
export const THEME_STORAGE_KEY = "gameswap_theme";
export const THEME_MODE = {
  LIGHT: "light",
  DARK: "dark",
  SYSTEM: "system",
};

// Canonical status strings (must match API)
export const ACCESS_REQUEST_STATUS = {
  PENDING: "Pending",
  APPROVED: "Approved",
  DENIED: "Denied",
};

export const SLOT_STATUS = {
  OPEN: "Open",
  CANCELLED: "Cancelled",
  CONFIRMED: "Confirmed",
};

export const SLOT_REQUEST_STATUS = {
  PENDING: "Pending",
  APPROVED: "Approved",
  DENIED: "Denied",
};

export const ROLE = {
  LEAGUE_ADMIN: "LeagueAdmin",
  COACH: "Coach",
  VIEWER: "Viewer",
};

export const FIELD_STATUS = {
  ACTIVE: "Active",
  INACTIVE: "Inactive",
};

// Error codes (mirror backend ErrorCodes.cs for structured error handling)
export const ErrorCodes = {
  // Authentication & Authorization
  UNAUTHENTICATED: "UNAUTHENTICATED",

  /**
   * @deprecated Use FORBIDDEN instead for 403 errors (insufficient permissions).
   * Kept for backward compatibility only.
   */
  UNAUTHORIZED: "UNAUTHORIZED",

  FORBIDDEN: "FORBIDDEN",

  // Resource Not Found
  NOT_FOUND: "NOT_FOUND",
  FIELD_NOT_FOUND: "FIELD_NOT_FOUND",
  FIELD_INACTIVE: "FIELD_INACTIVE",
  SLOT_NOT_FOUND: "SLOT_NOT_FOUND",
  TEAM_NOT_FOUND: "TEAM_NOT_FOUND",
  DIVISION_NOT_FOUND: "DIVISION_NOT_FOUND",
  LEAGUE_NOT_FOUND: "LEAGUE_NOT_FOUND",
  REQUEST_NOT_FOUND: "REQUEST_NOT_FOUND",
  RULE_NOT_FOUND: "RULE_NOT_FOUND",

  // Validation Errors
  BAD_REQUEST: "BAD_REQUEST",
  INVALID_DATE: "INVALID_DATE",
  INVALID_TIME: "INVALID_TIME",
  INVALID_DATE_RANGE: "INVALID_DATE_RANGE",
  INVALID_TIME_RANGE: "INVALID_TIME_RANGE",
  INVALID_FIELD_KEY: "INVALID_FIELD_KEY",
  INVALID_TABLE_KEY: "INVALID_TABLE_KEY",
  MISSING_REQUIRED_FIELD: "MISSING_REQUIRED_FIELD",
  INVALID_INPUT: "INVALID_INPUT",
  DIVISION_MISMATCH: "DIVISION_MISMATCH",
  INVALID_WORKBOOK_URL: "INVALID_WORKBOOK_URL",
  WORKBOOK_LOAD_FAILED: "WORKBOOK_LOAD_FAILED",
  WORKBOOK_FILE_REQUIRED: "WORKBOOK_FILE_REQUIRED",
  FIELD_ALIAS_REQUIRED: "FIELD_ALIAS_REQUIRED",
  REVIEW_REQUIRED: "REVIEW_REQUIRED",
  PRACTICE_SPACE_NOT_FOUND: "PRACTICE_SPACE_NOT_FOUND",
  PRACTICE_SPACE_NOT_REQUESTABLE: "PRACTICE_SPACE_NOT_REQUESTABLE",
  PRACTICE_SPACE_FULL: "PRACTICE_SPACE_FULL",
  PRACTICE_REQUEST_NOT_FOUND: "PRACTICE_REQUEST_NOT_FOUND",
  PRACTICE_POLICY_REQUIRED: "PRACTICE_POLICY_REQUIRED",
  PRACTICE_NORMALIZATION_CONFLICT: "PRACTICE_NORMALIZATION_CONFLICT",
  PRACTICE_MOVE_NOT_ALLOWED: "PRACTICE_MOVE_NOT_ALLOWED",
  GAME_RESCHEDULE_NOT_FOUND: "GAME_RESCHEDULE_NOT_FOUND",
  GAME_NOT_CONFIRMED: "GAME_NOT_CONFIRMED",
  LEAD_TIME_VIOLATION: "LEAD_TIME_VIOLATION",
  NOT_GAME_PARTICIPANT: "NOT_GAME_PARTICIPANT",
  RESCHEDULE_CONFLICT_DETECTED: "RESCHEDULE_CONFLICT_DETECTED",
  FINALIZATION_FAILED: "FINALIZATION_FAILED",

  // Business Logic Errors
  SLOT_CONFLICT: "SLOT_CONFLICT",
  SLOT_NOT_OPEN: "SLOT_NOT_OPEN",
  SLOT_UNASSIGNED: "SLOT_UNASSIGNED",
  SLOT_ASSIGNED: "SLOT_ASSIGNED",
  SLOT_CANCELLED: "SLOT_CANCELLED",
  FIELD_IN_USE: "FIELD_IN_USE",
  DOUBLE_BOOKING: "DOUBLE_BOOKING",
  COACH_TEAM_REQUIRED: "COACH_TEAM_REQUIRED",
  COACH_DIVISION_MISMATCH: "COACH_DIVISION_MISMATCH",
  ALREADY_EXISTS: "ALREADY_EXISTS",
  INVALID_STATUS_TRANSITION: "INVALID_STATUS_TRANSITION",
  REQUEST_ALREADY_APPROVED: "REQUEST_ALREADY_APPROVED",
  REQUEST_NOT_PENDING: "REQUEST_NOT_PENDING",
  CANNOT_APPROVE_OWN_REQUEST: "CANNOT_APPROVE_OWN_REQUEST",
  SLOT_NOT_AVAILABLE: "SLOT_NOT_AVAILABLE",

  // Conflicts
  CONFLICT: "CONFLICT",
  CONCURRENT_MODIFICATION: "CONCURRENT_MODIFICATION",
  SCHEDULE_BLOCKED: "SCHEDULE_BLOCKED",

  // Server Errors
  INTERNAL_ERROR: "INTERNAL_ERROR",
  SERVICE_UNAVAILABLE: "SERVICE_UNAVAILABLE",
};

// User-friendly error messages mapped from error codes
export const ERROR_MESSAGES = {
  [ErrorCodes.FIELD_NOT_FOUND]: "The selected field could not be found.",
  [ErrorCodes.FIELD_INACTIVE]: "The selected field is inactive and cannot be used.",
  [ErrorCodes.SLOT_CONFLICT]: "This slot conflicts with an existing booking.",
  [ErrorCodes.UNAUTHORIZED]: "You do not have permission to perform this action.",
  [ErrorCodes.COACH_TEAM_REQUIRED]: "Coach role requires an assigned team.",
  [ErrorCodes.COACH_DIVISION_MISMATCH]: "You can only manage slots in your assigned division.",
  [ErrorCodes.INVALID_DATE]: "Please enter a valid date in YYYY-MM-DD format.",
  [ErrorCodes.INVALID_TIME]: "Please enter a valid time in HH:MM format.",
  [ErrorCodes.INVALID_DATE_RANGE]: "Start date must be before end date.",
  [ErrorCodes.INVALID_TIME_RANGE]: "Start time must be before end time.",
  [ErrorCodes.SLOT_NOT_FOUND]: "The requested slot could not be found.",
  [ErrorCodes.FIELD_IN_USE]: "This field is already booked for the selected time.",
  [ErrorCodes.MISSING_REQUIRED_FIELD]: "Please fill in all required fields.",
  [ErrorCodes.UNAUTHENTICATED]: "Please sign in to continue.",
  [ErrorCodes.FORBIDDEN]: "Access denied.",
  [ErrorCodes.INTERNAL_ERROR]: "An unexpected error occurred. Please try again.",
  
  // Additional error messages for completeness
  [ErrorCodes.NOT_FOUND]: "The requested resource could not be found.",
  [ErrorCodes.TEAM_NOT_FOUND]: "The selected team could not be found.",
  [ErrorCodes.DIVISION_NOT_FOUND]: "The selected division could not be found.",
  [ErrorCodes.LEAGUE_NOT_FOUND]: "The selected league could not be found.",
  [ErrorCodes.REQUEST_NOT_FOUND]: "The request could not be found.",
  [ErrorCodes.RULE_NOT_FOUND]: "The scheduling rule could not be found.",
  [ErrorCodes.BAD_REQUEST]: "The request was invalid. Please check your input.",
  [ErrorCodes.INVALID_FIELD_KEY]: "The field identifier is invalid.",
  [ErrorCodes.INVALID_TABLE_KEY]: "The table key format is invalid.",
  [ErrorCodes.INVALID_INPUT]: "One or more fields have invalid values.",
  [ErrorCodes.DIVISION_MISMATCH]: "The selected team does not belong to this division.",
  [ErrorCodes.INVALID_WORKBOOK_URL]: "The workbook URL is not valid. Please check the link.",
  [ErrorCodes.WORKBOOK_LOAD_FAILED]: "Failed to load the workbook. Please try again.",
  [ErrorCodes.WORKBOOK_FILE_REQUIRED]: "Please upload an Excel (.xlsx) workbook file.",
  [ErrorCodes.FIELD_ALIAS_REQUIRED]: "Field alias is required.",
  [ErrorCodes.REVIEW_REQUIRED]: "This action requires review and approval.",
  [ErrorCodes.PRACTICE_SPACE_NOT_FOUND]: "The practice space could not be found.",
  [ErrorCodes.PRACTICE_SPACE_NOT_REQUESTABLE]: "This practice space cannot be requested.",
  [ErrorCodes.PRACTICE_SPACE_FULL]: "This practice space is fully booked.",
  [ErrorCodes.PRACTICE_REQUEST_NOT_FOUND]: "The practice request could not be found.",
  [ErrorCodes.PRACTICE_POLICY_REQUIRED]: "A practice policy must be configured.",
  [ErrorCodes.PRACTICE_NORMALIZATION_CONFLICT]: "The practice request conflicts with existing bookings.",
  [ErrorCodes.PRACTICE_MOVE_NOT_ALLOWED]: "This practice request cannot be moved.",
  [ErrorCodes.SLOT_NOT_OPEN]: "This slot is not open for assignment.",
  [ErrorCodes.SLOT_UNASSIGNED]: "This slot is unassigned.",
  [ErrorCodes.SLOT_ASSIGNED]: "This slot is already assigned.",
  [ErrorCodes.SLOT_CANCELLED]: "This slot has been cancelled.",
  [ErrorCodes.SLOT_NOT_AVAILABLE]: "This slot is not available.",
  [ErrorCodes.DOUBLE_BOOKING]: "A team cannot be booked twice for the same time.",
  [ErrorCodes.ALREADY_EXISTS]: "This resource already exists.",
  [ErrorCodes.INVALID_STATUS_TRANSITION]: "This status change is not allowed.",
  [ErrorCodes.REQUEST_ALREADY_APPROVED]: "This request has already been approved.",
  [ErrorCodes.REQUEST_NOT_PENDING]: "This request is not in pending status.",
  [ErrorCodes.CANNOT_APPROVE_OWN_REQUEST]: "You cannot approve your own request.",
  [ErrorCodes.CONFLICT]: "There is a conflict with your request. Please review and try again.",
  [ErrorCodes.CONCURRENT_MODIFICATION]: "This resource was modified by another user. Please refresh and try again.",
  [ErrorCodes.SCHEDULE_BLOCKED]: "The schedule cannot be modified at this time.",
  [ErrorCodes.SERVICE_UNAVAILABLE]: "The service is temporarily unavailable. Please try again later.",
};
