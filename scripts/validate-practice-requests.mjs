#!/usr/bin/env node

import process from "node:process";

const DEFAULT_API_BASE = "http://localhost:7071";

class ValidationError extends Error {
  constructor(message) {
    super(message);
    this.name = "ValidationError";
  }
}

function parseArgs(argv) {
  const options = {};
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (!arg.startsWith("--")) continue;

    const withoutPrefix = arg.slice(2);
    const equalsIndex = withoutPrefix.indexOf("=");
    if (equalsIndex >= 0) {
      const key = withoutPrefix.slice(0, equalsIndex).trim();
      const value = withoutPrefix.slice(equalsIndex + 1).trim();
      options[key] = value;
      continue;
    }

    const key = withoutPrefix.trim();
    const next = argv[i + 1];
    if (next && !next.startsWith("--")) {
      options[key] = next;
      i += 1;
    } else {
      options[key] = true;
    }
  }
  return options;
}

function toFlag(value) {
  if (value === true) return true;
  if (value === false || value == null) return false;
  const normalized = String(value).trim().toLowerCase();
  return normalized === "1" || normalized === "true" || normalized === "yes" || normalized === "on";
}

function hasFlag(options, name) {
  return toFlag(options[name]);
}

function getOption(options, name, envName, defaultValue = "") {
  if (typeof options[name] === "string" && options[name].trim().length > 0) {
    return options[name].trim();
  }
  if (envName && typeof process.env[envName] === "string" && process.env[envName].trim().length > 0) {
    return process.env[envName].trim();
  }
  return defaultValue;
}

function normalizeBaseUrl(baseUrl) {
  const value = (baseUrl || DEFAULT_API_BASE).trim();
  if (!value) return DEFAULT_API_BASE;
  return value.replace(/\/+$/, "");
}

function assert(condition, message) {
  if (!condition) throw new ValidationError(message);
}

function unwrapApiData(payload) {
  if (payload && typeof payload === "object" && Object.prototype.hasOwnProperty.call(payload, "data")) {
    return payload.data;
  }
  return payload;
}

function payloadToMessage(payload, fallbackStatus) {
  if (payload && typeof payload === "object") {
    if (payload.error && typeof payload.error === "object") {
      const code = payload.error.code ? String(payload.error.code).trim() : "";
      const message = payload.error.message ? String(payload.error.message).trim() : "";
      if (code && message) return `${code}: ${message}`;
      if (message) return message;
      if (code) return code;
    }
    try {
      return JSON.stringify(payload);
    } catch {
      return `HTTP ${fallbackStatus}`;
    }
  }
  if (typeof payload === "string" && payload.trim().length > 0) return payload.trim();
  return `HTTP ${fallbackStatus}`;
}

