// src/lib/constants.js

// Canonical league scoping
export const LEAGUE_STORAGE_KEY = "gameswap_leagueId";
export const LEAGUE_HEADER_NAME = "x-league-id";

// Canonical status strings (must match API)
export const ACCESS_REQUEST_STATUS = {
  PENDING: "Pending",
  APPROVED: "Approved",
  DENIED: "Denied",
};

export const SLOT_STATUS = {
  OPEN: "Open",
  PENDING: "Pending",
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
  UNAUTHORIZED: "UNAUTHORIZED",
  FORBIDDEN: "FORBIDDEN",

  // Resource Not Found
  NOT_FOUND: "NOT_FOUND",
  FIELD_NOT_FOUND: "FIELD_NOT_FOUND",
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

  // Business Logic Errors
  SLOT_CONFLICT: "SLOT_CONFLICT",
  FIELD_IN_USE: "FIELD_IN_USE",
  COACH_TEAM_REQUIRED: "COACH_TEAM_REQUIRED",
  COACH_DIVISION_MISMATCH: "COACH_DIVISION_MISMATCH",
  ALREADY_EXISTS: "ALREADY_EXISTS",
  INVALID_STATUS_TRANSITION: "INVALID_STATUS_TRANSITION",
  REQUEST_ALREADY_APPROVED: "REQUEST_ALREADY_APPROVED",
  CANNOT_APPROVE_OWN_REQUEST: "CANNOT_APPROVE_OWN_REQUEST",
  SLOT_NOT_AVAILABLE: "SLOT_NOT_AVAILABLE",

  // Conflicts
  CONFLICT: "CONFLICT",
  CONCURRENT_MODIFICATION: "CONCURRENT_MODIFICATION",

  // Server Errors
  INTERNAL_ERROR: "INTERNAL_ERROR",
  SERVICE_UNAVAILABLE: "SERVICE_UNAVAILABLE",
};

// User-friendly error messages mapped from error codes
export const ERROR_MESSAGES = {
  [ErrorCodes.FIELD_NOT_FOUND]: "The selected field could not be found.",
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
};
