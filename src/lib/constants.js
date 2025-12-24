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