async function apiRequest({
  baseUrl,
  path,
  method = "GET",
  headers = {},
  body = undefined,
  expectedStatuses = [200]
}) {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`;
  const url = `${baseUrl}${normalizedPath}`;

  const requestInit = {
    method,
    headers
  };

  if (body !== undefined) {
    requestInit.body = JSON.stringify(body);
  }

  let response;
  try {
    response = await fetch(url, requestInit);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    throw new ValidationError(`${method} ${normalizedPath} failed to reach API: ${message}`);
  }

  const text = await response.text();
  let parsed = null;
  if (text) {
    try {
      parsed = JSON.parse(text);
    } catch {
      parsed = text;
    }
  }

  const allowed = expectedStatuses.length === 0 || expectedStatuses.includes(response.status);
  if (!allowed) {
    const expected = expectedStatuses.join(", ");
    const details = payloadToMessage(parsed, response.status);
    throw new ValidationError(
      `${method} ${normalizedPath} returned ${response.status} (expected: ${expected}). ${details}`
    );
  }

  return {
    status: response.status,
    payload: parsed,
    data: unwrapApiData(parsed)
  };
}

function buildHeaders(leagueId, userId, userEmail) {
  const headers = {
    "x-league-id": leagueId,
    "x-user-id": userId,
    "x-user-email": userEmail,
    "content-type": "application/json"
  };
  return headers;
}

function pickSlot(slots, requestedSlotId) {
  if (requestedSlotId) {
    return slots.find((slot) => String(slot?.slotId || "").trim() === requestedSlotId) || null;
  }

  const practiceSlot =
    slots.find((slot) => String(slot?.gameType || "").trim().toLowerCase() === "practice") || null;
  if (practiceSlot) return practiceSlot;

  return slots[0] || null;
}

function printUsage() {
  console.log(`
Validate practice request workflow against a live API.

Usage:
  npm run test:practice-requests -- --league-id <league> --division <division> --team-id <team> --coach-user-id <coachUser> --admin-user-id <adminUser>

Required:
  --league-id                Active league id header value.
  --division                 Division used for slot lookup and request creation.
  --team-id                  Team id to attach to request.
  --coach-user-id            User id used to create the request.
  --admin-user-id            User id used to approve/reject.

Optional:
  --api-base                 API base URL (default: ${DEFAULT_API_BASE}).
  --coach-email              Coach email header (default: coach.test@gameswap.local).
  --admin-email              Admin email header (default: admin.test@gameswap.local).
  --slot-id                  Explicit open slot id to use. If omitted, first open practice slot is used.
  --decision                 approve (default) or reject.
  --create-reason            Reason attached to create request.
  --review-reason            Reason attached to approve/reject request.
  --skip-coach-review-check  Skip the negative check that coach cannot approve/reject.
  --help                     Show this help.

Environment fallbacks:
  API_BASE_URL, LEAGUE_ID, DIVISION, TEAM_ID, COACH_USER_ID, COACH_EMAIL, ADMIN_USER_ID, ADMIN_EMAIL
`);
}

async function main() {
  const options = parseArgs(process.argv.slice(2));

  if (hasFlag(options, "help")) {
    printUsage();
    return;
  }

  const apiBase = normalizeBaseUrl(getOption(options, "api-base", "API_BASE_URL", DEFAULT_API_BASE));
  const leagueId = getOption(options, "league-id", "LEAGUE_ID");
  const division = getOption(options, "division", "DIVISION");
  const teamId = getOption(options, "team-id", "TEAM_ID");
  const coachUserId = getOption(options, "coach-user-id", "COACH_USER_ID");
  const coachEmail = getOption(options, "coach-email", "COACH_EMAIL", "coach.test@gameswap.local");
  const adminUserId = getOption(options, "admin-user-id", "ADMIN_USER_ID");
  const adminEmail = getOption(options, "admin-email", "ADMIN_EMAIL", "admin.test@gameswap.local");
  const slotIdOverride = getOption(options, "slot-id", "SLOT_ID");
  const decision = getOption(options, "decision", "PRACTICE_REQUEST_DECISION", "approve").toLowerCase();
  const createReason = getOption(
    options,
    "create-reason",
    "PRACTICE_REQUEST_CREATE_REASON",
    "Automated validation create"
  );
  const reviewReason = getOption(
    options,
    "review-reason",
    "PRACTICE_REQUEST_REVIEW_REASON",
    decision === "reject" ? "Automated validation reject" : "Automated validation approve"
  );
  const skipCoachReviewCheck = hasFlag(options, "skip-coach-review-check");

  assert(leagueId, "Missing --league-id");
  assert(division, "Missing --division");
  assert(teamId, "Missing --team-id");
  assert(coachUserId, "Missing --coach-user-id");
  assert(adminUserId, "Missing --admin-user-id");
  assert(decision === "approve" || decision === "reject", "--decision must be either approve or reject");

  const coachHeaders = buildHeaders(leagueId, coachUserId, coachEmail);
  const adminHeaders = buildHeaders(leagueId, adminUserId, adminEmail);
  const finalStatus = decision === "reject" ? "Rejected" : "Approved";

  console.log(`[1/8] Checking coach identity via /api/me`);
  const coachMe = await apiRequest({
    baseUrl: apiBase,
    path: "/api/me",
    method: "GET",
    headers: coachHeaders,
    expectedStatuses: [200]
  });
  const coachData = coachMe.data && typeof coachMe.data === "object" ? coachMe.data : {};
  console.log(`      Coach user: ${coachData.userId || coachUserId}`);

  console.log(`[2/8] Checking admin identity via /api/me`);
  const adminMe = await apiRequest({
    baseUrl: apiBase,
    path: "/api/me",
    method: "GET",
    headers: adminHeaders,
    expectedStatuses: [200]
  });
  const adminData = adminMe.data && typeof adminMe.data === "object" ? adminMe.data : {};
  console.log(`      Admin user: ${adminData.userId || adminUserId}`);

  console.log(`[3/8] Looking up open slots in division ${division}`);
  const slotsResponse = await apiRequest({
    baseUrl: apiBase,
    path: `/api/slots?division=${encodeURIComponent(division)}&status=Open`,
    method: "GET",
    headers: coachHeaders,
    expectedStatuses: [200]
  });
  const slots = Array.isArray(slotsResponse.data) ? slotsResponse.data : [];
  assert(slots.length > 0, `No open slots found for division ${division} in league ${leagueId}`);

  const slot = pickSlot(slots, slotIdOverride);
  assert(slot, slotIdOverride
    ? `Could not find open slot ${slotIdOverride} in division ${division}`
    : `Could not choose a slot from ${slots.length} open slots`);
  assert(slot.slotId, "Selected slot did not include slotId");

  console.log(
    `      Using slot ${slot.slotId} (${slot.gameDate || "unknown date"} ${slot.startTime || ""}-${slot.endTime || ""}, ${slot.displayName || slot.fieldKey || "unknown field"})`
  );

  console.log(`[4/8] Creating practice request as coach`);
  const createResponse = await apiRequest({
    baseUrl: apiBase,
    path: "/api/practice-requests",
    method: "POST",
    headers: coachHeaders,
    body: {
      division,
      teamId,
      slotId: slot.slotId,
      reason: createReason
    },
    expectedStatuses: [201]
  });
  const createdRequest = createResponse.data && typeof createResponse.data === "object" ? createResponse.data : {};
  const requestId = String(createdRequest.requestId || "").trim();
  assert(requestId, "Create response did not include requestId");
  assert(
    String(createdRequest.status || "").trim().toLowerCase() === "pending",
    `Expected created request status Pending, got ${createdRequest.status || "<empty>"}`
  );
  console.log(`      Created request ${requestId}`);

  console.log(`[5/8] Verifying duplicate create is blocked`);
  await apiRequest({
    baseUrl: apiBase,
    path: "/api/practice-requests",
    method: "POST",
    headers: coachHeaders,
    body: {
      division,
      teamId,
      slotId: slot.slotId,
      reason: "Duplicate check"
    },
    expectedStatuses: [400]
  });
  console.log(`      Duplicate create correctly returned 400`);

  console.log(`[6/8] Verifying request appears in pending list`);
  const pendingResponse = await apiRequest({
    baseUrl: apiBase,
    path: `/api/practice-requests?status=Pending&teamId=${encodeURIComponent(teamId)}`,
    method: "GET",
    headers: adminHeaders,
    expectedStatuses: [200]
  });
  const pendingList = Array.isArray(pendingResponse.data) ? pendingResponse.data : [];
  const pendingRecord = pendingList.find((item) => String(item?.requestId || "").trim() === requestId);
  assert(pendingRecord, `Pending list did not contain request ${requestId}`);
  console.log(`      Pending list contains request ${requestId}`);

  if (!skipCoachReviewCheck && coachUserId !== adminUserId) {
    console.log(`[7/8] Verifying coach cannot ${decision} request`);
    await apiRequest({
      baseUrl: apiBase,
      path: `/api/practice-requests/${encodeURIComponent(requestId)}/${decision}`,
      method: "PATCH",
      headers: coachHeaders,
      body: { reason: "Role gate check" },
      expectedStatuses: [403]
    });
    console.log(`      Coach review attempt correctly returned 403`);
  } else {
    console.log(`[7/8] Skipping coach review authorization check`);
  }

  console.log(`[8/8] Reviewing request as admin and validating final status (${finalStatus})`);
  const reviewResponse = await apiRequest({
    baseUrl: apiBase,
    path: `/api/practice-requests/${encodeURIComponent(requestId)}/${decision}`,
    method: "PATCH",
    headers: adminHeaders,
    body: { reason: reviewReason },
    expectedStatuses: [200]
  });
  const reviewedRequest = reviewResponse.data && typeof reviewResponse.data === "object" ? reviewResponse.data : {};
  assert(
    String(reviewedRequest.status || "").trim().toLowerCase() === finalStatus.toLowerCase(),
    `Expected review status ${finalStatus}, got ${reviewedRequest.status || "<empty>"}`
  );

  const finalResponse = await apiRequest({
    baseUrl: apiBase,
    path: `/api/practice-requests?status=${encodeURIComponent(finalStatus)}&teamId=${encodeURIComponent(teamId)}`,
    method: "GET",
    headers: adminHeaders,
    expectedStatuses: [200]
  });
  const finalList = Array.isArray(finalResponse.data) ? finalResponse.data : [];
  const finalRecord = finalList.find((item) => String(item?.requestId || "").trim() === requestId);
  assert(finalRecord, `${finalStatus} list did not contain request ${requestId}`);

  console.log(`Validation passed for request ${requestId}. Final status: ${finalStatus}.`);
}

main().catch((error) => {
  const message = error instanceof Error ? error.message : String(error);
  console.error(`Validation failed: ${message}`);
  process.exitCode = 1;
});

