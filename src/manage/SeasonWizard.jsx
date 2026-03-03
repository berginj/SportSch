import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import { trackEvent } from "../lib/telemetry";
import CollapsibleSection from "../components/CollapsibleSection";
import Toast from "../components/Toast";
import { useCollapsibleSectionControl } from "../lib/useCollapsibleSectionControl";

const WEEKDAY_OPTIONS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const MAX_RIVALRY_MATCHUPS = 12;
const SLOT_TYPE_OPTIONS = [
  { value: "practice", label: "Practice", shortLabel: "P" },
  { value: "game", label: "Game", shortLabel: "G" },
  { value: "both", label: "Both", shortLabel: "B" },
];
const SLOT_TYPE_APPEARANCE = {
  practice: {
    accentColor: "#9a3412",
    borderColor: "#fdba74",
    surfaceColor: "#fff7ed",
    selectSurfaceColor: "#ffedd5",
    textColor: "#7c2d12",
  },
  game: {
    accentColor: "#1d4ed8",
    borderColor: "#93c5fd",
    surfaceColor: "#eff6ff",
    selectSurfaceColor: "#dbeafe",
    textColor: "#1e3a8a",
  },
  both: {
    accentColor: "#0f766e",
    borderColor: "#99f6e4",
    surfaceColor: "#f0fdfa",
    selectSurfaceColor: "#ccfbf1",
    textColor: "#134e4a",
  },
};
const ISSUE_HINTS = {
  "unassigned-matchups": "Not enough availability slots, or constraints are too tight for the slot pool.",
  "unassigned-slots": "More availability than matchups. These can become extra offers or remain unused.",
  "double-header": "A team was scheduled twice on the same date. Open another usable date, or allow doubleheaders if that load is intentional.",
  "double-header-balance": "Doubleheaders are not evenly distributed. Shift slot priorities/times to spread same-day load across teams.",
  "home-away-balance": "A few teams are carrying a noticeably uneven home/away split.",
  "idle-gap-balance": "Some teams have longer idle stretches than the rest of the division.",
  "max-games-per-week": "Max games/week is a hard cap. Add slots or widen the season window if assignments are short.",
  "missing-opponent": "A slot is missing an opponent. Check team count or external/guest game settings.",
  "opponent-repeat-balance": "A few team pairings are repeating more often than the rest of the schedule.",
  "unused-game-capacity": "Open game-capable slots remain after the schedule was built.",
};
const WIZARD_STEPS = [
  {
    label: "Basics",
    description: "Choose the division and season window before building anything else.",
  },
  {
    label: "Postseason",
    description: "Reserve pool-play and bracket dates so the regular season is planned around them.",
  },
  {
    label: "Slot plan (all phases)",
    description: "Mark which availability slots can host games across the regular season and postseason.",
  },
  {
    label: "Rules",
    description: "Set matchup targets, weekly limits, and league rules for schedule construction.",
  },
  {
    label: "Preview",
    description: "Inspect the generated schedule, rule health, and apply only when it looks right.",
  },
];
const PREVIEW_SECTION_STORAGE_KEYS = {
  health: "season-wizard-preview-health",
  coverage: "season-wizard-preview-coverage",
  assignments: "season-wizard-preview-assignments",
};
const PREVIEW_SECTION_IDS = ["health", "coverage", "assignments"];
const RULE_PRESETS = [
  {
    id: "balanced",
    label: "Balanced",
    description: "Stay close to the live feasibility recommendation and keep fairness rules on.",
  },
  {
    id: "max_games",
    label: "Max games",
    description: "Push for more games by relaxing balance constraints and allowing doubleheaders.",
  },
  {
    id: "conservative",
    label: "Conservative",
    description: "Reduce weekly load, tighten guardrails, and lean on the strongest weeknights.",
  },
];
const EMPTY_POSTSEASON_DATES = {
  poolStart: "",
  poolEnd: "",
  bracketStart: "",
  bracketEnd: "",
};

function getCommonHolidays(year) {
  const holidays = [];
  holidays.push({ label: "New Year's Day", startDate: `${year}-01-01`, endDate: `${year}-01-01` });

  const memorialDay = new Date(Date.UTC(year, 4, 31));
  while (memorialDay.getUTCDay() !== 1) memorialDay.setUTCDate(memorialDay.getUTCDate() - 1);
  const memMM = String(memorialDay.getUTCMonth() + 1).padStart(2, "0");
  const memDD = String(memorialDay.getUTCDate()).padStart(2, "0");
  holidays.push({ label: "Memorial Day", startDate: `${year}-${memMM}-${memDD}`, endDate: `${year}-${memMM}-${memDD}` });

  holidays.push({ label: "Independence Day", startDate: `${year}-07-04`, endDate: `${year}-07-04` });

  const laborDay = new Date(Date.UTC(year, 8, 1));
  while (laborDay.getUTCDay() !== 1) laborDay.setUTCDate(laborDay.getUTCDate() + 1);
  const labMM = String(laborDay.getUTCMonth() + 1).padStart(2, "0");
  const labDD = String(laborDay.getUTCDate()).padStart(2, "0");
  holidays.push({ label: "Labor Day", startDate: `${year}-${labMM}-${labDD}`, endDate: `${year}-${labMM}-${labDD}` });

  const thanksgiving = new Date(Date.UTC(year, 10, 1));
  let thursdayCount = 0;
  while (thursdayCount < 4) {
    if (thanksgiving.getUTCDay() === 4) thursdayCount++;
    if (thursdayCount < 4) thanksgiving.setUTCDate(thanksgiving.getUTCDate() + 1);
  }
  const thkMM = String(thanksgiving.getUTCMonth() + 1).padStart(2, "0");
  const thkDD = String(thanksgiving.getUTCDate()).padStart(2, "0");
  holidays.push({ label: "Thanksgiving", startDate: `${year}-${thkMM}-${thkDD}`, endDate: `${year}-${thkMM}-${thkDD}` });

  holidays.push({ label: "Christmas", startDate: `${year}-12-25`, endDate: `${year}-12-25` });
  return holidays;
}

function isoDayShort(value) {
  if (!value) return "";
  const parts = value.split("-");
  if (parts.length !== 3) return "";
  const year = Number(parts[0]);
  const month = Number(parts[1]) - 1;
  const day = Number(parts[2]);
  const dt = new Date(Date.UTC(year, month, day));
  if (Number.isNaN(dt.getTime())) return "";
  return WEEKDAY_OPTIONS[(dt.getUTCDay() + 6) % 7] || "";
}

function normalizeSlotType(value) {
  const raw = String(value || "").trim().toLowerCase();
  if (raw === "game" || raw === "both") return raw;
  return "practice";
}

function slotTypeSelectTitle(value) {
  const normalizedValue = normalizeSlotType(value);
  const activeOption = SLOT_TYPE_OPTIONS.find((option) => option.value === normalizedValue);
  const activeText = activeOption ? `${activeOption.shortLabel} = ${activeOption.label}` : "";
  return [activeText, "P = Practice", "G = Game", "B = Both"]
    .filter(Boolean)
    .join(" | ");
}

function getSlotTypeAppearance(value) {
  const normalizedValue = normalizeSlotType(value);
  return SLOT_TYPE_APPEARANCE[normalizedValue] || SLOT_TYPE_APPEARANCE.practice;
}

function normalizeRequestErrorMessage(error, fallbackMessage) {
  const rawMessage = String(error?.message || "").trim();
  if (!rawMessage) return fallbackMessage;
  if (rawMessage === "Failed to fetch") {
    return `${fallbackMessage} The scheduler service did not respond.`;
  }
  return rawMessage;
}

function normalizePriorityRank(value) {
  if (value === "" || value === null || value === undefined) return "";
  const parsed = Number(value);
  if (!Number.isFinite(parsed)) return "";
  const rounded = Math.trunc(parsed);
  return rounded > 0 ? String(rounded) : "";
}

function isIsoDate(value) {
  return /^\d{4}-\d{2}-\d{2}$/.test(String(value || ""));
}

function maxIsoDate(a, b) {
  const left = isIsoDate(a) ? a : "";
  const right = isIsoDate(b) ? b : "";
  if (left && right) return left > right ? left : right;
  return left || right || "";
}

function isIsoDateInRange(value, from, to) {
  if (!isIsoDate(value) || !isIsoDate(from) || !isIsoDate(to)) return false;
  return value >= from && value <= to;
}

function addIsoDays(value, days) {
  if (!isIsoDate(value)) return "";
  const [y, m, d] = value.split("-").map((part) => Number(part));
  const dt = new Date(Date.UTC(y, m - 1, d));
  if (Number.isNaN(dt.getTime())) return "";
  dt.setUTCDate(dt.getUTCDate() + Number(days || 0));
  const yy = dt.getUTCFullYear();
  const mm = String(dt.getUTCMonth() + 1).padStart(2, "0");
  const dd = String(dt.getUTCDate()).padStart(2, "0");
  return `${yy}-${mm}-${dd}`;
}

function isoUtcWeekday(value) {
  if (!isIsoDate(value)) return null;
  const [y, m, d] = value.split("-").map((part) => Number(part));
  const dt = new Date(Date.UTC(y, m - 1, d));
  if (Number.isNaN(dt.getTime())) return null;
  return dt.getUTCDay();
}

function buildDefaultPostseasonDates(seasonStart, seasonEnd) {
  if (!isIsoDate(seasonStart) || !isIsoDate(seasonEnd) || seasonStart > seasonEnd) {
    return { ...EMPTY_POSTSEASON_DATES };
  }

  const seasonEndWeekday = isoUtcWeekday(seasonEnd);
  if (seasonEndWeekday == null) {
    return { ...EMPTY_POSTSEASON_DATES };
  }

  const lastWeekendStart = addIsoDays(seasonEnd, -((seasonEndWeekday + 1) % 7));
  if (!lastWeekendStart) {
    return { ...EMPTY_POSTSEASON_DATES };
  }

  const championshipStart = lastWeekendStart < seasonStart ? seasonStart : lastWeekendStart;
  const idealChampionshipEnd = addIsoDays(lastWeekendStart, 1);
  const championshipEnd =
    idealChampionshipEnd && idealChampionshipEnd <= seasonEnd ? idealChampionshipEnd : seasonEnd;

  const idealPoolStart = addIsoDays(lastWeekendStart, -6);
  const idealPoolEnd = addIsoDays(lastWeekendStart, -1);
  const poolStart = idealPoolStart && idealPoolStart >= seasonStart ? idealPoolStart : seasonStart;
  const poolEnd = idealPoolEnd && idealPoolEnd <= seasonEnd ? idealPoolEnd : seasonEnd;
  const hasPoolWindow = poolStart && poolEnd && poolStart <= poolEnd;

  return {
    poolStart: hasPoolWindow ? poolStart : "",
    poolEnd: hasPoolWindow ? poolEnd : "",
    bracketStart: championshipStart,
    bracketEnd: championshipEnd >= championshipStart ? championshipEnd : championshipStart,
  };
}

function weekStartIso(value) {
  if (!isIsoDate(value)) return "";
  const [y, m, d] = value.split("-").map((part) => Number(part));
  const dt = new Date(Date.UTC(y, m - 1, d));
  if (Number.isNaN(dt.getTime())) return "";
  const monBasedDay = (dt.getUTCDay() + 6) % 7;
  dt.setUTCDate(dt.getUTCDate() - monBasedDay);
  const yy = dt.getUTCFullYear();
  const mm = String(dt.getUTCMonth() + 1).padStart(2, "0");
  const dd = String(dt.getUTCDate()).padStart(2, "0");
  return `${yy}-${mm}-${dd}`;
}

function buildIsoWeekKeys(from, to) {
  if (!isIsoDate(from) || !isIsoDate(to) || from > to) return [];
  const startWeek = weekStartIso(from);
  const endWeek = weekStartIso(to);
  if (!startWeek || !endWeek || startWeek > endWeek) return [];
  const keys = [];
  let cursor = startWeek;
  while (cursor && cursor <= endWeek && keys.length < 600) {
    keys.push(cursor);
    cursor = addIsoDays(cursor, 7);
  }
  return keys;
}

function estimateRoundRobinMatchups(teamCount) {
  if (!Number.isFinite(teamCount) || teamCount < 2) return 0;
  return Math.floor((teamCount * (teamCount - 1)) / 2);
}

function normalizeTeamPairKey(teamA, teamB) {
  const left = String(teamA || "").trim();
  const right = String(teamB || "").trim();
  if (!left || !right) return "";
  return left.localeCompare(right) <= 0 ? `${left}|${right}` : `${right}|${left}`;
}

function buildRoundRobinPairsForTeams(teamIds) {
  const teams = Array.isArray(teamIds) ? teamIds.map((v) => String(v || "").trim()).filter(Boolean) : [];
  if (teams.length < 2) return [];
  const list = [...teams];
  if (list.length % 2 === 1) list.push("BYE");
  const rounds = list.length - 1;
  const half = list.length / 2;
  const matchups = [];

  for (let round = 0; round < rounds; round += 1) {
    for (let i = 0; i < half; i += 1) {
      const teamA = list[i];
      const teamB = list[list.length - 1 - i];
      if (teamA === "BYE" || teamB === "BYE") continue;
      const homeTeamId = round % 2 === 0 ? teamA : teamB;
      const awayTeamId = round % 2 === 0 ? teamB : teamA;
      matchups.push({ homeTeamId, awayTeamId });
    }
    const last = list[list.length - 1];
    list.splice(list.length - 1, 1);
    list.splice(1, 0, last);
  }

  return matchups;
}

function buildTargetMatchupsForTeams(teamIds, gamesPerTeam) {
  const teams = Array.isArray(teamIds) ? teamIds.map((v) => String(v || "").trim()).filter(Boolean) : [];
  const targetGames = Math.max(0, Number(gamesPerTeam) || 0);
  if (teams.length < 2 || targetGames <= 0) return [];

  const counts = new Map(teams.map((teamId) => [teamId, 0]));
  const matchups = [];
  const rounds = Math.max(1, targetGames);

  for (let round = 0; round < rounds; round += 1) {
    let roundMatchups = buildRoundRobinPairsForTeams(teams);
    if (round % 2 === 1) {
      roundMatchups = roundMatchups.map((m) => ({ homeTeamId: m.awayTeamId, awayTeamId: m.homeTeamId }));
    }

    for (const matchup of roundMatchups) {
      const homeCount = counts.get(matchup.homeTeamId) || 0;
      const awayCount = counts.get(matchup.awayTeamId) || 0;
      if (homeCount >= targetGames || awayCount >= targetGames) continue;
      matchups.push(matchup);
      counts.set(matchup.homeTeamId, homeCount + 1);
      counts.set(matchup.awayTeamId, awayCount + 1);
      if ([...counts.values()].every((value) => value >= targetGames)) {
        return matchups;
      }
    }
  }

  return matchups;
}

function buildRepeatedMatchupsForTeams(teamIds, gamesPerTeam) {
  const teams = Array.isArray(teamIds) ? teamIds.map((v) => String(v || "").trim()).filter(Boolean) : [];
  const targetGames = Math.max(0, Number(gamesPerTeam) || 0);
  if (teams.length < 2 || targetGames <= 0) return [];

  const roundGames = Math.max(1, teams.length - 1);
  const fullRounds = Math.floor(targetGames / roundGames);
  const remainderGames = targetGames % roundGames;
  if (remainderGames > 0) return buildTargetMatchupsForTeams(teams, targetGames);

  const result = [];
  for (let round = 0; round < fullRounds; round += 1) {
    let matchups = buildRoundRobinPairsForTeams(teams);
    if (round % 2 === 1) {
      matchups = matchups.map((m) => ({ homeTeamId: m.awayTeamId, awayTeamId: m.homeTeamId }));
    }
    result.push(...matchups);
  }
  return result;
}

function suggestPriorityMatchupsFromDemand(teamIds, gamesPerTeam, maxSuggestions = MAX_RIVALRY_MATCHUPS) {
  const matchups = buildRepeatedMatchupsForTeams(teamIds, gamesPerTeam);
  const pairCounts = new Map();
  for (const matchup of matchups) {
    const key = normalizeTeamPairKey(matchup.homeTeamId, matchup.awayTeamId);
    if (!key) continue;
    const current = pairCounts.get(key) || { teamA: key.split("|")[0], teamB: key.split("|")[1], count: 0 };
    current.count += 1;
    pairCounts.set(key, current);
  }

  return [...pairCounts.values()]
    .filter((row) => row.count > 1)
    .sort((a, b) => {
      if (b.count !== a.count) return b.count - a.count;
      const left = `${a.teamA}|${a.teamB}`;
      const right = `${b.teamA}|${b.teamB}`;
      return left.localeCompare(right);
    })
    .slice(0, Math.max(1, Number(maxSuggestions) || MAX_RIVALRY_MATCHUPS))
    .map((row) => {
      const overage = Math.max(1, row.count - 1);
      const weight = Math.max(1, Math.min(10, 2 + overage * 2));
      return { teamA: row.teamA, teamB: row.teamB, weight, suggestedCount: row.count };
    });
}

function suggestPriorityMatchupsByAdjacency(teamIds, maxSuggestions = MAX_RIVALRY_MATCHUPS) {
  const teams = Array.isArray(teamIds) ? teamIds.map((v) => String(v || "").trim()).filter(Boolean) : [];
  if (teams.length < 2) return [];

  const suggestions = [];
  const seen = new Set();
  const max = Math.max(1, Number(maxSuggestions) || MAX_RIVALRY_MATCHUPS);

  const pushPair = (teamA, teamB, weight, reason) => {
    const key = normalizeTeamPairKey(teamA, teamB);
    if (!key || seen.has(key)) return;
    seen.add(key);
    const [a, b] = key.split("|");
    suggestions.push({ teamA: a, teamB: b, weight, suggestionReason: reason });
  };

  // Adjacent teams in current division order are often competitive peers (seed/order proxy).
  for (let i = 0; i < teams.length - 1 && suggestions.length < max; i += 1) {
    pushPair(teams[i], teams[i + 1], 4, "adjacent");
  }

  // Add near-adjacent / mirrored pairs for broader spread if we still have room.
  for (let i = 0; i < teams.length - 2 && suggestions.length < max; i += 1) {
    pushPair(teams[i], teams[i + 2], 3, "near-adjacent");
  }
  for (let i = 0; i < Math.floor(teams.length / 2) && suggestions.length < max; i += 1) {
    pushPair(teams[i], teams[teams.length - 1 - i], 2, "mirrored");
  }

  return suggestions.slice(0, max);
}

function suggestPriorityMatchupsComposite(teamIds, gamesPerTeam, maxSuggestions = MAX_RIVALRY_MATCHUPS) {
  const max = Math.max(1, Number(maxSuggestions) || MAX_RIVALRY_MATCHUPS);
  const demandBased = suggestPriorityMatchupsFromDemand(teamIds, gamesPerTeam, max);
  if (demandBased.length >= max) return demandBased.slice(0, max);

  const seen = new Set(demandBased.map((row) => normalizeTeamPairKey(row.teamA, row.teamB)).filter(Boolean));
  const fallback = suggestPriorityMatchupsByAdjacency(teamIds, max * 2)
    .filter((row) => {
      const key = normalizeTeamPairKey(row.teamA, row.teamB);
      if (!key || seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .slice(0, Math.max(0, max - demandBased.length));

  return [...demandBased, ...fallback].slice(0, max);
}

function buildSpringBreakRange(seasonStart, seasonEnd) {
  if (!isIsoDate(seasonStart) || !isIsoDate(seasonEnd)) return null;
  const from = seasonStart;
  const to = seasonEnd;
  const startYear = Number(seasonStart.slice(0, 4));
  const endYear = Number(seasonEnd.slice(0, 4));
  const years = [startYear, endYear].filter((value, idx, all) => Number.isFinite(value) && all.indexOf(value) === idx);
  if (!years.length) return null;

  const candidates = years.map((year) => ({
    startDate: `${year}-03-28`,
    endDate: `${year}-04-04`,
    label: "Spring Break",
  }));
  const intersectsSeason = (range) => !(range.endDate < from || range.startDate > to);
  return candidates.find(intersectsSeason) || candidates[0];
}

function extractSlotItems(payload) {
  if (Array.isArray(payload)) return payload;
  if (!payload || typeof payload !== "object") return [];
  if (Array.isArray(payload.items)) return payload.items;
  if (payload.data && typeof payload.data === "object" && Array.isArray(payload.data.items)) {
    return payload.data.items;
  }
  if (Array.isArray(payload.data)) return payload.data;
  return [];
}

function extractContinuationToken(payload) {
  if (!payload || typeof payload !== "object") return "";
  const token = payload.continuationToken || payload.nextContinuationToken || payload.nextToken || "";
  return String(token || "").trim();
}

function parseMinutes(raw) {
  const parts = String(raw || "").split(":");
  if (parts.length < 2) return null;
  const h = Number(parts[0]);
  const m = Number(parts[1]);
  if (!Number.isFinite(h) || !Number.isFinite(m)) return null;
  return h * 60 + m;
}

function normalizeClockInput(raw) {
  const minutes = parseMinutes(raw);
  if (minutes == null || minutes < 0 || minutes >= 24 * 60) return "";
  const h = String(Math.floor(minutes / 60)).padStart(2, "0");
  const m = String(minutes % 60).padStart(2, "0");
  return `${h}:${m}`;
}

function parseNoGamesDateText(raw) {
  const values = String(raw || "")
    .split(/[\s,;]+/)
    .map((value) => String(value || "").trim())
    .filter(Boolean);
  const unique = [];
  const seen = new Set();
  const invalid = [];
  values.forEach((value) => {
    if (!isIsoDate(value)) {
      invalid.push(value);
      return;
    }
    if (seen.has(value)) return;
    seen.add(value);
    unique.push(value);
  });
  unique.sort((a, b) => a.localeCompare(b));
  return { values: unique, invalid };
}

function isEngineTraceSource(source) {
  const value = String(source || "").trim().toLowerCase();
  return value === "schedule_engine_trace_v1" || value === "schedule_engine_replay_trace_v1";
}

function buildPreviewSwapRepairProposal(sourceAssignment, targetAssignment) {
  const source = sourceAssignment || {};
  const target = targetAssignment || {};
  if (source.isExternalOffer || target.isExternalOffer) return null;
  const sourceSlotId = String(source.slotId || "").trim();
  const targetSlotId = String(target.slotId || "").trim();
  if (!sourceSlotId || !targetSlotId || sourceSlotId === targetSlotId) return null;

  const sourceGame = {
    slotId: sourceSlotId,
    gameDate: String(source.gameDate || "").trim(),
    startTime: String(source.startTime || "").trim(),
    endTime: String(source.endTime || "").trim(),
    fieldKey: String(source.fieldKey || "").trim(),
    homeTeamId: String(source.homeTeamId || "").trim(),
    awayTeamId: String(source.awayTeamId || "").trim(),
  };
  const targetGame = {
    slotId: targetSlotId,
    gameDate: String(target.gameDate || "").trim(),
    startTime: String(target.startTime || "").trim(),
    endTime: String(target.endTime || "").trim(),
    fieldKey: String(target.fieldKey || "").trim(),
    homeTeamId: String(target.homeTeamId || "").trim(),
    awayTeamId: String(target.awayTeamId || "").trim(),
  };
  if (!sourceGame.homeTeamId || !sourceGame.awayTeamId || !targetGame.homeTeamId || !targetGame.awayTeamId) return null;

  return {
    proposalId: `drag-swap-${sourceSlotId}-${targetSlotId}`,
    title: "Drag-and-drop swap (preview)",
    rationale: "Swap two regular-season games and revalidate the preview.",
    fixesRuleIds: [],
    requiresUserAction: false,
    changes: [
      {
        changeType: "move",
        fromSlotId: sourceSlotId,
        toSlotId: targetSlotId,
        before: sourceGame,
        after: {
          ...targetGame,
          homeTeamId: sourceGame.homeTeamId,
          awayTeamId: sourceGame.awayTeamId,
        },
      },
      {
        changeType: "move",
        fromSlotId: targetSlotId,
        toSlotId: sourceSlotId,
        before: targetGame,
        after: {
          ...sourceGame,
          homeTeamId: targetGame.homeTeamId,
          awayTeamId: targetGame.awayTeamId,
        },
      },
    ],
    beforeAfterSummary: {
      repairMoveType: "swap",
      source: "wizard_drag_drop_preview_v1",
    },
  };
}

function readObjectString(obj, key) {
  if (!obj || typeof obj !== "object") return "";
  const value = obj[key];
  if (value == null) return "";
  return String(value).trim();
}

function buildRepairProposalScope(proposal) {
  if (!proposal || typeof proposal !== "object") return null;
  const changes = Array.isArray(proposal.changes) ? proposal.changes : [];
  const slotIds = new Set();
  const teamIds = new Set();
  const weekKeys = new Set();
  const fieldKeys = new Set();
  const dates = new Set();
  const moveSummaries = [];

  const collectEndpoint = (endpoint, fallbackSlotId) => {
    if (!endpoint || typeof endpoint !== "object") return null;
    const slotId = readObjectString(endpoint, "slotId") || String(fallbackSlotId || "").trim();
    const gameDate = readObjectString(endpoint, "gameDate");
    const startTime = readObjectString(endpoint, "startTime");
    const endTime = readObjectString(endpoint, "endTime");
    const fieldKey = readObjectString(endpoint, "fieldKey");
    const homeTeamId = readObjectString(endpoint, "homeTeamId");
    const awayTeamId = readObjectString(endpoint, "awayTeamId");
    if (slotId) slotIds.add(slotId);
    if (gameDate) {
      dates.add(gameDate);
      const weekKey = weekStartIso(gameDate);
      if (weekKey) weekKeys.add(weekKey);
    }
    if (fieldKey) fieldKeys.add(fieldKey);
    if (homeTeamId) teamIds.add(homeTeamId);
    if (awayTeamId) teamIds.add(awayTeamId);
    return { slotId, gameDate, startTime, endTime, fieldKey, homeTeamId, awayTeamId };
  };

  changes.forEach((change, idx) => {
    const changeType = String(change?.changeType || "").trim().toLowerCase();
    const fromSlotId = String(change?.fromSlotId || "").trim();
    const toSlotId = String(change?.toSlotId || "").trim();
    if (fromSlotId) slotIds.add(fromSlotId);
    if (toSlotId) slotIds.add(toSlotId);
    const before = collectEndpoint(change?.before, fromSlotId);
    const after = collectEndpoint(change?.after, toSlotId);
    if (changeType === "move") {
      moveSummaries.push({
        key: `${idx}-${fromSlotId}-${toSlotId}`,
        from: before,
        after,
      });
    }
  });

  const ruleIds = (Array.isArray(proposal.fixesRuleIds) ? proposal.fixesRuleIds : [])
    .map((value) => String(value || "").trim())
    .filter(Boolean);

  return {
    proposalId: String(proposal.proposalId || "").trim(),
    title: String(proposal.title || "").trim(),
    rationale: String(proposal.rationale || "").trim(),
    ruleIds,
    changeTypes: changes.map((change) => String(change?.changeType || "").trim()).filter(Boolean),
    slotIds: [...slotIds].sort((a, b) => a.localeCompare(b)),
    teamIds: [...teamIds].sort((a, b) => a.localeCompare(b)),
    weekKeys: [...weekKeys].sort((a, b) => a.localeCompare(b)),
    fieldKeys: [...fieldKeys].sort((a, b) => a.localeCompare(b)),
    dates: [...dates].sort((a, b) => a.localeCompare(b)),
    moveSummaries,
  };
}

function summarizeRepairProposalPriorityImpact(scope, priorityPairInfoByKey) {
  if (!scope || typeof scope !== "object") return null;
  if (!(priorityPairInfoByKey instanceof Map) || priorityPairInfoByKey.size === 0) return null;
  const moves = Array.isArray(scope.moveSummaries) ? scope.moveSummaries : [];
  if (!moves.length) return null;

  const touched = [];
  const byPair = new Map();

  const compareIso = (fromDate, toDate) => {
    const from = String(fromDate || "").trim();
    const to = String(toDate || "").trim();
    if (!isIsoDate(from) || !isIsoDate(to)) return 0;
    if (to > from) return 1;
    if (to < from) return -1;
    return 0;
  };

  for (const move of moves) {
    const from = move?.from || null;
    const after = move?.after || null;
    const homeTeamId = String(from?.homeTeamId || after?.homeTeamId || "").trim();
    const awayTeamId = String(from?.awayTeamId || after?.awayTeamId || "").trim();
    const pairKey = normalizeTeamPairKey(homeTeamId, awayTeamId);
    if (!pairKey) continue;

    const priorityInfo = priorityPairInfoByKey.get(pairKey);
    if (!priorityInfo || (!priorityInfo.manualPriorityWeight && !priorityInfo.autoRepeatPriority)) continue;

    const direction = compareIso(from?.gameDate, after?.gameDate);
    const dirLabel = direction > 0 ? "later" : direction < 0 ? "earlier" : "same-week/date";
    const touch = {
      pairKey,
      pairLabel: `${priorityInfo.teamA} vs ${priorityInfo.teamB}`,
      manualPriorityWeight: Number(priorityInfo.manualPriorityWeight || 0),
      autoRepeatPriority: !!priorityInfo.autoRepeatPriority,
      direction,
      directionLabel: dirLabel,
      fromDate: String(from?.gameDate || "").trim(),
      toDate: String(after?.gameDate || "").trim(),
    };
    touched.push(touch);

    const agg = byPair.get(pairKey) || {
      ...touch,
      laterMoves: 0,
      earlierMoves: 0,
      sameMoves: 0,
    };
    if (direction > 0) agg.laterMoves += 1;
    else if (direction < 0) agg.earlierMoves += 1;
    else agg.sameMoves += 1;
    byPair.set(pairKey, agg);
  }

  if (!touched.length) return null;

  const totals = {
    manualLater: 0,
    manualEarlier: 0,
    repeatLater: 0,
    repeatEarlier: 0,
    same: 0,
  };
  touched.forEach((item) => {
    if (item.direction === 0) totals.same += 1;
    if (item.manualPriorityWeight > 0) {
      if (item.direction > 0) totals.manualLater += 1;
      if (item.direction < 0) totals.manualEarlier += 1;
    }
    if (item.autoRepeatPriority) {
      if (item.direction > 0) totals.repeatLater += 1;
      if (item.direction < 0) totals.repeatEarlier += 1;
    }
  });

  const summaryParts = [];
  if (totals.manualLater > 0) summaryParts.push(`${totals.manualLater} manual priority move(s) later`);
  if (totals.manualEarlier > 0) summaryParts.push(`${totals.manualEarlier} manual priority move(s) earlier`);
  if (totals.repeatLater > 0) summaryParts.push(`${totals.repeatLater} repeat-priority move(s) later`);
  if (totals.repeatEarlier > 0) summaryParts.push(`${totals.repeatEarlier} repeat-priority move(s) earlier`);
  if (!summaryParts.length && totals.same > 0) summaryParts.push(`${totals.same} priority move(s) without date change`);

  const pairDetails = [...byPair.values()]
    .sort((a, b) => {
      const manualDiff = (b.manualPriorityWeight || 0) - (a.manualPriorityWeight || 0);
      if (manualDiff !== 0) return manualDiff;
      const repeatDiff = Number(b.autoRepeatPriority) - Number(a.autoRepeatPriority);
      if (repeatDiff !== 0) return repeatDiff;
      return String(a.pairLabel || "").localeCompare(String(b.pairLabel || ""));
    })
    .slice(0, 3)
    .map((item) => {
      const tags = [];
      if (item.manualPriorityWeight > 0) tags.push(`manual w${item.manualPriorityWeight}`);
      if (item.autoRepeatPriority) tags.push("repeat");
      const movement =
        item.laterMoves > 0 && item.earlierMoves > 0
          ? "mixed (earlier + later)"
          : item.laterMoves > 0
            ? "later"
            : item.earlierMoves > 0
              ? "earlier"
              : "same date";
      return `${item.pairLabel} (${tags.join(", ")}) -> ${movement}`;
    });

  return {
    touchedCount: touched.length,
    summary: summaryParts.join("; "),
    pairDetails,
    manualLater: totals.manualLater,
    manualEarlier: totals.manualEarlier,
    repeatLater: totals.repeatLater,
    repeatEarlier: totals.repeatEarlier,
    netScore:
      (totals.manualLater * 4) +
      (totals.repeatLater * 2) -
      (totals.manualEarlier * 5) -
      (totals.repeatEarlier * 2),
    hasManualEarlier: totals.manualEarlier > 0,
    hasRepeatEarlier: totals.repeatEarlier > 0,
    hasAnyLater: totals.manualLater > 0 || totals.repeatLater > 0,
  };
}

function classifyRepairPriorityImpact(impact) {
  if (!impact) return null;
  const manualLater = Number(impact.manualLater || 0);
  const manualEarlier = Number(impact.manualEarlier || 0);
  const repeatLater = Number(impact.repeatLater || 0);
  const repeatEarlier = Number(impact.repeatEarlier || 0);
  const netScore = Number(impact.netScore || 0);

  if (manualEarlier > 0 || repeatEarlier > 0) {
    if (manualLater > 0 || repeatLater > 0) {
      return {
        label: "Priority mixed",
        tone: "warning",
        title: "Moves some priority pairs later but also pulls some earlier.",
        score: netScore,
      };
    }
    return {
      label: "Priority risk",
      tone: "error",
      title: "Moves priority pairs earlier.",
      score: netScore,
    };
  }

  if (manualLater > 0) {
    return {
      label: "Priority +",
      tone: "success",
      title: "Improves manual priority matchup placement (moves later).",
      score: netScore,
    };
  }
  if (repeatLater > 0) {
    return {
      label: "Priority +",
      tone: "ok",
      title: "Improves repeat-priority matchup placement (moves later).",
      score: netScore,
    };
  }
  return {
    label: "Priority neutral",
    tone: "neutral",
    title: "No clear priority-pair placement impact detected.",
    score: netScore,
  };
}

function repairPriorityBadgeStyle(tone) {
  switch (String(tone || "").toLowerCase()) {
    case "success":
      return { background: "#dcfce7", border: "1px solid #86efac", color: "#166534" };
    case "ok":
      return { background: "#e0f2fe", border: "1px solid #7dd3fc", color: "#0c4a6e" };
    case "warning":
      return { background: "#fef3c7", border: "1px solid #fcd34d", color: "#92400e" };
    case "error":
      return { background: "#fee2e2", border: "1px solid #fca5a5", color: "#991b1b" };
    default:
      return { background: "#f3f4f6", border: "1px solid #d1d5db", color: "#374151" };
  }
}

function buildRuleGroupFocusScope(group) {
  if (!group || typeof group !== "object") return null;
  const ruleId = String(group?.ruleId || "").trim();
  const severity = String(group?.severity || "").trim().toLowerCase();
  const summary = String(group?.summary || "").trim();
  const slotIds = new Set();
  const teamIds = new Set();
  const weekKeys = new Set();

  const violations = Array.isArray(group?.violations) ? group.violations : [];
  violations.forEach((violation) => {
    (Array.isArray(violation?.slotIds) ? violation.slotIds : []).forEach((value) => {
      const slotId = String(value || "").trim();
      if (slotId) slotIds.add(slotId);
    });
    (Array.isArray(violation?.teamIds) ? violation.teamIds : []).forEach((value) => {
      const teamId = String(value || "").trim();
      if (teamId) teamIds.add(teamId);
    });
    (Array.isArray(violation?.weekKeys) ? violation.weekKeys : []).forEach((value) => {
      const weekKey = String(value || "").trim();
      if (weekKey) weekKeys.add(weekKey);
    });
  });

  return {
    key: `${severity}:${ruleId}`,
    ruleId,
    severity,
    summary,
    slotIds: [...slotIds].sort((a, b) => a.localeCompare(b)),
    teamIds: [...teamIds].sort((a, b) => a.localeCompare(b)),
    weekKeys: [...weekKeys].sort((a, b) => a.localeCompare(b)),
    fieldKeys: [],
    ruleIds: ruleId ? [ruleId] : [],
    source: "rule-health",
  };
}

function formatMinutesAsTime(totalMinutes) {
  const value = Math.trunc(Number(totalMinutes));
  if (!Number.isFinite(value) || value < 0 || value >= 24 * 60) return "";
  const h = String(Math.floor(value / 60)).padStart(2, "0");
  const m = String(value % 60).padStart(2, "0");
  return `${h}:${m}`;
}

function computeSlotScore(slot, weekday, patternCounts, dayCounts) {
  const patternKey = `${weekday}|${slot.startTime || ""}|${slot.endTime || ""}|${slot.fieldKey || ""}`;
  const patternCount = patternCounts.get(patternKey) || 1;
  const dayCount = dayCounts.get(weekday) || 0;
  const start = parseMinutes(slot.startTime);
  const end = parseMinutes(slot.endTime);
  const duration = start != null && end != null && end > start ? end - start : 0;
  const durationBucket = Math.min(20, Math.floor(duration / 15));
  return patternCount * 100 + dayCount + durationBucket;
}

function patternKeyFromParts(weekday, startTime, endTime, fieldKey) {
  return `${weekday || ""}|${startTime || ""}|${endTime || ""}|${fieldKey || ""}`;
}

function dayOrderIndex(weekday) {
  const idx = WEEKDAY_OPTIONS.indexOf(weekday || "");
  return idx >= 0 ? idx : 999;
}

function assignmentExplainKey(assignment) {
  const phase = String(assignment?.phase || "").trim();
  const slotId = String(assignment?.slotId || "").trim();
  const gameDate = String(assignment?.gameDate || "").trim();
  const startTime = String(assignment?.startTime || "").trim();
  const homeTeamId = String(assignment?.homeTeamId || "").trim();
  const awayTeamId = String(assignment?.awayTeamId || "").trim();
  return [phase, slotId, gameDate, startTime, homeTeamId, awayTeamId].join("|");
}

function StepButton({ active, status = "neutral", onClick, children, title = "" }) {
  const stylesByStatus = {
    complete: {
      backgroundColor: "#1f7a43",
      borderColor: "#1f7a43",
      color: "#fff",
    },
    error: {
      backgroundColor: "#b42318",
      borderColor: "#b42318",
      color: "#fff",
    },
    active: {
      backgroundColor: "#0f172a",
      borderColor: "#0f172a",
      color: "#fff",
    },
    neutral: {
      backgroundColor: "#f8fafc",
      borderColor: "#cbd5e1",
      color: "#0f172a",
    },
  };
  const style = stylesByStatus[status] || stylesByStatus.neutral;
  return (
    <button
      className={`btn btn--ghost ${active ? "is-active" : ""}`}
      type="button"
      onClick={onClick}
      title={title}
      style={{
        ...style,
        boxShadow: active ? "0 0 0 2px rgba(15,23,42,0.25)" : "none",
      }}
    >
      {children}
    </button>
  );
}

export default function SeasonWizard({ leagueId, tableView = "A" }) {
  const [division, setDivision] = useState("");
  const [divisions, setDivisions] = useState([]);
  const [leagueSeasonConfig, setLeagueSeasonConfig] = useState({});

  // Default season dates for Little League (Mar 15 - Jun 6)
  const defaultYear = new Date().getFullYear();
  const [seasonStart, setSeasonStart] = useState(`${defaultYear}-03-15`);
  const [seasonEnd, setSeasonEnd] = useState(`${defaultYear}-06-06`);
  const [poolStart, setPoolStart] = useState("");
  const [poolEnd, setPoolEnd] = useState("");
  const [bracketStart, setBracketStart] = useState("");
  const [bracketEnd, setBracketEnd] = useState("");

  const [minGamesPerTeam, setMinGamesPerTeam] = useState(13);
  const [poolGamesPerTeam, setPoolGamesPerTeam] = useState(2);
  const [guestGamesPerWeek, setGuestGamesPerWeek] = useState(0);
  const [maxExternalOffersPerTeamSeason, setMaxExternalOffersPerTeamSeason] = useState(0);
  const [resetGeneratedSlotsBeforeApply, setResetGeneratedSlotsBeforeApply] = useState(true);
  const [blockSpringBreak, setBlockSpringBreak] = useState(false);
  const [blockedHolidays, setBlockedHolidays] = useState(new Set());
  const [maxGamesPerWeek, setMaxGamesPerWeek] = useState(2);
  const [noDoubleHeaders, setNoDoubleHeaders] = useState(true);
  const [balanceHomeAway, setBalanceHomeAway] = useState(true);
  const [noGamesOnDatesText, setNoGamesOnDatesText] = useState("");
  const [noGamesBeforeTime, setNoGamesBeforeTime] = useState("");
  const [noGamesAfterTime, setNoGamesAfterTime] = useState("");
  const [teamCount, setTeamCount] = useState(0);
  const [divisionTeams, setDivisionTeams] = useState([]);
  const [rivalryMatchups, setRivalryMatchups] = useState([]);

  const [step, setStep] = useState(0);
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [repairApplyingId, setRepairApplyingId] = useState("");
  const [selectedRepairProposalId, setSelectedRepairProposalId] = useState("");
  const [selectedRuleFocusKey, setSelectedRuleFocusKey] = useState("");
  const [selectedExplainGameKey, setSelectedExplainGameKey] = useState("");
  const [dragSwapSourceKey, setDragSwapSourceKey] = useState("");
  const [dragSwapTargetKey, setDragSwapTargetKey] = useState("");
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);
  const [slotPlan, setSlotPlan] = useState([]);
  const [guestAnchorPrimarySlotId, setGuestAnchorPrimarySlotId] = useState("");
  const [guestAnchorSecondarySlotId, setGuestAnchorSecondarySlotId] = useState("");
  const [availabilityLoading, setAvailabilityLoading] = useState(false);
  const [availabilityErr, setAvailabilityErr] = useState("");

  // Feasibility state
  const [feasibility, setFeasibility] = useState(null);
  const [feasibilityLoading, setFeasibilityLoading] = useState(false);
  const [hasAutoApplied, setHasAutoApplied] = useState(false);
  const [assignmentPhaseFilter, setAssignmentPhaseFilter] = useState("");
  const [assignmentTeamFilter, setAssignmentTeamFilter] = useState("");
  const [assignmentFieldFilter, setAssignmentFieldFilter] = useState("");
  const [showHighlightedAssignmentsOnly, setShowHighlightedAssignmentsOnly] = useState(false);
  const availabilityCacheRef = useRef(new Map());
  const availabilityRequestIdRef = useRef(0);
  const autoPostseasonDefaultsRef = useRef({ ...EMPTY_POSTSEASON_DATES });
  const previewSectionControl = useCollapsibleSectionControl(PREVIEW_SECTION_IDS);
  const steps = WIZARD_STEPS;
  const currentStepMeta = steps[step] || steps[0];

  useEffect(() => {
    if (!leagueId) return;
    (async () => {
      setErr("");
      try {
        const [divs, league] = await Promise.all([
          apiFetch("/api/divisions"),
          apiFetch("/api/league"),
        ]);
        const list = Array.isArray(divs) ? divs : [];
        setDivisions(list);
        if (list.length) {
          setDivision((prev) => prev || list[0].code || list[0].division || "");
        }
        const season = league?.season || {};
        setLeagueSeasonConfig(season);
        setSeasonStart(season.springStart || "");
        setSeasonEnd(season.springEnd || "");
      } catch (e) {
        setErr(e?.message || "Failed to load wizard data.");
      }
    })();
  }, [leagueId]);

  useEffect(() => {
    const previousDefaults = autoPostseasonDefaultsRef.current;
    const nextDefaults = buildDefaultPostseasonDates(seasonStart, seasonEnd);

    setPoolStart((prev) => (!prev || prev === previousDefaults.poolStart ? nextDefaults.poolStart : prev));
    setPoolEnd((prev) => (!prev || prev === previousDefaults.poolEnd ? nextDefaults.poolEnd : prev));
    setBracketStart((prev) => (!prev || prev === previousDefaults.bracketStart ? nextDefaults.bracketStart : prev));
    setBracketEnd((prev) => (!prev || prev === previousDefaults.bracketEnd ? nextDefaults.bracketEnd : prev));

    autoPostseasonDefaultsRef.current = nextDefaults;
  }, [seasonStart, seasonEnd]);

  const springBreakRange = useMemo(
    () => buildSpringBreakRange(seasonStart, seasonEnd),
    [seasonStart, seasonEnd]
  );

  const normalizedDivisionTeams = useMemo(
    () =>
      (divisionTeams || [])
        .map((team) => ({
          teamId: String(team?.teamId || "").trim(),
          name: String(team?.name || "").trim(),
        }))
        .filter((team) => team.teamId),
    [divisionTeams]
  );
  const teamNameById = useMemo(
    () =>
      new Map(
        normalizedDivisionTeams.map((team) => [team.teamId, team.name || team.teamId])
      ),
    [normalizedDivisionTeams]
  );

  const rivalryRowIssues = useMemo(() => {
    const issues = [];
    const seen = new Set();
    (rivalryMatchups || []).forEach((row, idx) => {
      const teamA = String(row?.teamA || "").trim();
      const teamB = String(row?.teamB || "").trim();
      const hasAnyTeam = teamA || teamB;
      if (!hasAnyTeam) return;

      if (!teamA || !teamB) {
        issues.push(`Rivalry row ${idx + 1}: select both teams.`);
        return;
      }
      if (teamA === teamB) {
        issues.push(`Rivalry row ${idx + 1}: teams must be different.`);
        return;
      }

      const key = [teamA, teamB].sort().join("|");
      if (seen.has(key)) {
        issues.push(`Rivalry row ${idx + 1}: duplicate pairing (${teamA}/${teamB}).`);
        return;
      }
      seen.add(key);

      const rawWeight = Number(row?.weight);
      if (!Number.isFinite(rawWeight) || rawWeight <= 0) {
        issues.push(`Rivalry row ${idx + 1}: weight must be greater than 0.`);
      }
    });
    return issues;
  }, [rivalryMatchups]);

  const rivalryPayload = useMemo(
    () =>
      (rivalryMatchups || [])
        .map((row) => ({
          teamA: String(row?.teamA || "").trim(),
          teamB: String(row?.teamB || "").trim(),
          weight: Number(row?.weight),
        }))
        .filter((row) => row.teamA && row.teamB && row.teamA !== row.teamB && Number.isFinite(row.weight) && row.weight > 0)
        .slice(0, MAX_RIVALRY_MATCHUPS)
        .map((row) => ({ ...row, weight: Math.max(1, Math.min(10, Math.round(row.weight)))})),
    [rivalryMatchups]
  );

  const parsedNoGamesOnDates = useMemo(
    () => parseNoGamesDateText(noGamesOnDatesText),
    [noGamesOnDatesText]
  );

  const leagueRuleIssues = useMemo(() => {
    const issues = [];
    if (parsedNoGamesOnDates.invalid.length) {
      issues.push(`No-games dates must be YYYY-MM-DD. Invalid: ${parsedNoGamesOnDates.invalid.slice(0, 4).join(", ")}${parsedNoGamesOnDates.invalid.length > 4 ? "..." : ""}`);
    }
    const before = noGamesBeforeTime ? normalizeClockInput(noGamesBeforeTime) : "";
    const after = noGamesAfterTime ? normalizeClockInput(noGamesAfterTime) : "";
    if (noGamesBeforeTime && !before) {
      issues.push("No games before time must use HH:MM.");
    }
    if (noGamesAfterTime && !after) {
      issues.push("No games after time must use HH:MM.");
    }
    if (before && after) {
      const beforeMin = parseMinutes(before);
      const afterMin = parseMinutes(after);
      if (beforeMin != null && afterMin != null && beforeMin >= afterMin) {
        issues.push("No games before time must be earlier than no games after time.");
      }
    }
    return issues;
  }, [parsedNoGamesOnDates, noGamesBeforeTime, noGamesAfterTime]);

  const activeBlockedRanges = useMemo(() => {
    const ranges = [];
    if (blockSpringBreak && springBreakRange) ranges.push(springBreakRange);

    if (seasonStart) {
      const year = Number(seasonStart.split("-")[0]);
      if (!isNaN(year)) {
        const holidays = getCommonHolidays(year);
        holidays.forEach((h) => {
          if (blockedHolidays.has(h.label)) {
            ranges.push(h);
          }
        });
        if (seasonEnd) {
          const endYear = Number(seasonEnd.split("-")[0]);
          if (endYear > year) {
            const nextYearHolidays = getCommonHolidays(endYear);
            nextYearHolidays.forEach((h) => {
              if (blockedHolidays.has(h.label)) {
                ranges.push(h);
              }
            });
          }
        }
      }
    }

    return ranges;
  }, [blockSpringBreak, springBreakRange, blockedHolidays, seasonStart, seasonEnd]);

  const slotPlanSummary = useMemo(() => {
    const total = slotPlan.length;
    const practice = slotPlan.filter((s) => s.slotType === "practice").length;
    const game = slotPlan.filter((s) => s.slotType === "game").length;
    const both = slotPlan.filter((s) => s.slotType === "both").length;
    const ranked = slotPlan.filter((s) => Number(s.priorityRank) > 0);
    const uniqueRankedPatterns = new Set(ranked.map((s) => s.basePatternKey)).size;
    return { total, practice, game, both, ranked: uniqueRankedPatterns, gameCapable: game + both };
  }, [slotPlan]);
  const slotFieldTotals = useMemo(() => {
    const totals = new Map();
    (slotPlan || []).forEach((slot) => {
      const fieldKey = String(slot?.fieldKey || "").trim();
      if (!fieldKey) return;
      totals.set(fieldKey, (totals.get(fieldKey) || 0) + 1);
    });
    return totals;
  }, [slotPlan]);

  const slotPatterns = useMemo(() => {
    const map = new Map();
    for (const slot of slotPlan) {
      const key = slot.basePatternKey || patternKeyFromParts(slot.weekday, slot.baseStartTime, slot.baseEndTime, slot.fieldKey);
      if (!map.has(key)) {
        map.set(key, {
          key,
          weekday: slot.weekday,
          fieldKey: slot.fieldKey,
          startTime: slot.startTime,
          endTime: slot.endTime,
          baseStartTime: slot.baseStartTime || slot.startTime,
          baseEndTime: slot.baseEndTime || slot.endTime,
          slotType: slot.slotType,
          priorityRank: slot.priorityRank,
          count: 0,
          scoreTotal: 0,
          scoreMax: slot.score || 0,
          firstDate: slot.gameDate || "",
        });
      }
      const row = map.get(key);
      row.count += 1;
      row.scoreTotal += Number(slot.score || 0);
      row.scoreMax = Math.max(row.scoreMax, Number(slot.score || 0));
      if (!row.firstDate || (slot.gameDate && slot.gameDate < row.firstDate)) row.firstDate = slot.gameDate;
      row.startTime = slot.startTime || row.startTime;
      row.endTime = slot.endTime || row.endTime;
      row.slotType = slot.slotType || row.slotType;
      row.priorityRank = slot.priorityRank || row.priorityRank;
    }
    return Array.from(map.values())
      .map((row) => ({
        ...row,
        score: Math.round(row.scoreTotal / Math.max(1, row.count)),
      }))
      .sort((a, b) => {
        const day = dayOrderIndex(a.weekday) - dayOrderIndex(b.weekday);
        if (day !== 0) return day;
        const start = (a.startTime || "").localeCompare(b.startTime || "");
        if (start !== 0) return start;
        return (a.fieldKey || "").localeCompare(b.fieldKey || "");
      });
  }, [slotPlan]);

  const effectiveGameSlotMinutes = useMemo(() => {
    const selectedDivision = divisions.find(
      (item) => (item?.code || item?.division || "") === division
    );
    const divisionMinutes = Number(
      selectedDivision?.season?.gameLengthMinutes ??
        selectedDivision?.gameLengthMinutes ??
        selectedDivision?.seasonGameLengthMinutes ??
        0
    );
    if (Number.isFinite(divisionMinutes) && divisionMinutes > 0) return Math.trunc(divisionMinutes);
    const leagueMinutes = Number(leagueSeasonConfig?.gameLengthMinutes ?? 0);
    if (Number.isFinite(leagueMinutes) && leagueMinutes > 0) return Math.trunc(leagueMinutes);
    return 120;
  }, [division, divisions, leagueSeasonConfig]);

  const planningIntel = useMemo(() => {
    const teams = Number.isFinite(teamCount) ? teamCount : 0;
    const minRegularGames = Math.max(0, Number(minGamesPerTeam) || 0);
    const poolGamesTarget = Math.max(2, Number(poolGamesPerTeam) || 2);
    const maxGamesLimit = Number(maxGamesPerWeek);
    const teamRuleGamesCapPerWeek =
      Number.isFinite(maxGamesLimit) && maxGamesLimit > 0 && teams >= 2
        ? Math.floor((teams * maxGamesLimit) / 2)
        : null;

    const hasSeasonRange = isIsoDate(seasonStart) && isIsoDate(seasonEnd) && seasonStart <= seasonEnd;
    const hasPoolRange = isIsoDate(poolStart) && isIsoDate(poolEnd) && poolStart <= poolEnd;
    const hasBracketRange = isIsoDate(bracketStart) && isIsoDate(bracketEnd) && bracketStart <= bracketEnd;
    const regularRangeEnd = hasPoolRange ? addIsoDays(poolStart, -1) : seasonEnd;
    const hasRegularRange = hasSeasonRange && isIsoDate(regularRangeEnd) && seasonStart <= regularRangeEnd;
    const blockedDateSet = new Set(parsedNoGamesOnDates.values);
    const noGamesBeforeMin = normalizeClockInput(noGamesBeforeTime) ? parseMinutes(normalizeClockInput(noGamesBeforeTime)) : null;
    const noGamesAfterMin = normalizeClockInput(noGamesAfterTime) ? parseMinutes(normalizeClockInput(noGamesAfterTime)) : null;

    const isBlocked = (gameDate) =>
      activeBlockedRanges.some((range) => isIsoDateInRange(gameDate, range.startDate, range.endDate));

    const violatesLeagueRuleWindow = (slot) => {
      if (!slot) return false;
      if (blockedDateSet.has(String(slot.gameDate || "").trim())) return true;
      const startMin = parseMinutes(slot.startTime);
      const endMin = parseMinutes(slot.endTime);
      if (noGamesBeforeMin != null && startMin != null && startMin < noGamesBeforeMin) return true;
      if (noGamesAfterMin != null && endMin != null && endMin > noGamesAfterMin) return true;
      return false;
    };

    const datedSlots = (slotPlan || []).filter((slot) => isIsoDate(slot?.gameDate));
    const gameOnlySlots = datedSlots.filter((slot) => slot?.slotType === "game");
    const bothSlots = datedSlots.filter((slot) => slot?.slotType === "both");
    const practiceOnlySlots = datedSlots.filter((slot) => slot?.slotType === "practice");

    const gameCapableSlots = [...gameOnlySlots, ...bothSlots];
    const availableAllSlots = datedSlots.filter((slot) => !isBlocked(slot.gameDate) && !violatesLeagueRuleWindow(slot));
    const availableGameCapableSlots = gameCapableSlots.filter((slot) => !isBlocked(slot.gameDate) && !violatesLeagueRuleWindow(slot));
    const practiceSlotsAvailable = practiceOnlySlots.filter((slot) => !isBlocked(slot.gameDate) && !violatesLeagueRuleWindow(slot));
    const blockedOutSlots = Math.max(0, datedSlots.length - availableAllSlots.length);

    const regularGameSlots = hasRegularRange
      ? availableGameCapableSlots.filter((slot) => isIsoDateInRange(slot.gameDate, seasonStart, regularRangeEnd))
      : [];
    const poolSlotsAllTypes = hasPoolRange
      ? availableAllSlots.filter((slot) => isIsoDateInRange(slot.gameDate, poolStart, poolEnd))
      : [];
    const bracketSlotsAllTypes = hasBracketRange
      ? availableAllSlots.filter((slot) => isIsoDateInRange(slot.gameDate, bracketStart, bracketEnd))
      : [];

    const regularSlotsAvailable = regularGameSlots.length;
    const poolSlotsAvailable = poolSlotsAllTypes.length;
    const bracketSlotsAvailable = bracketSlotsAllTypes.length;
    const poolPracticeFallbackSlots = poolSlotsAllTypes.filter((slot) => slot?.slotType === "practice").length;
    const bracketPracticeFallbackSlots = bracketSlotsAllTypes.filter((slot) => slot?.slotType === "practice").length;

    const regularWeekKeys = hasRegularRange ? buildIsoWeekKeys(seasonStart, regularRangeEnd) : [];
    const regularWeeklyCounts = new Map(regularWeekKeys.map((key) => [key, 0]));
    regularGameSlots.forEach((slot) => {
      const weekKey = weekStartIso(slot.gameDate);
      if (!weekKey) return;
      regularWeeklyCounts.set(weekKey, (regularWeeklyCounts.get(weekKey) || 0) + 1);
    });
    const regularWeeklyValues = Array.from(regularWeeklyCounts.values());
    const regularWeeksCount = regularWeeklyValues.length;
    const avgGameSlotsPerWeek = regularWeeksCount ? regularSlotsAvailable / regularWeeksCount : 0;
    const maxGameSlotsPerWeek = regularWeeksCount ? Math.max(...regularWeeklyValues) : 0;
    const minGameSlotsPerWeek = regularWeeksCount ? Math.min(...regularWeeklyValues) : 0;
    const maxGamesSupportedPerWeek =
      teamRuleGamesCapPerWeek == null ? maxGameSlotsPerWeek : Math.min(maxGameSlotsPerWeek, teamRuleGamesCapPerWeek);

    const roundRobinMatchups = estimateRoundRobinMatchups(teams);
    const gamesPerTeamRound = Math.max(1, teams - 1);
    const roundRobinRounds = roundRobinMatchups > 0 ? Math.ceil(minRegularGames / gamesPerTeamRound) : 0;
    const regularRequiredCycleSlots = roundRobinMatchups * roundRobinRounds;
    const regularRequiredMinimum = teams >= 2 ? Math.ceil((teams * minRegularGames) / 2) : 0;
    const poolRequiredSlots = hasPoolRange && teams >= 2 ? Math.ceil((teams * poolGamesTarget) / 2) : 0;
    const bracketRequiredSlots = hasBracketRange ? 3 : 0;

    const totalGameCapableSlotsAvailable = availableGameCapableSlots.length;
    const totalPhaseSlotsAvailable = regularSlotsAvailable + poolSlotsAvailable + bracketSlotsAvailable;
    const totalRequiredSlotsMinimum = regularRequiredMinimum + poolRequiredSlots + bracketRequiredSlots;
    const totalRequiredSlotsCycle = regularRequiredCycleSlots + poolRequiredSlots + bracketRequiredSlots;

    return {
      teams,
      minRegularGames,
      poolGamesTarget,
      totalGameOnlySlots: gameOnlySlots.length,
      totalBothSlots: bothSlots.length,
      totalPracticeOnlySlots: practiceOnlySlots.length,
      totalGameCapableSlotsAvailable,
      totalPhaseSlotsAvailable,
      totalPracticeSlotsAvailable: practiceSlotsAvailable.length,
      blockedOutSlots,
      regularSlotsAvailable,
      poolSlotsAvailable,
      bracketSlotsAvailable,
      poolPracticeFallbackSlots,
      bracketPracticeFallbackSlots,
      regularWeeksCount,
      avgGameSlotsPerWeek,
      maxGameSlotsPerWeek,
      minGameSlotsPerWeek,
      teamRuleGamesCapPerWeek,
      maxGamesSupportedPerWeek,
      regularRequiredCycleSlots,
      roundRobinMatchups,
      roundRobinRounds,
      gamesPerTeamRound,
      regularRequiredMinimum,
      poolRequiredSlots,
      bracketRequiredSlots,
      totalRequiredSlotsMinimum,
      totalRequiredSlotsCycle,
      regularShortfall: Math.max(0, regularRequiredMinimum - regularSlotsAvailable),
      regularCycleShortfall: Math.max(0, regularRequiredCycleSlots - regularSlotsAvailable),
      poolShortfall: Math.max(0, poolRequiredSlots - poolSlotsAvailable),
      bracketShortfall: Math.max(0, bracketRequiredSlots - bracketSlotsAvailable),
      totalShortfall: Math.max(0, totalRequiredSlotsMinimum - totalPhaseSlotsAvailable),
      totalCycleShortfall: Math.max(0, totalRequiredSlotsCycle - totalPhaseSlotsAvailable),
    };
  }, [
    activeBlockedRanges,
    bracketEnd,
    bracketStart,
    maxGamesPerWeek,
    minGamesPerTeam,
    noGamesAfterTime,
    noGamesBeforeTime,
    parsedNoGamesOnDates,
    poolEnd,
    poolGamesPerTeam,
    poolStart,
    seasonEnd,
    seasonStart,
    slotPlan,
    teamCount,
  ]);

  const guestAnchorOptions = useMemo(
    () =>
      slotPatterns
        .filter((p) => p.slotType === "game" || p.slotType === "both")
        .map((p) => ({
          slotId: p.key,
          label: `${p.weekday} ${p.startTime}-${p.endTime} ${p.fieldKey} (score ${p.score}, ${p.count} opening${p.count === 1 ? "" : "s"})`,
        })),
    [slotPatterns]
  );

  function updatePatternPlan(patternKey, patch) {
    setSlotPlan((prev) =>
      prev.map((item) => (item.basePatternKey === patternKey ? { ...item, ...patch } : item))
    );
    setPreview(null);
  }

  function updatePatternPlanWithLaneShift(patternKey, patch) {
    const representative = slotPatterns.find((pattern) => pattern.key === patternKey);
    if (!representative) {
      setErr("");
      updatePatternPlan(patternKey, patch);
      return { shiftedPatternCount: 0 };
    }

    const nextEndTime = patch.endTime != null ? patch.endTime : representative.endTime;
    const priorEndMinutes = parseMinutes(representative.endTime);
    const nextEndMinutes = parseMinutes(nextEndTime);
    const deltaMinutes =
      priorEndMinutes != null && nextEndMinutes != null ? nextEndMinutes - priorEndMinutes : 0;
    const shiftedTimingByPattern = new Map();

    if (deltaMinutes !== 0) {
      const lanePatterns = slotPatterns
        .filter(
          (pattern) =>
            pattern.weekday === representative.weekday &&
            String(pattern.fieldKey || "") === String(representative.fieldKey || "")
        )
        .sort((left, right) => {
          const start = String(left.startTime || "").localeCompare(String(right.startTime || ""));
          if (start !== 0) return start;
          const end = String(left.endTime || "").localeCompare(String(right.endTime || ""));
          if (end !== 0) return end;
          return String(left.key || "").localeCompare(String(right.key || ""));
        });
      const patternIndex = lanePatterns.findIndex((pattern) => pattern.key === patternKey);

      if (patternIndex >= 0) {
        for (let index = patternIndex + 1; index < lanePatterns.length; index += 1) {
          const lanePattern = lanePatterns[index];
          const currentStartMinutes = parseMinutes(lanePattern.startTime);
          const currentEndMinutes = parseMinutes(lanePattern.endTime);
          if (currentStartMinutes == null || currentEndMinutes == null) {
            setErr(
              `Could not shift later slots for ${representative.weekday} ${representative.fieldKey}: one of the following slots has an invalid time.`
            );
            return null;
          }
          const shiftedStartTime = formatMinutesAsTime(currentStartMinutes + deltaMinutes);
          const shiftedEndTime = formatMinutesAsTime(currentEndMinutes + deltaMinutes);
          if (!shiftedStartTime || !shiftedEndTime) {
            setErr(
              `Updating ${representative.weekday} ${representative.fieldKey} would push later slots past midnight.`
            );
            return null;
          }
          shiftedTimingByPattern.set(lanePattern.key, {
            startTime: shiftedStartTime,
            endTime: shiftedEndTime,
          });
        }
      }
    }

    setErr("");
    setSlotPlan((prev) =>
      prev.map((item) => {
        if (item.basePatternKey === patternKey) {
          return { ...item, ...patch };
        }
        const shiftedPatch = shiftedTimingByPattern.get(item.basePatternKey);
        return shiftedPatch ? { ...item, ...shiftedPatch } : item;
      })
    );
    setPreview(null);
    return { shiftedPatternCount: shiftedTimingByPattern.size };
  }

  function updatePatternSlotType(patternKey, currentPriorityRank, nextTypeRaw) {
    const nextType = normalizeSlotType(nextTypeRaw);
    const representative = slotPatterns.find((p) => p.key === patternKey);

    if (!representative) {
      updatePatternPlan(patternKey, {
        slotType: nextType,
        priorityRank: nextType === "practice" ? "" : normalizePriorityRank(currentPriorityRank),
      });
      return;
    }

    // Auto-refactor duration when changing slot type
    const startMin = parseMinutes(representative.startTime);
    if (startMin != null) {
      let targetDuration;
      if (nextType === "practice") {
        targetDuration = 90; // Practice slots: 90 minutes
      } else if (nextType === "both" || nextType === "game") {
        targetDuration = 120; // Both/Game slots: 120 minutes
      }

      if (targetDuration) {
        const newEndTime = formatMinutesAsTime(startMin + targetDuration);
        if (newEndTime) {
          const updateResult = updatePatternPlanWithLaneShift(patternKey, {
            slotType: nextType,
            endTime: newEndTime,
            priorityRank: nextType === "practice" ? "" : normalizePriorityRank(currentPriorityRank),
          });
          if (!updateResult) {
            return;
          }
          setToast({
            tone: "success",
            duration: 2500,
            message: `${representative.weekday} ${representative.fieldKey}: set to ${nextType.toUpperCase()} (${targetDuration}m). Updated ${representative.count || 1} slot(s)${updateResult.shiftedPatternCount ? ` and shifted ${updateResult.shiftedPatternCount} later pattern(s).` : "."}`,
          });
          return;
        }
      }
    }

    // Fallback if duration calculation failed
    updatePatternPlan(patternKey, {
      slotType: nextType,
      priorityRank: nextType === "practice" ? "" : normalizePriorityRank(currentPriorityRank),
    });
  }

  function updatePatternStartTime(patternKey, nextStart) {
    const representative = slotPatterns.find((p) => p.key === patternKey);
    if (!representative) return;
    const start = String(nextStart || "").trim();
    if (!start) return;
    const startMin = parseMinutes(start);
    const endMin = parseMinutes(representative.endTime);
    if (startMin == null || endMin == null) {
      setErr("Start time must be in HH:mm format.");
      return;
    }
    if (startMin >= endMin) {
      setErr(`Start time must be earlier than ${representative.endTime} for ${representative.weekday} ${representative.fieldKey}.`);
      return;
    }
    setErr("");
    updatePatternPlan(patternKey, { startTime: start });
  }

  function updatePatternEndTime(patternKey, nextEnd) {
    const representative = slotPatterns.find((p) => p.key === patternKey);
    if (!representative) return;
    const end = String(nextEnd || "").trim();
    if (!end) return;
    const startMin = parseMinutes(representative.startTime);
    const endMin = parseMinutes(end);
    if (startMin == null || endMin == null) {
      setErr("End time must be in HH:mm format.");
      return;
    }
    if (endMin <= startMin) {
      setErr(`End time must be later than ${representative.startTime} for ${representative.weekday} ${representative.fieldKey}.`);
      return;
    }
    updatePatternPlanWithLaneShift(patternKey, { endTime: end });
  }

  function updatePatternDurationMinutes(patternKey, nextDurationRaw) {
    const representative = slotPatterns.find((p) => p.key === patternKey);
    if (!representative) return;
    const duration = Math.trunc(Number(nextDurationRaw));
    if (!Number.isFinite(duration) || duration <= 0) {
      setErr("Duration must be a positive number of minutes.");
      return;
    }
    const startMin = parseMinutes(representative.startTime);
    if (startMin == null) {
      setErr("Start time must be valid before changing duration.");
      return;
    }
    const end = formatMinutesAsTime(startMin + duration);
    if (!end) {
      setErr(`Duration pushes end time past midnight for ${representative.weekday} ${representative.fieldKey}.`);
      return;
    }
    updatePatternPlanWithLaneShift(patternKey, { endTime: end });
  }

  function quickConvertPattern(patternKey, nextTypeRaw, durationMinutes) {
    const representative = slotPatterns.find((p) => p.key === patternKey);
    if (!representative) return;
    const startMin = parseMinutes(representative.startTime);
    if (startMin == null) {
      setErr("Start time must be valid before converting slot pattern.");
      return;
    }
    const endTime = formatMinutesAsTime(startMin + Number(durationMinutes || 0));
    if (!endTime) {
      setErr(`Could not convert ${representative.weekday} ${representative.fieldKey}: invalid duration.`);
      return;
    }
    const nextType = normalizeSlotType(nextTypeRaw);
    const priorType = normalizeSlotType(representative.slotType);
    const priorEndTime = representative.endTime || "";
    const priorPriority = representative.priorityRank || "";
    const nextPriority = nextType === "practice" ? "" : normalizePriorityRank(representative.priorityRank);
    const updateResult = updatePatternPlanWithLaneShift(patternKey, {
      slotType: nextType,
      priorityRank: nextPriority,
      endTime,
    });
    if (!updateResult) {
      return;
    }
    const changed =
      priorType !== nextType ||
      priorEndTime !== endTime ||
      String(priorPriority || "") !== String(nextPriority || "");
    setToast({
      tone: changed ? "success" : "info",
      duration: 2800,
      message: changed
        ? `${representative.weekday} ${representative.fieldKey}: set to ${nextType.toUpperCase()} (${Number(durationMinutes || 0)}m). Updated ${representative.count || 1} opening(s)${updateResult.shiftedPatternCount ? ` and shifted ${updateResult.shiftedPatternCount} later pattern(s).` : "."}`
        : `${representative.weekday} ${representative.fieldKey} is already ${nextType.toUpperCase()} at ${Number(durationMinutes || 0)}m.`,
    });
  }

  function setAllSlotTypes(nextType) {
    const normalized = normalizeSlotType(nextType);
    setSlotPlan((prev) => prev.map((item) => ({ ...item, slotType: normalized })));
    setPreview(null);
  }

  function setAllSlotTypesWithRefactor(nextTypeRaw, durationMinutes) {
    const nextType = normalizeSlotType(nextTypeRaw);
    const duration = Math.trunc(Number(durationMinutes));
    if (!Number.isFinite(duration) || duration <= 0) {
      setErr("Refactor duration must be a positive number of minutes.");
      return;
    }

    let invalidCount = 0;
    let changedCount = 0;
    const changedPatterns = new Set();
    setSlotPlan((prev) => {
      let localInvalidCount = 0;
      let localChangedCount = 0;
      const localChangedPatterns = new Set();
      const next = prev.map((item) => {
        const startMin = parseMinutes(item.startTime);
        const refactoredEnd = startMin == null ? "" : formatMinutesAsTime(startMin + duration);
        if (!refactoredEnd) localInvalidCount += 1;
        const nextPriority = nextType === "practice" ? "" : normalizePriorityRank(item.priorityRank);
        const nextEndTime = refactoredEnd || item.endTime;
        const didChange =
          normalizeSlotType(item.slotType) !== nextType ||
          String(item.endTime || "") !== String(nextEndTime || "") ||
          String(item.priorityRank || "") !== String(nextPriority || "");
        if (didChange) {
          localChangedCount += 1;
          if (item.basePatternKey) localChangedPatterns.add(item.basePatternKey);
        }
        return {
          ...item,
          slotType: nextType,
          priorityRank: nextPriority,
          endTime: nextEndTime,
        };
      });
      invalidCount = localInvalidCount;
      changedCount = localChangedCount;
      changedPatterns.clear();
      localChangedPatterns.forEach((key) => changedPatterns.add(key));
      return next;
    });
    setPreview(null);
    setErr(
      invalidCount
        ? `Refactored ${nextType} slots, but ${invalidCount} slot(s) kept their prior end time because the new duration would exceed midnight.`
        : ""
    );
    setToast({
      tone: changedCount > 0 ? "success" : "info",
      duration: 3200,
      message:
        changedCount > 0
          ? `Set all slots to ${nextType.toUpperCase()} + refactor (${duration}m). Updated ${changedCount} opening(s) across ${changedPatterns.size} pattern(s).`
          : `No changes: slots were already ${nextType.toUpperCase()} with ${duration}m timing.`,
    });
  }

  function autoRankGameSlots() {
    const rankedPatterns = slotPatterns
      .filter((p) => p.slotType === "game" || p.slotType === "both")
      .sort((a, b) => {
        const score = (b.score || 0) - (a.score || 0);
        if (score !== 0) return score;
        const day = dayOrderIndex(a.weekday) - dayOrderIndex(b.weekday);
        if (day !== 0) return day;
        return (a.startTime || "").localeCompare(b.startTime || "");
      });
    const rankByPattern = new Map();
    rankedPatterns.forEach((p, idx) => rankByPattern.set(p.key, String(idx + 1)));
    setSlotPlan((prev) =>
      prev.map((item) => {
        if (item.slotType === "game" || item.slotType === "both") {
          return { ...item, priorityRank: rankByPattern.get(item.basePatternKey) || "" };
        }
        return { ...item, priorityRank: "" };
      })
    );
    setPreview(null);
  }

  function saveSlotPlanTemplate() {
    const templateName = window.prompt("Enter a name for this slot plan template:");
    if (!templateName || !templateName.trim()) return;

    const template = {
      name: templateName.trim(),
      slotTypes: slotPlan.map((slot) => ({
        basePatternKey: slot.basePatternKey,
        slotType: slot.slotType,
        priorityRank: slot.priorityRank,
      })),
      savedAt: new Date().toISOString(),
    };

    try {
      const key = `slotPlanTemplates_${leagueId}`;
      const existing = JSON.parse(localStorage.getItem(key) || "[]");
      existing.push(template);
      localStorage.setItem(key, JSON.stringify(existing));
      setToast({
        tone: "success",
        message: `Saved template "${templateName}"`,
      });
    } catch (e) {
      setErr("Failed to save template. LocalStorage may be full.");
    }
  }

  function loadSlotPlanTemplate(template) {
    if (!template || !template.slotTypes) return;

    const typesByPattern = new Map();
    template.slotTypes.forEach((t) => {
      typesByPattern.set(t.basePatternKey, { slotType: t.slotType, priorityRank: t.priorityRank });
    });

    setSlotPlan((prev) =>
      prev.map((slot) => {
        const match = typesByPattern.get(slot.basePatternKey);
        if (match) {
          return { ...slot, slotType: match.slotType, priorityRank: match.priorityRank || "" };
        }
        return slot;
      })
    );

    setPreview(null);
    setToast({
      tone: "success",
      message: `Loaded template "${template.name}"`,
    });
  }

  function getSlotPlanTemplates() {
    try {
      const key = `slotPlanTemplates_${leagueId}`;
      return JSON.parse(localStorage.getItem(key) || "[]");
    } catch {
      return [];
    }
  }

  function deleteSlotPlanTemplate(templateName) {
    try {
      const key = `slotPlanTemplates_${leagueId}`;
      const existing = JSON.parse(localStorage.getItem(key) || "[]");
      const filtered = existing.filter((t) => t.name !== templateName);
      localStorage.setItem(key, JSON.stringify(filtered));
      setToast({
        tone: "info",
        message: `Deleted template "${templateName}"`,
      });
    } catch (e) {
      setErr("Failed to delete template.");
    }
  }

  useEffect(() => {
    if (!division) return;
    setSlotPlan([]);
    setGuestAnchorPrimarySlotId("");
    setGuestAnchorSecondarySlotId("");
    setRivalryMatchups([]);
  }, [division]);

  useEffect(() => {
    if (!leagueId || !division) {
      setTeamCount(0);
      setDivisionTeams([]);
      return;
    }
    (async () => {
      try {
        const qs = new URLSearchParams();
        qs.set("division", division);
        const data = await apiFetch(`/api/teams?${qs.toString()}`);
        const list = Array.isArray(data) ? data : [];
        const normalizedTeams = list
          .map((team) => ({
            teamId: String(team?.teamId || "").trim(),
            name: String(team?.name || "").trim(),
          }))
          .filter((team) => team.teamId)
          .sort((a, b) => (a.name || a.teamId).localeCompare(b.name || b.teamId));
        setDivisionTeams(normalizedTeams);
        setTeamCount(list.length);
      } catch {
        setTeamCount(0);
        setDivisionTeams([]);
      }
    })();
  }, [leagueId, division]);

  const applyAvailabilityPayload = useCallback((payload) => {
    const availability = Array.isArray(payload?.availability) ? payload.availability : [];
    const dayCounts = new Map();
    const patternCounts = new Map();
    for (const slot of availability) {
      const weekday = isoDayShort(slot.gameDate || "");
      if (weekday) {
        dayCounts.set(weekday, (dayCounts.get(weekday) || 0) + 1);
      }
      const patternKey = `${weekday}|${slot.startTime || ""}|${slot.endTime || ""}|${slot.fieldKey || ""}`;
      patternCounts.set(patternKey, (patternCounts.get(patternKey) || 0) + 1);
    }

    let nextSlotPlan = [];
    setSlotPlan((prev) => {
      const previousById = new Map((prev || []).map((item) => [item.slotId, item]));
      nextSlotPlan = availability.map((slot) => {
        const prior = previousById.get(slot.slotId);
        const weekday = isoDayShort(slot.gameDate || "");
        const baseStartTime = slot.startTime || "";
        const baseEndTime = slot.endTime || "";
        const basePatternKey = patternKeyFromParts(weekday, baseStartTime, baseEndTime, slot.fieldKey || "");
        const nextStartTime = prior?.startTime || baseStartTime;
        const nextEndTime = prior?.endTime || baseEndTime;
        const allocationSlotType = normalizeSlotType(slot.allocationSlotType || "game");
        const allocationPriority = normalizePriorityRank(slot.allocationPriorityRank ?? "");
        const baselinePriority = allocationSlotType === "practice" ? "" : allocationPriority;
        const nextSlotType = normalizeSlotType(prior?.slotType || allocationSlotType);
        const nextPriority = normalizePriorityRank(prior?.priorityRank || baselinePriority);
        return {
          slotId: slot.slotId,
          gameDate: slot.gameDate || "",
          startTime: nextStartTime,
          endTime: nextEndTime,
          fieldKey: slot.fieldKey || "",
          weekday,
          baseStartTime,
          baseEndTime,
          basePatternKey,
          slotType: nextSlotType,
          priorityRank: nextSlotType === "practice" ? "" : nextPriority,
          score: computeSlotScore(slot, weekday, patternCounts, dayCounts),
        };
      });
      return nextSlotPlan;
    });

    return nextSlotPlan;
  }, []);

  const loadAvailabilityIntoSlotPlan = useCallback(async ({ forceRefresh = false } = {}) => {
    if (!leagueId || !division || !seasonStart || !seasonEnd) return null;

    const requestId = availabilityRequestIdRef.current + 1;
    availabilityRequestIdRef.current = requestId;
    const planningDateTo = maxIsoDate(seasonEnd, bracketEnd) || seasonEnd;
    const cacheKey = [leagueId, division, seasonStart, seasonEnd, planningDateTo].join("|");

    setAvailabilityErr("");
    setAvailabilityLoading(true);
    try {
      if (!forceRefresh) {
        const cachedPayload = availabilityCacheRef.current.get(cacheKey);
        if (cachedPayload) {
          if (requestId !== availabilityRequestIdRef.current) return null;
          return applyAvailabilityPayload(cachedPayload);
        }
      }

      const list = [];
      const seenSlotIds = new Set();
      let continuationToken = "";
      for (let page = 0; page < 50; page += 1) {
        const qs = new URLSearchParams();
        qs.set("division", division);
        qs.set("dateFrom", seasonStart);
        qs.set("dateTo", planningDateTo);
        qs.set("status", "Open");
        qs.set("pageSize", "500");
        if (continuationToken) qs.set("continuationToken", continuationToken);

        const data = await apiFetch(`/api/slots?${qs.toString()}`);
        const pageItems = extractSlotItems(data);
        for (const slot of pageItems) {
          const slotId = String(slot?.slotId || "").trim();
          if (!slotId || seenSlotIds.has(slotId)) continue;
          seenSlotIds.add(slotId);
          list.push(slot);
        }

        const nextToken = extractContinuationToken(data);
        if (!nextToken || pageItems.length === 0) break;
        continuationToken = nextToken;
      }

      const availability = list
        .filter((s) => s.isAvailability)
        .sort((a, b) => {
          const ad = `${a.gameDate || ""}|${a.startTime || ""}|${a.fieldKey || ""}|${a.slotId || ""}`;
          const bd = `${b.gameDate || ""}|${b.startTime || ""}|${b.fieldKey || ""}|${b.slotId || ""}`;
          return ad.localeCompare(bd);
        });

      const payload = { availability };
      availabilityCacheRef.current.set(cacheKey, payload);
      if (requestId !== availabilityRequestIdRef.current) return null;
      return applyAvailabilityPayload(payload);
    } catch (e) {
      if (requestId !== availabilityRequestIdRef.current) return null;
      setAvailabilityErr(normalizeRequestErrorMessage(e, "Failed to load availability slots."));
      setSlotPlan([]);
      setGuestAnchorPrimarySlotId("");
      setGuestAnchorSecondarySlotId("");
      return null;
    } finally {
      if (requestId === availabilityRequestIdRef.current) {
        setAvailabilityLoading(false);
      }
    }
  }, [applyAvailabilityPayload, bracketEnd, division, leagueId, seasonEnd, seasonStart]);

  async function resetGeneratedSlotsForRerun() {
    if (!resetGeneratedSlotsBeforeApply) return undefined;

    const resetPayload = {
      division,
      seasonStart,
      seasonEnd,
      bracketEnd: bracketEnd || undefined,
    };
    const result = await apiFetch("/api/schedule/wizard/reset-generated", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(resetPayload),
    });

    availabilityCacheRef.current.clear();
    const refreshedSlotPlan = await loadAvailabilityIntoSlotPlan({ forceRefresh: true });
    if (!Array.isArray(refreshedSlotPlan)) {
      throw new Error("Reset completed, but the refreshed availability could not be loaded. Try again.");
    }
    setPreview(null);
    setToast({
      tone: "success",
      message: `Reset ${Number(result?.resetCount || 0)} existing non-practice game/guest slot${Number(result?.resetCount || 0) === 1 ? "" : "s"} and reloaded availability.`,
    });
    return refreshedSlotPlan;
  }

  useEffect(() => {
    loadAvailabilityIntoSlotPlan();
    return () => {
      availabilityRequestIdRef.current += 1;
    };
  }, [loadAvailabilityIntoSlotPlan]);

  useEffect(() => {
    const allowed = new Set(
      slotPatterns
        .filter((s) => s.slotType === "game" || s.slotType === "both")
        .map((s) => s.key)
    );
    if (guestAnchorPrimarySlotId && !allowed.has(guestAnchorPrimarySlotId)) {
      setGuestAnchorPrimarySlotId("");
    }
    if (guestAnchorSecondarySlotId && !allowed.has(guestAnchorSecondarySlotId)) {
      setGuestAnchorSecondarySlotId("");
    }
  }, [slotPatterns, guestAnchorPrimarySlotId, guestAnchorSecondarySlotId]);

  // Feasibility check with debouncing (triggered when rule inputs change on the Rules step)
  useEffect(() => {
    if (step !== 3) return; // Only run on the Rules step.
    if (!division || !seasonStart || !seasonEnd) return;
    if (slotPlan.length === 0) return;

    const timer = setTimeout(() => {
      fetchFeasibility();
    }, 500);

    return () => clearTimeout(timer);
    // fetchFeasibility intentionally omitted to debounce on concrete rule inputs.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    step,
    division,
    seasonStart,
    seasonEnd,
    minGamesPerTeam,
    poolGamesPerTeam,
    maxGamesPerWeek,
    noDoubleHeaders,
    guestGamesPerWeek,
    slotPlan.length,
  ]);

  // Auto-fill logic: apply recommended values when feasibility loads for the first time
  useEffect(() => {
    if (!feasibility || hasAutoApplied) return;
    if (minGamesPerTeam > 0) return; // Only auto-fill if unset

    // Apply recommendations
    setMinGamesPerTeam(feasibility.recommendations.minGamesPerTeam);
    setGuestGamesPerWeek(feasibility.recommendations.optimalGuestGamesPerWeek);
    setHasAutoApplied(true);
  }, [feasibility, hasAutoApplied, minGamesPerTeam]);

  function applyRulePreset(presetId) {
    const recommendedMin = Math.max(1, Number(feasibility?.recommendations?.minGamesPerTeam || minGamesPerTeam || 1));
    const recommendedMax = Math.max(
      recommendedMin,
      Number(feasibility?.recommendations?.maxGamesPerTeam || recommendedMin)
    );
    const recommendedGuestGames = Math.max(
      0,
      Number(feasibility?.recommendations?.optimalGuestGamesPerWeek || guestGamesPerWeek || 0)
    );

    if (presetId === "balanced") {
      setMinGamesPerTeam(recommendedMin);
      setGuestGamesPerWeek(recommendedGuestGames);
      setMaxGamesPerWeek(Math.max(1, Math.min(2, recommendedMax)));
      setNoDoubleHeaders(true);
      setBalanceHomeAway(true);
    } else if (presetId === "max_games") {
      setMinGamesPerTeam(recommendedMax);
      setGuestGamesPerWeek(Math.max(recommendedGuestGames, 1));
      setMaxGamesPerWeek(Math.max(2, Number(maxGamesPerWeek) || 0));
      setNoDoubleHeaders(false);
      setBalanceHomeAway(false);
    } else if (presetId === "conservative") {
      setMinGamesPerTeam(Math.max(1, recommendedMin - 1));
      setGuestGamesPerWeek(0);
      setMaxGamesPerWeek(1);
      setNoDoubleHeaders(true);
      setBalanceHomeAway(true);
    } else {
      return;
    }

    setPreview(null);
    setToast({
      tone: "success",
      duration: 2600,
      message: `Applied the ${RULE_PRESETS.find((preset) => preset.id === presetId)?.label || "selected"} rule preset.`,
    });
  }

  function guestAnchorPayloadFromSlotId(slotId) {
    if (!slotId) return null;
    const slot = slotPatterns.find((s) => s.key === slotId);
    if (!slot) return null;
    return {
      dayOfWeek: slot.weekday,
      startTime: slot.startTime,
      endTime: slot.endTime,
      fieldKey: slot.fieldKey,
    };
  }

  function addRivalryMatchupRow() {
    setRivalryMatchups((prev) => {
      const list = Array.isArray(prev) ? prev : [];
      if (list.length >= MAX_RIVALRY_MATCHUPS) return list;
      return [...list, { teamA: "", teamB: "", weight: 3 }];
    });
    setPreview(null);
  }

  function updateRivalryMatchupRow(index, patch) {
    setRivalryMatchups((prev) =>
      (Array.isArray(prev) ? prev : []).map((row, idx) => (idx === index ? { ...row, ...patch } : row))
    );
    setPreview(null);
  }

  function removeRivalryMatchupRow(index) {
    setRivalryMatchups((prev) => (Array.isArray(prev) ? prev : []).filter((_, idx) => idx !== index));
    setPreview(null);
  }

  function suggestRivalryMatchups() {
    const teamIds = normalizedDivisionTeams.map((team) => team.teamId).filter(Boolean);
    if (teamIds.length < 2) {
      setErr("Need at least two teams in the selected division to suggest priority matchups.");
      return;
    }

    const targetGames = Math.max(0, Number(minGamesPerTeam) || 0);
    if (targetGames <= 0) {
      setErr("Set Min games per team above 0 before auto-suggesting priority matchups.");
      return;
    }

    const demandOnly = suggestPriorityMatchupsFromDemand(teamIds, targetGames, MAX_RIVALRY_MATCHUPS);
    const suggestions = suggestPriorityMatchupsComposite(teamIds, targetGames, MAX_RIVALRY_MATCHUPS);
    if (!suggestions.length) {
      setToast({
        tone: "info",
        message: "No priority matchup suggestions were found from repeated demand or team-order proximity. Add matchups manually if you still want late-season bias.",
      });
      return;
    }

    if (rivalryMatchups.length > 0) {
      const confirmed = window.confirm("Replace current priority matchups with suggested repeated pairs?");
      if (!confirmed) return;
    }

    setRivalryMatchups(suggestions.map((row) => ({ teamA: row.teamA, teamB: row.teamB, weight: row.weight })));
    setPreview(null);
    setErr("");
    const demandCount = demandOnly.length;
    const fallbackCount = Math.max(0, suggestions.length - demandCount);
    setToast({
      tone: "success",
      message: `Suggested ${suggestions.length} priority matchup${suggestions.length === 1 ? "" : "s"} (${demandCount} from repeated pair demand${fallbackCount ? `, ${fallbackCount} from nearby-team pairing` : ""}).`,
    });
  }

  function buildWizardPayload(slotPlanOverride) {
    const sourceSlotPlan = Array.isArray(slotPlanOverride) ? slotPlanOverride : slotPlan;
    const slotPlanPayload = sourceSlotPlan.map((slot) => {
      const rank = Number(slot.priorityRank);
      return {
        slotId: slot.slotId,
        slotType: normalizeSlotType(slot.slotType),
        priorityRank: Number.isFinite(rank) && rank > 0 ? rank : undefined,
        startTime: slot.startTime || undefined,
        endTime: slot.endTime || undefined,
      };
    });
    const blockedDateRanges = activeBlockedRanges;
    const noGamesOnDates = parsedNoGamesOnDates.values;
    const normalizedNoGamesBeforeTime = normalizeClockInput(noGamesBeforeTime);
    const normalizedNoGamesAfterTime = normalizeClockInput(noGamesAfterTime);

    const payload = {
      division,
      seasonStart,
      seasonEnd,
      poolStart: poolStart || undefined,
      poolEnd: poolEnd || undefined,
      bracketStart: bracketStart || undefined,
      bracketEnd: bracketEnd || undefined,
      minGamesPerTeam: Number(minGamesPerTeam) || 0,
      poolGamesPerTeam: Math.max(2, Number(poolGamesPerTeam) || 2),
      externalOfferPerWeek: Number(guestGamesPerWeek) || 0,
      maxExternalOffersPerTeamSeason: Number(maxExternalOffersPerTeamSeason) || 0,
      resetGeneratedSlotsBeforeApply,
      maxGamesPerWeek: Number(maxGamesPerWeek) || 0,
      noDoubleHeaders,
      balanceHomeAway,
      slotPlan: slotPlanPayload,
    };
    if (blockedDateRanges.length) payload.blockedDateRanges = blockedDateRanges;
    if (noGamesOnDates.length) payload.noGamesOnDates = noGamesOnDates;
    if (normalizedNoGamesBeforeTime) payload.noGamesBeforeTime = normalizedNoGamesBeforeTime;
    if (normalizedNoGamesAfterTime) payload.noGamesAfterTime = normalizedNoGamesAfterTime;
    if (rivalryPayload.length) payload.rivalryMatchups = rivalryPayload;

    const primaryAnchor = guestAnchorPayloadFromSlotId(guestAnchorPrimarySlotId);
    const secondaryAnchor = guestAnchorPayloadFromSlotId(guestAnchorSecondarySlotId);
    if (primaryAnchor) payload.guestAnchorPrimary = primaryAnchor;
    if (secondaryAnchor) payload.guestAnchorSecondary = secondaryAnchor;
    return payload;
  }

  async function fetchFeasibility() {
    if (!division || !seasonStart || !seasonEnd) return;
    if (slotPlan.length === 0) return;

    if (err === "Failed to fetch" || err.startsWith("Preview request failed.") || err.startsWith("Repair request failed.")) {
      setErr("");
    }
    setFeasibilityLoading(true);
    try {
      const payload = buildWizardPayload();
      const data = await apiFetch("/api/schedule/wizard/feasibility", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setFeasibility(data || null);
    } catch (e) {
      console.error("Feasibility check failed:", e);
      setFeasibility(null);
    } finally {
      setFeasibilityLoading(false);
    }
  }

  async function runPreview() {
    setErr("");
    const dateError = validateIsoDates([
      { label: "Season start", value: seasonStart, required: true },
      { label: "Season end", value: seasonEnd, required: true },
      { label: "Pool start", value: poolStart, required: false },
      { label: "Pool end", value: poolEnd, required: false },
      { label: "Bracket start", value: bracketStart, required: false },
      { label: "Bracket end", value: bracketEnd, required: false },
    ]);
    if (dateError) return setErr(dateError);
    if (!division) return setErr("Division is required.");
    if (slotPlanSummary.gameCapable <= 0) {
      return setErr("Select at least one availability slot as Game or Both in Slot plan.");
    }
    if (guestAnchorPrimarySlotId && guestAnchorSecondarySlotId && guestAnchorPrimarySlotId === guestAnchorSecondarySlotId) {
      return setErr("Guest anchor option 1 and option 2 must be different slots.");
    }
    if (rivalryRowIssues.length > 0) {
      return setErr(rivalryRowIssues[0]);
    }
    if (leagueRuleIssues.length > 0) {
      return setErr(leagueRuleIssues[0]);
    }
    setLoading(true);
    try {
      const refreshedSlotPlan = await resetGeneratedSlotsForRerun();
      const payload = buildWizardPayload(refreshedSlotPlan);
      const data = await apiFetch("/api/schedule/wizard/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setPreview(data || null);
      setStep(4);
      trackEvent("ui_season_wizard_preview", { leagueId, division });
    } catch (e) {
      setErr(normalizeRequestErrorMessage(e, "Preview request failed."));
      setPreview(null);
    } finally {
      setLoading(false);
    }
  }

  async function applySchedule() {
    if (!preview) return;

    // Warn user that this will overwrite existing schedule
    const confirmed = window.confirm(
      "⚠️ WARNING: Applying this schedule will OVERWRITE all existing game assignments in this division.\n\n" +
      "This action:\n" +
      "• Replaces all current slot assignments\n" +
      (resetGeneratedSlotsBeforeApply
        ? "• Resets existing non-practice game and guest slots in this season window before previewing and applying\n"
        : "• Leaves existing non-practice game and guest slots untouched before previewing and applying\n") +
      "• Does NOT remove recurring allocations or field blackouts\n" +
      "• Cannot be undone\n" +
      "• May still require allocation cleanup if you need a different underlying slot pool\n\n" +
      "Are you sure you want to continue?"
    );

    if (!confirmed) return;

    setErr("");
    const dateError = validateIsoDates([
      { label: "Season start", value: seasonStart, required: true },
      { label: "Season end", value: seasonEnd, required: true },
      { label: "Pool start", value: poolStart, required: false },
      { label: "Pool end", value: poolEnd, required: false },
      { label: "Bracket start", value: bracketStart, required: false },
      { label: "Bracket end", value: bracketEnd, required: false },
    ]);
    if (dateError) return setErr(dateError);
    if (slotPlanSummary.gameCapable <= 0) {
      return setErr("Select at least one availability slot as Game or Both in Slot plan.");
    }
    if (guestAnchorPrimarySlotId && guestAnchorSecondarySlotId && guestAnchorPrimarySlotId === guestAnchorSecondarySlotId) {
      return setErr("Guest anchor option 1 and option 2 must be different slots.");
    }
    if (rivalryRowIssues.length > 0) {
      return setErr(rivalryRowIssues[0]);
    }
    if (leagueRuleIssues.length > 0) {
      return setErr(leagueRuleIssues[0]);
    }
    setLoading(true);
    try {
      const refreshedSlotPlan = await resetGeneratedSlotsForRerun();
      const payload = buildWizardPayload(refreshedSlotPlan);
      await apiFetch("/api/schedule/wizard/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setToast({ tone: "success", message: "Wizard schedule applied." });
      trackEvent("ui_season_wizard_apply", { leagueId, division });
    } catch (e) {
      setErr(normalizeRequestErrorMessage(e, "Apply request failed."));
    } finally {
      setLoading(false);
    }
  }

  async function applyPreviewRepairProposal(proposal) {
    if (!preview || !proposal || proposal.requiresUserAction) return;
    const changes = Array.isArray(proposal.changes) ? proposal.changes : [];
    const hasMove = changes.some((c) => String(c?.changeType || "").toLowerCase() === "move");
    if (!hasMove) return;

    setErr("");
    setRepairApplyingId(String(proposal.proposalId || "repair"));
    try {
      const payload = {
        wizard: buildWizardPayload(),
        preview,
        proposal,
      };
      const data = await apiFetch("/api/schedule/wizard/repair/apply-preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setPreview(data || null);
      setToast({ tone: "success", message: "Preview repair applied and revalidated." });
    } catch (e) {
      setErr(normalizeRequestErrorMessage(e, "Repair request failed."));
    } finally {
      setRepairApplyingId("");
    }
  }

  function canDragSwapAssignment(assignment) {
    if (!assignment || repairApplyingId) return false;
    if (String(assignment.phase || "") !== "Regular Season") return false;
    if (assignment.isExternalOffer) return false;
    if (!String(assignment.slotId || "").trim()) return false;
    if (!String(assignment.homeTeamId || "").trim() || !String(assignment.awayTeamId || "").trim()) return false;
    return true;
  }

  function clearAssignmentDragSwap() {
    setDragSwapSourceKey("");
    setDragSwapTargetKey("");
  }

  function handleAssignmentDragStart(event, assignment) {
    if (!canDragSwapAssignment(assignment)) return;
    if (event?.dataTransfer) {
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", assignmentExplainKey(assignment));
    }
    setDragSwapSourceKey(assignmentExplainKey(assignment));
    setDragSwapTargetKey("");
  }

  function handleAssignmentDragOver(event, assignment) {
    if (!canDragSwapAssignment(assignment) || !dragSwapSourceKey) return;
    const targetKey = assignmentExplainKey(assignment);
    if (targetKey === dragSwapSourceKey) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "move";
    setDragSwapTargetKey(targetKey);
  }

  async function handleAssignmentDrop(event, targetAssignment) {
    event.preventDefault();
    try {
      if (!preview || !dragSwapSourceKey) return;
      if (!canDragSwapAssignment(targetAssignment)) return;
      const targetKey = assignmentExplainKey(targetAssignment);
      if (!targetKey || targetKey === dragSwapSourceKey) return;

      const sourceAssignment = (preview.assignments || []).find((row) => assignmentExplainKey(row) === dragSwapSourceKey);
      if (!canDragSwapAssignment(sourceAssignment)) {
        setErr("Drag source is no longer valid. Reload preview.");
        return;
      }
      if (sourceAssignment?.isExternalOffer || targetAssignment?.isExternalOffer) {
        setErr("Guest/external slots are locked in preview and cannot be swapped.");
        return;
      }
      const proposal = buildPreviewSwapRepairProposal(sourceAssignment, targetAssignment);
      if (!proposal) {
        setErr("Only regular-season assigned games can be swapped.");
        return;
      }
      await applyPreviewRepairProposal(proposal);
    } finally {
      clearAssignmentDragSwap();
    }
  }

  function getIssuePhase(issue) {
    const value = issue?.phase || issue?.details?.phase || "";
    const phase = String(value).trim();
    return phase || "Regular Season";
  }

  function buildGuestRotationDetail(guestCounts, totalGuestAssignments) {
    const ordered = [...guestCounts.entries()]
      .sort((a, b) => {
        const diff = (b[1] || 0) - (a[1] || 0);
        if (diff !== 0) return diff;
        return String(a[0] || "").localeCompare(String(b[0] || ""));
      })
      .map(([teamId, count]) => `${teamId}:${count}`);
    return `Guest assignments ${totalGuestAssignments}. Distribution: ${ordered.join(", ")}`;
  }

  function getPrimaryIssueDetail(issue) {
    const primary = issue?.details?.primaryViolation;
    if (primary && typeof primary === "object" && !Array.isArray(primary)) return primary;
    if (issue?.details && typeof issue.details === "object" && !Array.isArray(issue.details)) return issue.details;
    return {};
  }

  function buildIssueHint(issue, summary) {
    if (!issue) return "";
    const base = ISSUE_HINTS[issue.ruleId] || "";
    const issuePhase = getIssuePhase(issue);
    const primaryDetail = getPrimaryIssueDetail(issue);
    const sampleTeamId = String(primaryDetail?.teamId || "").trim();
    const sampleGameDate = String(primaryDetail?.gameDate || "").trim();
    const sampleCollision =
      sampleTeamId || sampleGameDate
        ? ` Example: ${sampleTeamId || "A team"}${sampleGameDate ? ` already has a game on ${sampleGameDate}` : " already has a game that day"}.`
        : "";
    if (!summary) return base;
    if (issue.ruleId === "unassigned-matchups") {
      const phase =
        issuePhase === "Pool Play"
          ? summary.poolPlay || {}
          : issuePhase === "Bracket"
            ? summary.bracket || {}
            : summary.regularSeason || {};
      if (phase.matchupsTotal > phase.slotsTotal) {
        return `${base} ${issuePhase} has fewer slots (${phase.slotsTotal}) than matchups (${phase.matchupsTotal}).`;
      }
      if (issuePhase === "Bracket") {
        return `${base} Bracket finals must be placed after semifinal end times, so add a later championship slot if needed.`;
      }
    }
    if (issue.ruleId === "double-header") {
      if (summary.teamCount && summary.teamCount % 2 === 1) {
        return `With an odd team count (${summary.teamCount}), two guest slots/week plus Max games/week at 2 should normally absorb the idle team. A remaining double-header usually means a guest slot still shares a date with an existing game, or one of the exact guest anchor weeks is missing.${sampleCollision}`;
      }
      return `${base}${sampleCollision}`;
    }
    if (issue.ruleId === "double-header-balance") {
      const max = Number(issue?.details?.maxDoubleHeaders ?? issue?.details?.max ?? NaN);
      const min = Number(issue?.details?.minDoubleHeaders ?? issue?.details?.min ?? NaN);
      if (Number.isFinite(max) && Number.isFinite(min)) {
        return `${base} Current spread is max ${max} vs min ${min} doubleheaders.`;
      }
    }
    if (issue.ruleId === "home-away-balance") {
      const offenders = Array.isArray(primaryDetail?.offenders) ? primaryDetail.offenders : [];
      const worst = offenders[0];
      const gap = Number(worst?.gap ?? NaN);
      const teamId = String(worst?.teamId || "").trim();
      if (teamId && Number.isFinite(gap)) {
        return `${base} Worst gap: ${teamId} is off by ${gap} home/away result${gap === 1 ? "" : "s"}.`;
      }
    }
    if (issue.ruleId === "idle-gap-balance") {
      const offenders = Array.isArray(primaryDetail?.offenders) ? primaryDetail.offenders : [];
      const worst = offenders[0];
      const extraGapWeeks = Number(worst?.extraGapWeeks ?? NaN);
      const teamId = String(worst?.teamId || "").trim();
      if (teamId && Number.isFinite(extraGapWeeks)) {
        return `${base} Worst idle stretch: ${teamId} has ${extraGapWeeks} extra idle week${extraGapWeeks === 1 ? "" : "s"} beyond the normal cadence.`;
      }
    }
    if (issue.ruleId === "opponent-repeat-balance") {
      const pairs = Array.isArray(primaryDetail?.pairs) ? primaryDetail.pairs : [];
      const worst = pairs[0];
      const pairKey = String(worst?.pairKey || "").trim();
      const pairCount = Number(worst?.count ?? NaN);
      if (pairKey && Number.isFinite(pairCount)) {
        const [teamA, teamB] = pairKey.split("|");
        if (teamA && teamB) {
          return `${base} Most repeated pairing is ${teamA} vs ${teamB} (${pairCount} time${pairCount === 1 ? "" : "s"}).`;
        }
      }
    }
    if (issue.ruleId === "unused-game-capacity") {
      const unusedCount = Number(primaryDetail?.count ?? NaN);
      if (Number.isFinite(unusedCount)) {
        const isBackwardLoaded = String(preview?.constructionStrategy || "")
          .toLowerCase()
          .startsWith("backward");
        if (isBackwardLoaded) {
          return `${base} ${unusedCount} slot${unusedCount === 1 ? "" : "s"} stayed open. Backward loading intentionally pushes regular games later in the season, so earlier openings often remain unused unless you need more league games.`;
        }
        return `${base} ${unusedCount} slot${unusedCount === 1 ? "" : "s"} stayed open and can absorb extra league games or guest use.`;
      }
    }
    return base;
  }

  function buildContextNotes(summary, issues) {
    if (!summary) return [];
    const notes = [];
    const regular = summary.regularSeason || {};
    const pool = summary.poolPlay || {};
    const bracket = summary.bracket || {};

    if (regular.slotsTotal < regular.matchupsTotal) {
      notes.push(`Regular season has ${regular.slotsTotal} slots for ${regular.matchupsTotal} matchups.`);
    }
    if (pool.matchupsTotal > 0 && pool.slotsTotal < pool.matchupsTotal) {
      notes.push(`Pool play has ${pool.slotsTotal} slots for ${pool.matchupsTotal} matchups.`);
    }
    if (bracket.matchupsTotal > 0 && bracket.slotsTotal < bracket.matchupsTotal) {
      notes.push(`Bracket has ${bracket.slotsTotal} slots for ${bracket.matchupsTotal} matchups.`);
    }
    if (summary.teamCount % 2 === 1) {
      notes.push(`Odd team count (${summary.teamCount}) creates one idle team each round; two guest slots/week plus Max games/week at 2 lets guest games absorb that instead of forcing BYEs.`);
    }
    if ((issues || []).some((i) => i.ruleId === "double-header")) {
      if (summary.teamCount % 2 === 1) {
        notes.push("With odd-team guest capacity reserved first, a remaining double-header usually points to a same-day guest collision or a missing exact guest anchor week, not just a lack of total slots.");
      } else {
        notes.push("Doubleheaders indicate tight slot density or too few usable dates.");
      }
    }
    if ((issues || []).some((i) => i.ruleId === "double-header-balance")) {
      notes.push("Doubleheaders are allowed, but current assignment is uneven across teams.");
    }
    if ((issues || []).some((i) => i.ruleId === "max-games-per-week")) {
      notes.push("Max games/week is a hard limit and is restricting assignments; add slots or widen date range.");
      if (summary.teamCount % 2 === 1) {
        notes.push("If Max games/week stays below 2, guest slots cannot clear weekly BYEs for an odd-team division.");
      }
    }
    if ((issues || []).some((i) => i.ruleId === "missing-opponent")) {
      notes.push("Guest games or external offers may be enabled; missing opponents are expected there.");
    }
    if (
      (issues || []).some((i) => i.ruleId === "unused-game-capacity") &&
      String(preview?.constructionStrategy || "").toLowerCase().startsWith("backward")
    ) {
      notes.push("Backward loading intentionally concentrates regular games later in the season, so earlier game-capable slots may remain open.");
    }
    return notes;
  }

  const basicsError = useMemo(() => {
    if (!division) return "Division is required.";
    if (!isIsoDate(seasonStart) || !isIsoDate(seasonEnd)) return "Season start/end must be YYYY-MM-DD.";
    if (seasonStart > seasonEnd) return "Season start must be before season end.";
    return "";
  }, [division, seasonStart, seasonEnd]);

  const postseasonError = useMemo(() => {
    if (!poolStart && !poolEnd && !bracketStart && !bracketEnd) return "";
    if ((poolStart && !poolEnd) || (!poolStart && poolEnd)) return "Pool play start and end must both be set.";
    if ((bracketStart && !bracketEnd) || (!bracketStart && bracketEnd)) return "Championship start and end must both be set.";
    if (poolStart && (!isIsoDate(poolStart) || !isIsoDate(poolEnd))) return "Pool play dates must be YYYY-MM-DD.";
    if (bracketStart && (!isIsoDate(bracketStart) || !isIsoDate(bracketEnd))) return "Championship dates must be YYYY-MM-DD.";
    if (poolStart && poolEnd && poolStart > poolEnd) return "Pool play start must be before pool play end.";
    if (bracketStart && bracketEnd && bracketStart > bracketEnd) return "Championship start must be before championship end.";
    if (poolStart && poolEnd && (poolStart < seasonStart || poolEnd > seasonEnd)) return "Pool play must stay within the season range.";
    if (bracketStart && bracketStart < seasonStart) return "Championship must start on or after season start.";
    return "";
  }, [poolStart, poolEnd, bracketStart, bracketEnd, seasonStart, seasonEnd]);

  const slotPlanError = useMemo(() => {
    if (availabilityErr) return availabilityErr;
    if (availabilityLoading) return "";
    if (!slotPlan.length) return "No availability slots loaded.";
    const invalidTime = slotPatterns.find((p) => {
      const startMin = parseMinutes(p.startTime);
      const endMin = parseMinutes(p.endTime);
      return startMin == null || endMin == null || startMin >= endMin;
    });
    if (invalidTime) return `Invalid time window for ${invalidTime.weekday} ${invalidTime.fieldKey}.`;
    if (slotPlanSummary.gameCapable <= 0) return "Select at least one pattern as Game or Both.";
    if (guestAnchorPrimarySlotId && guestAnchorSecondarySlotId && guestAnchorPrimarySlotId === guestAnchorSecondarySlotId) {
      return "Guest anchor option 1 and option 2 must be different.";
    }
    return "";
  }, [
    availabilityErr,
    availabilityLoading,
    slotPlan,
    slotPatterns,
    slotPlanSummary.gameCapable,
    guestAnchorPrimarySlotId,
    guestAnchorSecondarySlotId,
  ]);

  const rulesError = useMemo(() => {
    const maxGames = Number(maxGamesPerWeek);
    const minGames = Number(minGamesPerTeam);
    const poolGames = Number(poolGamesPerTeam);
    const guestGames = Number(guestGamesPerWeek);
    const externalCap = Number(maxExternalOffersPerTeamSeason);
    if (!Number.isFinite(maxGames) || maxGames < 0) return "Max games/week must be 0 or greater.";
    if (!Number.isFinite(minGames) || minGames < 0) return "Min games/team must be 0 or greater.";
    if (!Number.isFinite(poolGames) || poolGames < 2) return "Pool games/team must be 2 or greater.";
    if (!Number.isFinite(guestGames) || guestGames < 0) return "Guest games/week must be 0 or greater.";
    if (!Number.isFinite(externalCap) || externalCap < 0) return "Max guest/crossover offers per team must be 0 or greater.";
    if (leagueRuleIssues.length > 0) return leagueRuleIssues[0];
    if (rivalryRowIssues.length > 0) return rivalryRowIssues[0];
    return "";
  }, [maxGamesPerWeek, minGamesPerTeam, poolGamesPerTeam, guestGamesPerWeek, maxExternalOffersPerTeamSeason, leagueRuleIssues, rivalryRowIssues]);

  const previewError = useMemo(() => {
    if (!preview) return "";
    if ((preview.totalIssues || 0) > 0) return `${preview.totalIssues} validation issue(s) in preview.`;
    return "";
  }, [preview]);

  const previewRuleHealth = preview && typeof preview.ruleHealth === "object" ? preview.ruleHealth : null;
  const previewApplyBlocked = !!preview?.applyBlocked;
  const previewRepairProposals = useMemo(
    () => (Array.isArray(preview?.repairProposals) ? preview.repairProposals : []),
    [preview]
  );
  const previewExplainMap = preview?.explanations && typeof preview.explanations === "object"
    ? preview.explanations
    : null;
  const previewCollections = useMemo(() => {
    const assignments = Array.isArray(preview?.assignments) ? preview.assignments : [];
    const unassignedSlots = Array.isArray(preview?.unassignedSlots) ? preview.unassignedSlots : [];
    const unassignedMatchups = Array.isArray(preview?.unassignedMatchups) ? preview.unassignedMatchups : [];
    const issues = Array.isArray(preview?.issues) ? preview.issues : [];
    const warnings = Array.isArray(preview?.warnings) ? preview.warnings : [];
    const regularAssignments = assignments.filter((assignment) => assignment?.phase === "Regular Season");
    return {
      assignments,
      unassignedSlots,
      unassignedMatchups,
      issues,
      warnings,
      regularAssignments,
      regularScheduledAssignments: regularAssignments.filter((assignment) => !assignment?.isExternalOffer),
      regularGuestAssignments: regularAssignments.filter((assignment) => assignment?.isExternalOffer && assignment?.homeTeamId),
      regularUnassignedSlots: unassignedSlots.filter((slot) => slot?.phase === "Regular Season"),
      regularUnassignedMatchups: unassignedMatchups.filter((matchup) => getIssuePhase(matchup) === "Regular Season"),
    };
  }, [preview]);
  const previewAssignmentCount = previewCollections.assignments.length;
  const previewWarningCount = previewCollections.warnings.length;
  const previewIssueCount = Number(preview?.totalIssues || 0) > 0
    ? Number(preview?.totalIssues || 0)
    : previewCollections.issues.length;
  const stepErrors = useMemo(
    () => [basicsError, postseasonError, slotPlanError, rulesError, previewError],
    [basicsError, postseasonError, slotPlanError, rulesError, previewError]
  );
  const forwardStepErrors = useMemo(
    () => [basicsError, postseasonError, slotPlanError, rulesError],
    [basicsError, postseasonError, slotPlanError, rulesError]
  );
  const firstBlockedForwardStep = useMemo(
    () => forwardStepErrors.findIndex(Boolean),
    [forwardStepErrors]
  );
  const furthestUnlockedStep = useMemo(() => {
    if (firstBlockedForwardStep >= 0) return firstBlockedForwardStep;
    return preview ? 4 : 3;
  }, [firstBlockedForwardStep, preview]);
  const forwardBlockMessage = useMemo(() => {
    if (firstBlockedForwardStep >= 0) {
      return `${steps[firstBlockedForwardStep].label}: ${forwardStepErrors[firstBlockedForwardStep]}`;
    }
    if (!preview) return 'Run "Preview schedule" to unlock Preview.';
    return "";
  }, [firstBlockedForwardStep, forwardStepErrors, preview, steps]);

  useEffect(() => {
    if (!preview) {
      setSelectedExplainGameKey("");
      setSelectedRepairProposalId("");
      setSelectedRuleFocusKey("");
      clearAssignmentDragSwap();
      return;
    }
    const keys = new Set(
      previewCollections.assignments.map((assignment) => assignmentExplainKey(assignment))
    );
    if (!selectedExplainGameKey) return;
    if (!keys.has(selectedExplainGameKey)) {
      setSelectedExplainGameKey("");
    }
    if (dragSwapSourceKey && !keys.has(dragSwapSourceKey)) {
      clearAssignmentDragSwap();
    }
  }, [preview, previewCollections.assignments, selectedExplainGameKey, dragSwapSourceKey]);

  useEffect(() => {
    if (!previewRepairProposals.length) {
      if (selectedRepairProposalId) setSelectedRepairProposalId("");
      return;
    }
    if (!selectedRepairProposalId) return;
    const exists = previewRepairProposals.some((proposal) => String(proposal?.proposalId || "") === selectedRepairProposalId);
    if (!exists) {
      setSelectedRepairProposalId("");
    }
  }, [previewRepairProposals, selectedRepairProposalId]);

  const selectedRepairScope = useMemo(() => {
    if (!selectedRepairProposalId || !previewRepairProposals.length) return null;
    const proposal = previewRepairProposals.find((item) => String(item?.proposalId || "") === selectedRepairProposalId);
    return buildRepairProposalScope(proposal);
  }, [previewRepairProposals, selectedRepairProposalId]);

  const selectedRepairLookup = useMemo(() => {
    const scope = selectedRepairScope;
    if (!scope) return null;
    return {
      proposalId: scope.proposalId,
      title: scope.title,
      rationale: scope.rationale,
      slotIds: new Set(scope.slotIds || []),
      teamIds: new Set(scope.teamIds || []),
      weekKeys: new Set(scope.weekKeys || []),
      fieldKeys: new Set(scope.fieldKeys || []),
      ruleIds: new Set(scope.ruleIds || []),
      scope,
    };
  }, [selectedRepairScope]);

  useEffect(() => {
    const groups = Array.isArray(previewRuleHealth?.groups) ? previewRuleHealth.groups : [];
    if (!groups.length) {
      if (selectedRuleFocusKey) setSelectedRuleFocusKey("");
      return;
    }
    if (!selectedRuleFocusKey) return;
    const exists = groups.some((group) => `${String(group?.severity || "").toLowerCase()}:${String(group?.ruleId || "")}` === selectedRuleFocusKey);
    if (!exists) setSelectedRuleFocusKey("");
  }, [previewRuleHealth, selectedRuleFocusKey]);

  const selectedRuleFocusScope = useMemo(() => {
    const groups = Array.isArray(previewRuleHealth?.groups) ? previewRuleHealth.groups : [];
    if (!selectedRuleFocusKey || !groups.length) return null;
    const group = groups.find((item) => `${String(item?.severity || "").toLowerCase()}:${String(item?.ruleId || "")}` === selectedRuleFocusKey);
    return buildRuleGroupFocusScope(group);
  }, [previewRuleHealth, selectedRuleFocusKey]);

  const selectedRuleLookup = useMemo(() => {
    if (!selectedRuleFocusScope || !preview) return null;
    const slotIds = new Set(selectedRuleFocusScope.slotIds || []);
    const teamIds = new Set(selectedRuleFocusScope.teamIds || []);
    const weekKeys = new Set(selectedRuleFocusScope.weekKeys || []);
    const fieldKeys = new Set(selectedRuleFocusScope.fieldKeys || []);
    const slotRows = [
      ...previewCollections.assignments,
      ...previewCollections.unassignedSlots,
    ];
    slotRows.forEach((row) => {
      const slotId = String(row?.slotId || "").trim();
      if (!slotId || !slotIds.has(slotId)) return;
      const fieldKey = String(row?.fieldKey || "").trim();
      if (fieldKey) fieldKeys.add(fieldKey);
      const weekKey = weekStartIso(row?.gameDate);
      if (weekKey) weekKeys.add(weekKey);
    });
    return {
      proposalId: "",
      title: selectedRuleFocusScope.ruleId,
      rationale: selectedRuleFocusScope.summary,
      slotIds,
      teamIds,
      weekKeys,
      fieldKeys,
      ruleIds: new Set(selectedRuleFocusScope.ruleIds || []),
      scope: selectedRuleFocusScope,
      source: "rule-health",
    };
  }, [preview, previewCollections.assignments, previewCollections.unassignedSlots, selectedRuleFocusScope]);

  const activeHighlightLookup = selectedRepairLookup || selectedRuleLookup || null;

  const isAssignmentHighlightedByRepair = (assignment) => {
    if (!activeHighlightLookup || !assignment) return false;
    const slotId = String(assignment?.slotId || "").trim();
    const homeTeamId = String(assignment?.homeTeamId || "").trim();
    const awayTeamId = String(assignment?.awayTeamId || "").trim();
    const weekKey = weekStartIso(assignment?.gameDate);
    const fieldKey = String(assignment?.fieldKey || "").trim();
    if (slotId && activeHighlightLookup.slotIds.has(slotId)) return true;
    if (weekKey && activeHighlightLookup.weekKeys.has(weekKey)) return true;
    if (fieldKey && activeHighlightLookup.fieldKeys.has(fieldKey) && weekKey && activeHighlightLookup.weekKeys.has(weekKey)) return true;
    if (homeTeamId && activeHighlightLookup.teamIds.has(homeTeamId)) return true;
    if (awayTeamId && activeHighlightLookup.teamIds.has(awayTeamId)) return true;
    return false;
  };

  const isWeekHighlightedByRepair = (weekKey) => !!(activeHighlightLookup && weekKey && activeHighlightLookup.weekKeys.has(String(weekKey).trim()));
  const isTeamWeekHighlightedByRepair = (teamId, weekKey) => !!(
    activeHighlightLookup &&
    teamId &&
    weekKey &&
    activeHighlightLookup.teamIds.has(String(teamId).trim()) &&
    activeHighlightLookup.weekKeys.has(String(weekKey).trim())
  );
  const isFieldWeekHighlightedByRepair = (fieldKey, weekKey) => !!(
    activeHighlightLookup &&
    fieldKey &&
    weekKey &&
    activeHighlightLookup.fieldKeys.has(String(fieldKey).trim()) &&
    activeHighlightLookup.weekKeys.has(String(weekKey).trim())
  );
  const assignmentPhaseOptions = useMemo(
    () =>
      [...new Set(
        previewCollections.assignments
          .map((assignment) => String(assignment?.phase || "").trim())
          .filter(Boolean)
      )].sort(),
    [previewCollections.assignments]
  );
  const assignmentTeamOptions = useMemo(() => {
    const teamIds = new Set();
    previewCollections.assignments.forEach((assignment) => {
      const homeTeamId = String(assignment?.homeTeamId || "").trim();
      const awayTeamId = String(assignment?.awayTeamId || "").trim();
      if (homeTeamId) teamIds.add(homeTeamId);
      if (awayTeamId) teamIds.add(awayTeamId);
    });
    return [...teamIds]
      .map((teamId) => ({
        teamId,
        label: teamNameById.get(teamId) || teamId,
      }))
      .sort((left, right) => left.label.localeCompare(right.label));
  }, [previewCollections.assignments, teamNameById]);
  const assignmentFieldOptions = useMemo(
    () =>
      [...new Set(
        previewCollections.assignments
          .map((assignment) => String(assignment?.fieldKey || "").trim())
          .filter(Boolean)
      )].sort(),
    [previewCollections.assignments]
  );

  useEffect(() => {
    if (assignmentPhaseFilter && !assignmentPhaseOptions.includes(assignmentPhaseFilter)) {
      setAssignmentPhaseFilter("");
    }
  }, [assignmentPhaseFilter, assignmentPhaseOptions]);

  useEffect(() => {
    if (assignmentTeamFilter && !assignmentTeamOptions.some((team) => team.teamId === assignmentTeamFilter)) {
      setAssignmentTeamFilter("");
    }
  }, [assignmentTeamFilter, assignmentTeamOptions]);

  useEffect(() => {
    if (assignmentFieldFilter && !assignmentFieldOptions.includes(assignmentFieldFilter)) {
      setAssignmentFieldFilter("");
    }
  }, [assignmentFieldFilter, assignmentFieldOptions]);

  useEffect(() => {
    if (!activeHighlightLookup && showHighlightedAssignmentsOnly) {
      setShowHighlightedAssignmentsOnly(false);
    }
  }, [activeHighlightLookup, showHighlightedAssignmentsOnly]);

  const filteredPreviewAssignments = useMemo(
    () =>
      previewCollections.assignments.filter((assignment) => {
        const phase = String(assignment?.phase || "").trim();
        const fieldKey = String(assignment?.fieldKey || "").trim();
        const homeTeamId = String(assignment?.homeTeamId || "").trim();
        const awayTeamId = String(assignment?.awayTeamId || "").trim();
        const slotId = String(assignment?.slotId || "").trim();
        const weekKey = weekStartIso(assignment?.gameDate);
        if (assignmentPhaseFilter && phase !== assignmentPhaseFilter) return false;
        if (assignmentTeamFilter && homeTeamId !== assignmentTeamFilter && awayTeamId !== assignmentTeamFilter) return false;
        if (assignmentFieldFilter && fieldKey !== assignmentFieldFilter) return false;
        if (showHighlightedAssignmentsOnly) {
          const isHighlighted = !!(
            activeHighlightLookup &&
            (
              (slotId && activeHighlightLookup.slotIds.has(slotId)) ||
              (weekKey && activeHighlightLookup.weekKeys.has(weekKey)) ||
              (fieldKey && weekKey && activeHighlightLookup.fieldKeys.has(fieldKey) && activeHighlightLookup.weekKeys.has(weekKey)) ||
              (homeTeamId && activeHighlightLookup.teamIds.has(homeTeamId)) ||
              (awayTeamId && activeHighlightLookup.teamIds.has(awayTeamId))
            )
          );
          if (!isHighlighted) return false;
        }
        return true;
      }),
    [
      previewCollections.assignments,
      assignmentPhaseFilter,
      assignmentTeamFilter,
      assignmentFieldFilter,
      showHighlightedAssignmentsOnly,
      activeHighlightLookup,
    ]
  );

  const unassignedRegularReport = useMemo(() => {
    if (!preview) {
      return {
        rows: [],
        matchupRows: [],
        totalMatchups: 0,
        openSlots: 0,
      };
    }

    const regularAssignments = previewCollections.regularScheduledAssignments;
    const regularUnassignedMatchups = previewCollections.regularUnassignedMatchups;
    const regularOpenSlots = previewCollections.regularUnassignedSlots;

    const assignedByTeam = new Map();
    const unassignedByTeam = new Map();
    const ensureTeam = (teamId) => {
      const normalized = String(teamId || "").trim();
      if (!normalized) return;
      if (!assignedByTeam.has(normalized)) assignedByTeam.set(normalized, 0);
      if (!unassignedByTeam.has(normalized)) unassignedByTeam.set(normalized, 0);
    };
    const addAssigned = (teamId) => {
      ensureTeam(teamId);
      const normalized = String(teamId || "").trim();
      if (!normalized) return;
      assignedByTeam.set(normalized, (assignedByTeam.get(normalized) || 0) + 1);
    };
    const addUnassigned = (teamId) => {
      ensureTeam(teamId);
      const normalized = String(teamId || "").trim();
      if (!normalized) return;
      unassignedByTeam.set(normalized, (unassignedByTeam.get(normalized) || 0) + 1);
    };

    regularAssignments.forEach((assignment) => {
      addAssigned(assignment?.homeTeamId);
      addAssigned(assignment?.awayTeamId);
    });
    regularUnassignedMatchups.forEach((matchup) => {
      addUnassigned(matchup?.homeTeamId);
      addUnassigned(matchup?.awayTeamId);
    });

    const teamIds = new Set([...assignedByTeam.keys(), ...unassignedByTeam.keys()]);
    const rows = [...teamIds]
      .map((teamId) => {
        const assigned = assignedByTeam.get(teamId) || 0;
        const unassigned = unassignedByTeam.get(teamId) || 0;
        const target = assigned + unassigned;
        const coveragePct = target > 0 ? Math.round((assigned / target) * 100) : 100;
        return { teamId, assigned, unassigned, target, coveragePct };
      })
      .sort((left, right) => {
        const missingDiff = right.unassigned - left.unassigned;
        if (missingDiff !== 0) return missingDiff;
        return left.teamId.localeCompare(right.teamId);
      });

    const matchupCounts = new Map();
    regularUnassignedMatchups.forEach((matchup) => {
      const home = String(matchup?.homeTeamId || "").trim();
      const away = String(matchup?.awayTeamId || "").trim();
      if (!home || !away) return;
      const orderedTeams = [home, away].sort((a, b) => a.localeCompare(b));
      const key = `${orderedTeams[0]}|${orderedTeams[1]}`;
      const existing = matchupCounts.get(key) || { homeTeamId: orderedTeams[0], awayTeamId: orderedTeams[1], count: 0 };
      existing.count += 1;
      matchupCounts.set(key, existing);
    });

    const matchupRows = [...matchupCounts.values()].sort((left, right) => {
      const countDiff = right.count - left.count;
      if (countDiff !== 0) return countDiff;
      const homeDiff = left.homeTeamId.localeCompare(right.homeTeamId);
      if (homeDiff !== 0) return homeDiff;
      return left.awayTeamId.localeCompare(right.awayTeamId);
    });

    return {
      rows,
      matchupRows,
      totalMatchups: regularUnassignedMatchups.length,
      openSlots: regularOpenSlots.length,
    };
  }, [preview, previewCollections]);

  const regularBalanceReport = useMemo(() => {
    if (!preview) {
      return {
        teams: [],
        teamRows: [],
        matrixRows: [],
        pairRows: [],
        totalRegularGames: 0,
        totalGuestGames: 0,
        weekCount: 0,
        weekKeys: [],
      };
    }

    const regularAssignments = previewCollections.regularAssignments;
    const regularGames = previewCollections.regularScheduledAssignments.filter((a) => a?.homeTeamId && a?.awayTeamId);
    const regularGuestGames = previewCollections.regularGuestAssignments;
    const regularUnassignedSlots = previewCollections.regularUnassignedSlots;
    const regularUnassignedMatchups = previewCollections.regularUnassignedMatchups;
    const manualPriorityByPair = new Map(
      (rivalryPayload || [])
        .map((row) => {
          const key = normalizeTeamPairKey(row?.teamA, row?.teamB);
          const weight = Math.max(1, Math.min(10, Number(row?.weight) || 0));
          return key ? [key, weight] : null;
        })
        .filter(Boolean)
    );

    const teamIds = new Set();
    regularGames.forEach((a) => {
      if (a?.homeTeamId) teamIds.add(String(a.homeTeamId).trim());
      if (a?.awayTeamId) teamIds.add(String(a.awayTeamId).trim());
    });
    regularGuestGames.forEach((a) => {
      if (a?.homeTeamId) teamIds.add(String(a.homeTeamId).trim());
    });
    regularUnassignedMatchups.forEach((m) => {
      if (m?.homeTeamId) teamIds.add(String(m.homeTeamId).trim());
      if (m?.awayTeamId) teamIds.add(String(m.awayTeamId).trim());
    });

    const teams = [...teamIds].filter(Boolean).sort((a, b) => a.localeCompare(b));
    const teamStats = new Map(teams.map((teamId) => [teamId, {
      teamId,
      games: 0,
      home: 0,
      away: 0,
      guest: 0,
      activity: 0,
      unassigned: 0,
      target: 0,
      coveragePct: 100,
      homeAwayGap: 0,
      byeWeeks: 0,
    }]));
    const ensureTeamStat = (teamId) => {
      const normalized = String(teamId || "").trim();
      if (!normalized) return null;
      if (!teamStats.has(normalized)) {
        teamStats.set(normalized, {
          teamId: normalized,
          games: 0,
          home: 0,
          away: 0,
          guest: 0,
          activity: 0,
          unassigned: 0,
          target: 0,
          coveragePct: 100,
          homeAwayGap: 0,
          byeWeeks: 0,
        });
      }
      return teamStats.get(normalized);
    };

    const pairMap = new Map();
    const pairKey = (teamA, teamB) => [teamA, teamB].sort((a, b) => a.localeCompare(b)).join("|");
    const ensurePair = (leftRaw, rightRaw) => {
      const left = String(leftRaw || "").trim();
      const right = String(rightRaw || "").trim();
      if (!left || !right) return null;
      const [teamA, teamB] = [left, right].sort((a, b) => a.localeCompare(b));
      const key = `${teamA}|${teamB}`;
      if (!pairMap.has(key)) {
        pairMap.set(key, {
          key,
          teamA,
          teamB,
          assigned: 0,
          unassigned: 0,
          homeForA: 0,
          homeForB: 0,
        });
      }
      return pairMap.get(key);
    };

    const weeklyActivityByTeam = new Map();
    const addActivity = (teamId, gameDate) => {
      const normalized = String(teamId || "").trim();
      const weekKey = weekStartIso(gameDate);
      if (!normalized || !weekKey) return;
      let teamWeeks = weeklyActivityByTeam.get(normalized);
      if (!teamWeeks) {
        teamWeeks = new Map();
        weeklyActivityByTeam.set(normalized, teamWeeks);
      }
      teamWeeks.set(weekKey, (teamWeeks.get(weekKey) || 0) + 1);
    };

    regularGames.forEach((a) => {
      const home = String(a?.homeTeamId || "").trim();
      const away = String(a?.awayTeamId || "").trim();
      if (!home || !away) return;

      const pair = ensurePair(home, away);
      if (pair) {
        pair.assigned += 1;
        if (home === pair.teamA) pair.homeForA += 1;
        else pair.homeForB += 1;
      }

      const homeStat = ensureTeamStat(home);
      const awayStat = ensureTeamStat(away);
      if (homeStat) {
        homeStat.games += 1;
        homeStat.home += 1;
        homeStat.activity += 1;
      }
      if (awayStat) {
        awayStat.games += 1;
        awayStat.away += 1;
        awayStat.activity += 1;
      }
      addActivity(home, a?.gameDate);
      addActivity(away, a?.gameDate);
    });

    regularGuestGames.forEach((a) => {
      const teamId = String(a?.homeTeamId || "").trim();
      if (!teamId) return;
      const stat = ensureTeamStat(teamId);
      if (stat) {
        stat.guest += 1;
        stat.activity += 1;
      }
      addActivity(teamId, a?.gameDate);
    });

    regularUnassignedMatchups.forEach((m) => {
      const home = String(m?.homeTeamId || "").trim();
      const away = String(m?.awayTeamId || "").trim();
      if (!home || !away) return;
      const pair = ensurePair(home, away);
      if (pair) pair.unassigned += 1;
      const homeStat = ensureTeamStat(home);
      const awayStat = ensureTeamStat(away);
      if (homeStat) homeStat.unassigned += 1;
      if (awayStat) awayStat.unassigned += 1;
    });

    const regularWeekKeysFromSlots = new Set();
    [...regularAssignments, ...regularUnassignedSlots].forEach((slot) => {
      const key = weekStartIso(slot?.gameDate);
      if (key) regularWeekKeysFromSlots.add(key);
    });

    let regularWeekKeys = [...regularWeekKeysFromSlots].sort((a, b) => a.localeCompare(b));
    if (!regularWeekKeys.length) {
      const regularRangeEnd = poolStart ? addIsoDays(poolStart, -1) : seasonEnd;
      regularWeekKeys = buildIsoWeekKeys(seasonStart, regularRangeEnd);
    }

    const minGamesTarget = Math.max(0, Number(minGamesPerTeam) || 0);
    const teamRows = [...teamStats.values()]
      .map((row) => {
        const target = Math.max(minGamesTarget, row.games + row.unassigned);
        const coveragePct = target > 0 ? Math.round((row.games / target) * 100) : 100;
        const homeAwayGap = Math.abs(row.home - row.away);
        const teamWeeks = weeklyActivityByTeam.get(row.teamId) || new Map();
        const byeWeeks = regularWeekKeys.reduce((count, weekKey) => count + ((teamWeeks.get(weekKey) || 0) > 0 ? 0 : 1), 0);
        return {
          ...row,
          target,
          coveragePct,
          homeAwayGap,
          byeWeeks,
        };
      })
      .sort((a, b) => {
        const gameDiff = b.games - a.games;
        if (gameDiff !== 0) return gameDiff;
        return a.teamId.localeCompare(b.teamId);
      });

    const matrixRows = teams.map((rowTeamId) => ({
      teamId: rowTeamId,
      cells: teams.map((colTeamId) => {
        if (rowTeamId === colTeamId) return null;
        const record = pairMap.get(pairKey(rowTeamId, colTeamId));
        if (!record) {
          return { assigned: 0, target: 0, unassigned: 0, homeForRow: 0, awayForRow: 0 };
        }
        const rowIsA = rowTeamId === record.teamA;
        const homeForRow = rowIsA ? record.homeForA : record.homeForB;
        const awayForRow = record.assigned - homeForRow;
        const target = record.assigned + record.unassigned;
        const manualPriorityWeight = manualPriorityByPair.get(record.key) || 0;
        const autoRepeatPriority = target > 1;
        return {
          assigned: record.assigned,
          target,
          unassigned: record.unassigned,
          homeForRow,
          awayForRow,
          manualPriorityWeight,
          autoRepeatPriority,
        };
      }),
    }));

    const pairRows = [...pairMap.values()]
      .map((record) => {
        const target = record.assigned + record.unassigned;
        const manualPriorityWeight = manualPriorityByPair.get(record.key) || 0;
        const autoRepeatPriority = target > 1;
        let priorityLabel = "";
        if (manualPriorityWeight > 0 && autoRepeatPriority) priorityLabel = `Manual w${manualPriorityWeight} + Repeat`;
        else if (manualPriorityWeight > 0) priorityLabel = `Manual w${manualPriorityWeight}`;
        else if (autoRepeatPriority) priorityLabel = "Repeat";
        return {
          ...record,
          target,
          homeAwayGap: Math.abs(record.homeForA - record.homeForB),
          manualPriorityWeight,
          autoRepeatPriority,
          priorityLabel,
        };
      })
      .sort((a, b) => {
        const manualDiff = (b.manualPriorityWeight || 0) - (a.manualPriorityWeight || 0);
        if (manualDiff !== 0) return manualDiff;
        const repeatDiff = Number(b.autoRepeatPriority) - Number(a.autoRepeatPriority);
        if (repeatDiff !== 0) return repeatDiff;
        const targetDiff = b.target - a.target;
        if (targetDiff !== 0) return targetDiff;
        const assignedDiff = b.assigned - a.assigned;
        if (assignedDiff !== 0) return assignedDiff;
        const gapDiff = b.homeAwayGap - a.homeAwayGap;
        if (gapDiff !== 0) return gapDiff;
        return a.key.localeCompare(b.key);
      });

    return {
      teams,
      teamRows,
      matrixRows,
      pairRows,
      totalRegularGames: regularGames.length,
      totalGuestGames: regularGuestGames.length,
      weekCount: regularWeekKeys.length,
      weekKeys: regularWeekKeys,
      manualPriorityPairCount: manualPriorityByPair.size,
      autoRepeatPriorityPairCount: [...pairMap.values()].filter((p) => (p.assigned + p.unassigned) > 1).length,
    };
  }, [preview, previewCollections, seasonStart, seasonEnd, poolStart, minGamesPerTeam, rivalryPayload]);

  const teamLanesReport = useMemo(() => {
    if (!preview) {
      return { weekKeys: [], lanes: [], totalTeams: 0, shownTeams: 0 };
    }

    const regularAssignments = previewCollections.regularAssignments;
    const weekKeys = Array.isArray(regularBalanceReport.weekKeys) ? regularBalanceReport.weekKeys : [];
    const byTeamWeek = new Map();
    const byTeamWeekRegularAssignments = new Map();

    const ensureTeamWeek = (teamId, weekKey) => {
      const normalizedTeam = String(teamId || "").trim();
      const normalizedWeek = String(weekKey || "").trim();
      if (!normalizedTeam || !normalizedWeek) return null;
      let weekMap = byTeamWeek.get(normalizedTeam);
      if (!weekMap) {
        weekMap = new Map();
        byTeamWeek.set(normalizedTeam, weekMap);
      }
      if (!weekMap.has(normalizedWeek)) {
        weekMap.set(normalizedWeek, { regular: 0, guest: 0 });
      }
      return weekMap.get(normalizedWeek);
    };

    const addTeamWeekRegularAssignment = (teamId, weekKey, assignment) => {
      const normalizedTeam = String(teamId || "").trim();
      const normalizedWeek = String(weekKey || "").trim();
      if (!normalizedTeam || !normalizedWeek || !assignment) return;
      let weekMap = byTeamWeekRegularAssignments.get(normalizedTeam);
      if (!weekMap) {
        weekMap = new Map();
        byTeamWeekRegularAssignments.set(normalizedTeam, weekMap);
      }
      if (!weekMap.has(normalizedWeek)) {
        weekMap.set(normalizedWeek, []);
      }
      weekMap.get(normalizedWeek).push(assignment);
    };

    regularAssignments.forEach((a) => {
      const weekKey = weekStartIso(a?.gameDate);
      if (!weekKey) return;
      if (a?.isExternalOffer) {
        const home = String(a?.homeTeamId || "").trim();
        const cell = ensureTeamWeek(home, weekKey);
        if (cell) cell.guest += 1;
        return;
      }
      const home = String(a?.homeTeamId || "").trim();
      const away = String(a?.awayTeamId || "").trim();
      const homeCell = ensureTeamWeek(home, weekKey);
      const awayCell = ensureTeamWeek(away, weekKey);
      if (homeCell) homeCell.regular += 1;
      if (awayCell) awayCell.regular += 1;
      addTeamWeekRegularAssignment(home, weekKey, a);
      addTeamWeekRegularAssignment(away, weekKey, a);
    });

    const laneSource = Array.isArray(regularBalanceReport.teamRows) ? regularBalanceReport.teamRows : [];
    const lanes = laneSource
      .map((teamRow) => {
        const teamId = String(teamRow?.teamId || "").trim();
        const weekMap = byTeamWeek.get(teamId) || new Map();
        const weekAssignments = byTeamWeekRegularAssignments.get(teamId) || new Map();
        let zeroWeeks = 0;
        let singleWeeks = 0;
        let multiWeeks = 0;
        let currentIdle = 0;
        let longestIdleGap = 0;
        const cells = weekKeys.map((weekKey) => {
          const counts = weekMap.get(weekKey) || { regular: 0, guest: 0 };
          const total = Number(counts.regular || 0) + Number(counts.guest || 0);
          if (total <= 0) {
            zeroWeeks += 1;
            currentIdle += 1;
            longestIdleGap = Math.max(longestIdleGap, currentIdle);
          } else {
            currentIdle = 0;
            if (total === 1) singleWeeks += 1;
            else multiWeeks += 1;
          }
          const status = total <= 0 ? "gap" : (total === 1 ? "single" : "multi");
          const regularGames = Array.isArray(weekAssignments.get(weekKey)) ? weekAssignments.get(weekKey) : [];
          const dragAssignment = regularGames.length === 1 ? regularGames[0] : null;
          return {
            weekKey,
            regular: Number(counts.regular || 0),
            guest: Number(counts.guest || 0),
            total,
            status,
            dragAssignment,
            draggable: !!dragAssignment,
          };
        });
        return {
          teamId,
          games: Number(teamRow?.games || 0),
          guest: Number(teamRow?.guest || 0),
          byeWeeks: Number(teamRow?.byeWeeks || 0),
          zeroWeeks,
          singleWeeks,
          multiWeeks,
          longestIdleGap,
          cells,
        };
      })
      .sort((a, b) => {
        const gapDiff = b.zeroWeeks - a.zeroWeeks;
        if (gapDiff !== 0) return gapDiff;
        const idleDiff = b.longestIdleGap - a.longestIdleGap;
        if (idleDiff !== 0) return idleDiff;
        return a.teamId.localeCompare(b.teamId);
      });

    const shownLanes = lanes.slice(0, 20);
    return {
      weekKeys,
      lanes: shownLanes,
      totalTeams: lanes.length,
      shownTeams: shownLanes.length,
    };
  }, [preview, previewCollections.regularAssignments, regularBalanceReport]);

  const priorityPairInfoByKey = useMemo(() => {
    const pairRows = Array.isArray(regularBalanceReport?.pairRows) ? regularBalanceReport.pairRows : [];
    const map = new Map();
    pairRows.forEach((pair) => {
      const key = String(pair?.key || "").trim();
      if (!key) return;
      map.set(key, {
        key,
        teamA: String(pair?.teamA || "").trim(),
        teamB: String(pair?.teamB || "").trim(),
        manualPriorityWeight: Math.max(0, Number(pair?.manualPriorityWeight || 0)),
        autoRepeatPriority: !!pair?.autoRepeatPriority,
        priorityLabel: String(pair?.priorityLabel || "").trim(),
      });
    });
    return map;
  }, [regularBalanceReport]);

  const selectedRepairPriorityImpact = useMemo(
    () => summarizeRepairProposalPriorityImpact(selectedRepairScope, priorityPairInfoByKey),
    [selectedRepairScope, priorityPairInfoByKey]
  );
  const selectedRepairPriorityBadge = useMemo(
    () => classifyRepairPriorityImpact(selectedRepairPriorityImpact),
    [selectedRepairPriorityImpact]
  );

  const fieldHeatmapReport = useMemo(() => {
    if (!preview) {
      return { weekKeys: [], rows: [], totalFields: 0, shownFields: 0 };
    }

    const regularAssignments = previewCollections.regularAssignments;
    const regularOpenSlots = previewCollections.regularUnassignedSlots;
    const weekKeysSet = new Set(Array.isArray(regularBalanceReport.weekKeys) ? regularBalanceReport.weekKeys : []);

    [...regularAssignments, ...regularOpenSlots].forEach((slot) => {
      const weekKey = weekStartIso(slot?.gameDate);
      if (weekKey) weekKeysSet.add(weekKey);
    });

    const weekKeys = [...weekKeysSet].sort((a, b) => a.localeCompare(b));
    const fieldMap = new Map();

    const ensureFieldWeek = (fieldKey, weekKey) => {
      const field = String(fieldKey || "").trim() || "(Unknown field)";
      const week = String(weekKey || "").trim();
      if (!week) return null;
      let byWeek = fieldMap.get(field);
      if (!byWeek) {
        byWeek = new Map();
        fieldMap.set(field, byWeek);
      }
      if (!byWeek.has(week)) {
        byWeek.set(week, { capacity: 0, used: 0, guest: 0, regular: 0, regularAssignments: [] });
      }
      return byWeek.get(week);
    };

    regularAssignments.forEach((slot) => {
      const weekKey = weekStartIso(slot?.gameDate);
      const cell = ensureFieldWeek(slot?.fieldKey, weekKey);
      if (!cell) return;
      cell.capacity += 1;
      cell.used += 1;
      if (slot?.isExternalOffer) cell.guest += 1;
      else
      {
        cell.regular += 1;
        if (Array.isArray(cell.regularAssignments)) cell.regularAssignments.push(slot);
      }
    });

    regularOpenSlots.forEach((slot) => {
      const weekKey = weekStartIso(slot?.gameDate);
      const cell = ensureFieldWeek(slot?.fieldKey, weekKey);
      if (!cell) return;
      cell.capacity += 1;
    });

    const rows = [...fieldMap.entries()]
      .map(([fieldKey, byWeek]) => {
        let totalCapacity = 0;
        let totalUsed = 0;
        const cells = weekKeys.map((weekKey) => {
          const data = byWeek.get(weekKey) || { capacity: 0, used: 0, guest: 0, regular: 0, regularAssignments: [] };
          totalCapacity += data.capacity;
          totalUsed += data.used;
          const utilizationPct = data.capacity > 0 ? Math.round((data.used / data.capacity) * 100) : null;
          const regularAssignmentsForCell = Array.isArray(data?.regularAssignments) ? data.regularAssignments : [];
          return {
            weekKey,
            ...data,
            utilizationPct,
            regularGameCount: regularAssignmentsForCell.length,
            dragAssignment: regularAssignmentsForCell.length === 1 ? regularAssignmentsForCell[0] : null,
            draggable: regularAssignmentsForCell.length === 1,
          };
        });
        return {
          fieldKey,
          totalCapacity,
          totalUsed,
          totalUnused: Math.max(0, totalCapacity - totalUsed),
          utilizationPct: totalCapacity > 0 ? Math.round((totalUsed / totalCapacity) * 100) : 0,
          cells,
        };
      })
      .sort((a, b) => {
        const utilDiff = b.utilizationPct - a.utilizationPct;
        if (utilDiff !== 0) return utilDiff;
        const capDiff = b.totalCapacity - a.totalCapacity;
        if (capDiff !== 0) return capDiff;
        return a.fieldKey.localeCompare(b.fieldKey);
      });

    const shownRows = rows.slice(0, 16);
    return {
      weekKeys,
      rows: shownRows,
      totalFields: rows.length,
      shownFields: shownRows.length,
    };
  }, [preview, previewCollections.regularAssignments, previewCollections.regularUnassignedSlots, regularBalanceReport]);

  const regularCalendarTimelineReport = useMemo(() => {
    if (!preview) {
      return {
        weekRows: [],
        shownWeeks: 0,
        totalWeeks: 0,
        weekdayColumns: WEEKDAY_OPTIONS,
      };
    }

    const regularAssignments = previewCollections.regularAssignments;
    const regularOpenSlots = previewCollections.regularUnassignedSlots;
    const weekKeysSet = new Set(Array.isArray(regularBalanceReport.weekKeys) ? regularBalanceReport.weekKeys : []);

    [...regularAssignments, ...regularOpenSlots].forEach((row) => {
      const weekKey = weekStartIso(row?.gameDate);
      if (weekKey) weekKeysSet.add(weekKey);
    });
    const weekKeys = [...weekKeysSet].sort((a, b) => a.localeCompare(b));

    const byWeek = new Map();
    const ensureWeek = (weekKey) => {
      if (!weekKey) return null;
      if (!byWeek.has(weekKey)) {
        const cells = Object.fromEntries(WEEKDAY_OPTIONS.map((day) => [day, {
          games: 0,
          guest: 0,
          open: 0,
          firstDate: "",
          lastDate: "",
          regularAssignments: [],
        }]));
        byWeek.set(weekKey, {
          weekKey,
          totalGames: 0,
          totalGuest: 0,
          totalOpen: 0,
          cells,
        });
      }
      return byWeek.get(weekKey);
    };

    const touchCell = (container, day, gameDate, updater) => {
      if (!container || !WEEKDAY_OPTIONS.includes(day)) return;
      const cell = container.cells?.[day];
      if (!cell) return;
      updater(cell);
      if (gameDate) {
        if (!cell.firstDate || gameDate < cell.firstDate) cell.firstDate = gameDate;
        if (!cell.lastDate || gameDate > cell.lastDate) cell.lastDate = gameDate;
      }
    };

    regularAssignments.forEach((a) => {
      const weekKey = weekStartIso(a?.gameDate);
      const day = isoDayShort(a?.gameDate);
      const row = ensureWeek(weekKey);
      if (!row) return;
      row.totalGames += 1;
      if (a?.isExternalOffer) row.totalGuest += 1;
      touchCell(row, day, a?.gameDate, (cell) => {
        cell.games += 1;
        if (a?.isExternalOffer) cell.guest += 1;
        else if (Array.isArray(cell.regularAssignments)) cell.regularAssignments.push(a);
      });
    });

    regularOpenSlots.forEach((s) => {
      const weekKey = weekStartIso(s?.gameDate);
      const day = isoDayShort(s?.gameDate);
      const row = ensureWeek(weekKey);
      if (!row) return;
      row.totalOpen += 1;
      touchCell(row, day, s?.gameDate, (cell) => {
        cell.open += 1;
      });
    });

    const hardRuleByWeek = new Map();
    const softRuleByWeek = new Map();
    (Array.isArray(previewRuleHealth?.groups) ? previewRuleHealth.groups : []).forEach((group) => {
      const severity = String(group?.severity || "").toLowerCase();
      const violations = Array.isArray(group?.violations) ? group.violations : [];
      violations.forEach((violation) => {
        const weekKeysForViolation = Array.isArray(violation?.weekKeys) ? violation.weekKeys : [];
        weekKeysForViolation.forEach((weekKeyRaw) => {
          const weekKey = String(weekKeyRaw || "").trim();
          if (!weekKey) return;
          const map = severity === "hard" ? hardRuleByWeek : softRuleByWeek;
          map.set(weekKey, (map.get(weekKey) || 0) + 1);
        });
      });
    });

    const weekRows = weekKeys.map((weekKey, idx) => {
      const row = byWeek.get(weekKey) || ensureWeek(weekKey);
      const weekStart = weekKey;
      const weekEnd = addIsoDays(weekKey, 6);
      const totalCapacity = Number(row?.totalGames || 0) + Number(row?.totalOpen || 0);
      const utilizationPct = totalCapacity > 0 ? Math.round(((row?.totalGames || 0) / totalCapacity) * 100) : 0;
      const progress = weekKeys.length > 1 ? (idx / (weekKeys.length - 1)) : 1;
      const weatherWeight = Number((0.85 + (progress * 0.35)).toFixed(2));
      const phaseBand = progress >= 0.67 ? "late" : (progress >= 0.34 ? "mid" : "early");
      const blocked = (activeBlockedRanges || []).some((range) => {
        const startDate = String(range?.startDate || "").trim();
        const endDate = String(range?.endDate || "").trim();
        if (!isIsoDate(startDate) || !isIsoDate(endDate)) return false;
        return !(weekEnd < startDate || weekStart > endDate);
      });
      return {
        weekKey,
        weekIndex: idx + 1,
        weekStart,
        weekEnd,
        totalGames: Number(row?.totalGames || 0),
        totalGuest: Number(row?.totalGuest || 0),
        totalOpen: Number(row?.totalOpen || 0),
        totalCapacity,
        utilizationPct,
        weatherWeight,
        phaseBand,
        blocked,
        hardRuleTouches: hardRuleByWeek.get(weekKey) || 0,
        softRuleTouches: softRuleByWeek.get(weekKey) || 0,
        cells: WEEKDAY_OPTIONS.map((day) => ({
          day,
          ...(() => {
            const base = row?.cells?.[day] || { games: 0, guest: 0, open: 0, firstDate: "", lastDate: "", regularAssignments: [] };
            const regularAssignmentsForCell = Array.isArray(base?.regularAssignments) ? base.regularAssignments : [];
            return {
              ...base,
              regularGameCount: regularAssignmentsForCell.length,
              dragAssignment: regularAssignmentsForCell.length === 1 ? regularAssignmentsForCell[0] : null,
              draggable: regularAssignmentsForCell.length === 1,
            };
          })(),
        })),
      };
    });

    const shownRows = weekRows.slice(0, 16);
    return {
      weekRows: shownRows,
      shownWeeks: shownRows.length,
      totalWeeks: weekRows.length,
      weekdayColumns: WEEKDAY_OPTIONS,
    };
  }, [preview, previewCollections.regularAssignments, previewCollections.regularUnassignedSlots, previewRuleHealth, regularBalanceReport, activeBlockedRanges]);

  const selectedGameExplain = useMemo(() => {
    if (!preview || !selectedExplainGameKey) return null;

    const assignments = previewCollections.assignments;
    const unassignedSlots = previewCollections.unassignedSlots;
    const selected = assignments.find((assignment) => assignmentExplainKey(assignment) === selectedExplainGameKey);
    if (!selected) return null;

    const selectedPhase = String(selected?.phase || "").trim();
    const selectedWeekKey = weekStartIso(selected?.gameDate);
    const selectedSlotId = String(selected?.slotId || "").trim();
    const selectedHomeTeamId = String(selected?.homeTeamId || "").trim();
    const selectedAwayTeamId = String(selected?.awayTeamId || "").trim();
    const selectedTeamIds = [selectedHomeTeamId, selectedAwayTeamId].filter(Boolean);
    const constructionStrategy = String(preview?.constructionStrategy || "").trim();
    const seed = Number(preview?.seed);
    const backendExplanation = previewExplainMap && typeof previewExplainMap[selectedExplainGameKey] === "object"
      ? previewExplainMap[selectedExplainGameKey]
      : null;

    const regularAssignments = assignments.filter((a) => a?.phase === "Regular Season");
    const regularWeekKeys = Array.isArray(regularBalanceReport.weekKeys) ? regularBalanceReport.weekKeys : [];
    const weekIndex = selectedPhase === "Regular Season" && selectedWeekKey
      ? regularWeekKeys.indexOf(selectedWeekKey)
      : -1;
    const weekCount = regularWeekKeys.length;
    const weekNumber = weekIndex >= 0 ? weekIndex + 1 : null;
    const lateSeasonFactor = weekIndex >= 0 && weekCount > 1 ? (weekIndex / (weekCount - 1)) : null;

    const teamWeekCounts = new Map();
    const addTeamWeekCount = (teamIdRaw, weekKey, bucket) => {
      const teamId = String(teamIdRaw || "").trim();
      if (!teamId || !weekKey) return;
      let weekMap = teamWeekCounts.get(teamId);
      if (!weekMap) {
        weekMap = new Map();
        teamWeekCounts.set(teamId, weekMap);
      }
      if (!weekMap.has(weekKey)) {
        weekMap.set(weekKey, { regular: 0, guest: 0 });
      }
      const cell = weekMap.get(weekKey);
      cell[bucket] += 1;
    };

    regularAssignments.forEach((assignment) => {
      const weekKey = weekStartIso(assignment?.gameDate);
      if (!weekKey) return;
      if (assignment?.isExternalOffer) {
        addTeamWeekCount(assignment?.homeTeamId, weekKey, "guest");
        return;
      }
      addTeamWeekCount(assignment?.homeTeamId, weekKey, "regular");
      addTeamWeekCount(assignment?.awayTeamId, weekKey, "regular");
    });

    const buildTeamWeekSummary = (teamId) => {
      if (!teamId || !selectedWeekKey || selectedPhase !== "Regular Season") return null;
      const weekMap = teamWeekCounts.get(teamId) || new Map();
      const counts = weekMap.get(selectedWeekKey) || { regular: 0, guest: 0 };
      const total = Number(counts.regular || 0) + Number(counts.guest || 0);
      return {
        teamId,
        regular: Number(counts.regular || 0),
        guest: Number(counts.guest || 0),
        total,
        status: total <= 0 ? "gap" : (total === 1 ? "single" : "multi"),
      };
    };

    const fieldWeekMap = new Map();
    const ensureFieldWeek = (fieldKeyRaw, weekKey) => {
      const fieldKey = String(fieldKeyRaw || "").trim() || "(Unknown field)";
      if (!weekKey) return null;
      let weekMap = fieldWeekMap.get(fieldKey);
      if (!weekMap) {
        weekMap = new Map();
        fieldWeekMap.set(fieldKey, weekMap);
      }
      if (!weekMap.has(weekKey)) {
        weekMap.set(weekKey, { used: 0, capacity: 0, regular: 0, guest: 0 });
      }
      return weekMap.get(weekKey);
    };

    regularAssignments.forEach((assignment) => {
      const weekKey = weekStartIso(assignment?.gameDate);
      const cell = ensureFieldWeek(assignment?.fieldKey, weekKey);
      if (!cell) return;
      cell.capacity += 1;
      cell.used += 1;
      if (assignment?.isExternalOffer) cell.guest += 1;
      else cell.regular += 1;
    });
    unassignedSlots
      .filter((slot) => slot?.phase === "Regular Season")
      .forEach((slot) => {
        const weekKey = weekStartIso(slot?.gameDate);
        const cell = ensureFieldWeek(slot?.fieldKey, weekKey);
        if (!cell) return;
        cell.capacity += 1;
      });

    const selectedFieldUsage = selectedPhase === "Regular Season" && selectedWeekKey
      ? (() => {
          const byWeek = fieldWeekMap.get(String(selected?.fieldKey || "").trim() || "(Unknown field)");
          const cell = byWeek?.get(selectedWeekKey) || null;
          if (!cell) return null;
          const utilizationPct = cell.capacity > 0 ? Math.round((cell.used / cell.capacity) * 100) : 0;
          return { ...cell, utilizationPct };
        })()
      : null;

    const pairRows = Array.isArray(regularBalanceReport.pairRows) ? regularBalanceReport.pairRows : [];
    const pairKey = selectedHomeTeamId && selectedAwayTeamId
      ? [selectedHomeTeamId, selectedAwayTeamId].sort((a, b) => a.localeCompare(b)).join("|")
      : "";
    const pairBalance = selectedPhase === "Regular Season" && !selected?.isExternalOffer && pairKey
      ? pairRows.find((row) => row?.key === pairKey) || null
      : null;

    const ruleGroups = Array.isArray(previewRuleHealth?.groups) ? previewRuleHealth.groups : [];
    const relatedRuleGroups = ruleGroups
      .map((group) => {
        const violations = Array.isArray(group?.violations) ? group.violations : [];
        const matchedViolations = violations.filter((violation) => {
          const slotIds = new Set((Array.isArray(violation?.slotIds) ? violation.slotIds : []).map((v) => String(v || "").trim()).filter(Boolean));
          const teamIds = new Set((Array.isArray(violation?.teamIds) ? violation.teamIds : []).map((v) => String(v || "").trim()).filter(Boolean));
          const weekKeys = new Set((Array.isArray(violation?.weekKeys) ? violation.weekKeys : []).map((v) => String(v || "").trim()).filter(Boolean));
          const slotMatch = selectedSlotId && slotIds.has(selectedSlotId);
          const teamMatch = selectedTeamIds.some((teamId) => teamIds.has(teamId));
          const weekMatch = selectedWeekKey && weekKeys.has(selectedWeekKey);
          return slotMatch || teamMatch || weekMatch;
        });
        if (!matchedViolations.length) return null;
        return {
          ruleId: group?.ruleId || "",
          severity: String(group?.severity || "").toLowerCase(),
          summary: group?.summary || "",
          matchedViolations,
        };
      })
      .filter(Boolean)
      .sort((left, right) => {
        const sev = (left.severity === "hard" ? 0 : 1) - (right.severity === "hard" ? 0 : 1);
        if (sev !== 0) return sev;
        return (left.ruleId || "").localeCompare(right.ruleId || "");
      });

    const homeWeek = buildTeamWeekSummary(selectedHomeTeamId);
    const awayWeek = buildTeamWeekSummary(selectedAwayTeamId);

    const scoringFactors = [];
    if (isEngineTraceSource(backendExplanation?.source)) {
      scoringFactors.push({
        key: "scheduler-trace",
        label: "Scheduler score trace",
        tone: "neutral",
        detail:
          `Engine evaluated ${backendExplanation?.candidateCount ?? "?"} candidate(s), ` +
          `${backendExplanation?.feasibleCandidateCount ?? "?"} feasible` +
          (backendExplanation?.selectedScore != null ? `, selected score ${backendExplanation.selectedScore}.` : "."),
      });
    }
    if (backendExplanation?.fixedHomeTeamId) {
      scoringFactors.push({
        key: "fixed-home-slot",
        label: "Offering team / fixed-home filter",
        tone: "neutral",
        detail: `Slot constrained to offering team ${backendExplanation.fixedHomeTeamId} as home.`,
      });
    }
    if (backendExplanation?.source === "preview_repair_move_v1") {
      const from = backendExplanation?.movedFrom || {};
      const to = backendExplanation?.movedTo || {};
      scoringFactors.push({
        key: "preview-repair-move",
        label: "Preview repair move",
        tone: "warn",
        detail:
          `Moved in preview repair: ${from?.gameDate || "?"} ${from?.startTime || "?"}-${from?.endTime || "?"} ${from?.fieldKey || ""}`.trim() +
          ` -> ${to?.gameDate || "?"} ${to?.startTime || "?"}-${to?.endTime || "?"} ${to?.fieldKey || ""}`.trim(),
      });
    }
    if (selectedPhase === "Regular Season" && constructionStrategy.startsWith("backward_") && weekNumber && weekCount > 0) {
      scoringFactors.push({
        key: "backward-priority",
        label: "Backward allocation priority",
        tone: "good",
        detail:
          lateSeasonFactor == null
            ? `Regular-season slot scheduled with backward strategy (${constructionStrategy}).`
            : `Regular-season week W${weekNumber} of ${weekCount} (later weeks prioritized first; this sits at ~${Math.round(lateSeasonFactor * 100)}% toward season end).`,
      });
    }
    if (homeWeek || awayWeek) {
      const parts = [homeWeek, awayWeek]
        .filter(Boolean)
        .map((row) => `${row.teamId}: ${row.total} game${row.total === 1 ? "" : "s"} this week (${row.regular} reg, ${row.guest} guest)`);
      const bothSingle = [homeWeek, awayWeek].filter(Boolean).every((row) => row.total === 1);
      scoringFactors.push({
        key: "weekly-participation",
        label: "Weekly participation coverage",
        tone: bothSingle ? "good" : ([homeWeek, awayWeek].some((row) => row && row.total > 1) ? "warn" : "neutral"),
        detail: parts.join(" | ") || "No weekly participation data available.",
      });
    }
    if (pairBalance) {
      scoringFactors.push({
        key: "pair-balance",
        label: "Opponent balance",
        tone: Number(pairBalance?.assigned || 0) < Number(pairBalance?.target || 0) ? "good" : "neutral",
        detail: `${pairBalance.teamA} vs ${pairBalance.teamB}: assigned ${pairBalance.assigned}/${pairBalance.target}, unassigned ${pairBalance.unassigned}, home split ${pairBalance.homeForA}-${pairBalance.homeForB}.`,
      });
    }
    if (selectedFieldUsage) {
      scoringFactors.push({
        key: "field-utilization",
        label: "Field-week utilization",
        tone: selectedFieldUsage.utilizationPct >= 85 ? "good" : (selectedFieldUsage.utilizationPct <= 35 ? "neutral" : "warn"),
        detail: `${selected?.fieldKey || "(Unknown field)"} in ${selectedWeekKey}: used ${selectedFieldUsage.used}/${selectedFieldUsage.capacity} slots (${selectedFieldUsage.utilizationPct}% utilized).`,
      });
    }
    if (selected?.isExternalOffer) {
      scoringFactors.push({
        key: "external-offer",
        label: "External / guest game",
        tone: "neutral",
        detail: "This assignment is marked as an external offer (guest game), so opponent balance metrics differ from intra-division games.",
      });
    }
    if (!scoringFactors.length) {
      scoringFactors.push({
        key: "fallback",
        label: "Placement context",
        tone: "neutral",
        detail: "No detailed placement factors were derived for this assignment yet.",
      });
    }

    const hardRuleTouches = relatedRuleGroups.reduce(
      (count, group) => count + (group.severity === "hard" ? group.matchedViolations.length : 0),
      0
    );
    const softRuleTouches = relatedRuleGroups.reduce(
      (count, group) => count + (group.severity !== "hard" ? group.matchedViolations.length : 0),
      0
    );

    return {
      selected,
      selectedWeekKey,
      weekNumber,
      weekCount,
      lateSeasonFactor,
      constructionStrategy,
      seed: Number.isFinite(seed) ? seed : null,
      homeWeek,
      awayWeek,
      pairBalance,
      selectedFieldUsage,
      backendExplanation,
      relatedRuleGroups,
      hardRuleTouches,
      softRuleTouches,
      scoringFactors,
    };
  }, [preview, previewCollections.assignments, previewCollections.unassignedSlots, previewExplainMap, previewRuleHealth, regularBalanceReport, selectedExplainGameKey]);

  const previewRecommendations = useMemo(() => {
    if (!preview) return [];

    const summary = preview.summary || {};
    const regularSummary = summary.regularSeason || {};
    const teamCountValue = Number(summary.teamCount) || 0;
    const oddTeamCount = teamCountValue > 0 && teamCountValue % 2 === 1;
    const guestGamesValue = Math.max(0, Number(guestGamesPerWeek) || 0);
    const maxGamesValue = Math.max(0, Number(maxGamesPerWeek) || 0);
    const selectedGuestAnchorCount = [guestAnchorPrimarySlotId, guestAnchorSecondarySlotId].filter(Boolean).length;
    const recommendedGuestGames = Math.max(
      oddTeamCount ? 2 : 1,
      Number(feasibility?.recommendations?.optimalGuestGamesPerWeek) || 0
    );

    const rows = [];

    if (oddTeamCount) {
      const byeCounts = regularBalanceReport.teamRows.map((row) => row.byeWeeks);
      const maxBye = byeCounts.length ? Math.max(...byeCounts) : 0;
      const minBye = byeCounts.length ? Math.min(...byeCounts) : 0;
      const canCoverByesWithGuestSlots = guestGamesValue >= 2 && maxGamesValue >= 2;
      if (byeCounts.length && regularBalanceReport.weekCount > 0) {
        const heavyByeTeams = regularBalanceReport.teamRows
          .filter((row) => row.byeWeeks === maxBye)
          .slice(0, 4)
          .map((row) => row.teamId);
        rows.push({
          code: "ODD_TEAM_BYE_CONTEXT",
          tone: canCoverByesWithGuestSlots && maxBye > 0 ? "warning" : maxBye - minBye > 1 ? "warning" : "info",
          message:
            (canCoverByesWithGuestSlots
              ? `Odd team count (${teamCountValue}) does not require BYEs when guest slots are active. `
              : `Odd team count (${teamCountValue}) creates weekly idle-team pressure unless guest slots absorb it. `) +
            `Estimated BYE weeks across ${regularBalanceReport.weekCount} regular-season week(s): min ${minBye}, max ${maxBye}` +
            (heavyByeTeams.length ? ` (highest: ${heavyByeTeams.join(", ")}).` : "."),
        });
      } else {
        rows.push({
          code: "ODD_TEAM_BYE_CONTEXT",
          tone: "info",
          message: `Odd team count (${teamCountValue}) creates one idle team each round. Use two guest slots/week plus Max games/week = 2 to absorb that team instead of defaulting to BYEs.`,
        });
      }

      if (guestGamesValue < 2) {
        rows.push({
          code: "GUEST_GAME_RECOMMENDATION",
          tone: "warning",
          suggestedGuestGamesPerWeek: recommendedGuestGames,
          message:
            `This division has an odd team count. Target Guest games/week = ${recommendedGuestGames} so the scheduler can keep two recurring guest slots available each week and avoid avoidable BYEs.`,
        });
      }

      if (guestGamesValue >= 2 && maxGamesValue > 0 && maxGamesValue < 2) {
        rows.push({
          code: "MAX_GAMES_WEEK_RECOMMENDATION",
          tone: "warning",
          suggestedMaxGamesPerWeek: 2,
          message:
            `Guest games are set to ${guestGamesValue}/week, but Max games/week is ${maxGamesValue}. Raise Max games/week to 2 so an idle team can take a guest game instead of a BYE.`,
        });
      }

      if (guestGamesValue >= 2 && guestAnchorOptions.length >= 2 && selectedGuestAnchorCount < 2) {
        rows.push({
          code: "GUEST_ANCHOR_RECOMMENDATION",
          tone: "warning",
          message:
            "Set the required guest anchor options in Slot plan. The selected day, time, and field are treated as exact weekly guest requirements and stay locked in preview.",
        });
      }

      if (guestGamesValue > 0 && regularBalanceReport.totalGuestGames === 0 && (regularSummary.unassignedSlots || 0) > 0) {
        rows.push({
          code: "GUEST_GAME_NOT_PLACED",
          tone: "warning",
          message:
            `Guest games are enabled (${guestGamesValue}/week) but none were placed. Check that enough recurring slots stay tagged Game/Both and that your required guest anchor field/time patterns exist in the slot plan.`,
        });
      } else if (regularBalanceReport.totalGuestGames > 0) {
        rows.push({
          code: "GUEST_GAME_LOCKED_NOTICE",
          tone: "info",
          message:
            "Placed guest slots stay locked in preview. The wizard reserves guest capacity first, then places regular league matchups around it.",
        });
      }

      if (canCoverByesWithGuestSlots && maxBye > 0) {
        rows.push({
          code: "ODD_TEAM_CONFLICT_AVOIDANCE",
          tone: "warning",
          message:
            `You already have the right weekly guest capacity, so the remaining BYEs are coming from slot conflicts. Keep two weekly guest slots unblocked, tagged Game/Both, and anchored so repairs do not repurpose them.`,
        });
      }
    }

    if (regularBalanceReport.pairRows.length > 0) {
      const scheduledPairs = regularBalanceReport.pairRows.filter((row) => row.assigned > 0);
      if (scheduledPairs.length > 1) {
        const counts = scheduledPairs.map((row) => row.assigned);
        const maxAssigned = Math.max(...counts);
        const minAssigned = Math.min(...counts);
        if (maxAssigned - minAssigned > 1) {
          rows.push({
            code: "PAIR_BALANCE_REVIEW",
            tone: "warning",
            message:
              `Matchup distribution is uneven: some pairs are scheduled ${maxAssigned} time(s) while others are at ${minAssigned}. ` +
              `Use the matrix below to confirm the intended round-robin balance.`,
          });
        }
      }

      const unfilledPairs = regularBalanceReport.pairRows.filter((row) => row.unassigned > 0);
      if (unfilledPairs.length > 0) {
        const sample = unfilledPairs
          .slice(0, 3)
          .map((row) => `${row.teamA} vs ${row.teamB} (${row.unassigned})`)
          .join(", ");
        rows.push({
          code: "UNFILLED_PAIRINGS",
          tone: "info",
          message: `Unfilled regular-season pairings remain: ${sample}${unfilledPairs.length > 3 ? " ..." : ""}`,
        });
      }
    }

    return rows;
  }, [
    preview,
    guestGamesPerWeek,
    maxGamesPerWeek,
    feasibility,
    regularBalanceReport,
    guestAnchorPrimarySlotId,
    guestAnchorSecondarySlotId,
    guestAnchorOptions.length,
  ]);

  const planningChecksReport = useMemo(() => {
    if (!preview) return [];

    const checks = [];
    const issues = previewCollections.issues;
    const regularIssues = issues.filter((issue) => getIssuePhase(issue) === "Regular Season");

    const maxGamesLimit = Number(maxGamesPerWeek) || 0;
    if (maxGamesLimit > 0) {
      const hardCapCount = regularIssues.filter((issue) => issue?.ruleId === "max-games-per-week").length;
      checks.push({
        check: "Max games/week hard cap",
        scope: "Regular Season",
        result: hardCapCount > 0 ? "WARN" : "PASS",
        details:
          hardCapCount > 0
            ? `${hardCapCount} violation(s) found. Cap is ${maxGamesLimit}.`
            : `No team exceeds ${maxGamesLimit} games per week.`,
      });
    } else {
      checks.push({
        check: "Max games/week hard cap",
        scope: "Regular Season",
        result: "INFO",
        details: "Cap disabled (0). No hard weekly cap enforcement applied.",
      });
    }

    if (noDoubleHeaders) {
      checks.push({
        check: "Doubleheader fairness",
        scope: "Regular Season",
        result: "INFO",
        details: "No-doubleheaders is ON, so fairness balancing for doubleheaders is not needed.",
      });
    } else {
      const balanceIssue = regularIssues.find((issue) => issue?.ruleId === "double-header-balance");
      if (balanceIssue) {
        const max = Number(balanceIssue?.details?.maxDoubleHeaders ?? 0);
        const min = Number(balanceIssue?.details?.minDoubleHeaders ?? 0);
        checks.push({
          check: "Doubleheader fairness",
          scope: "Regular Season",
          result: "WARN",
          details: `Uneven distribution detected (max ${max}, min ${min}).`,
        });
      } else {
        checks.push({
          check: "Doubleheader fairness",
          scope: "Regular Season",
          result: "PASS",
          details: "Doubleheaders are evenly distributed within allowed tolerance.",
        });
      }
    }

    const guestWeeklyTarget = Number(guestGamesPerWeek) || 0;
    if (guestWeeklyTarget <= 0) {
      checks.push({
        check: "Guest-game rotation",
        scope: "Regular Season",
        result: "INFO",
        details: "Guest games are disabled.",
      });
    } else {
      const regularAssignments = previewCollections.regularAssignments;
      const guestAssignments = previewCollections.regularGuestAssignments;

      const teamIds = new Set();
      regularAssignments.forEach((a) => {
        if (a?.homeTeamId) teamIds.add(a.homeTeamId);
        if (a?.awayTeamId) teamIds.add(a.awayTeamId);
      });
      previewCollections.regularUnassignedMatchups.forEach((m) => {
        if (m?.homeTeamId) teamIds.add(m.homeTeamId);
        if (m?.awayTeamId) teamIds.add(m.awayTeamId);
      });
      guestAssignments.forEach((a) => {
        if (a?.homeTeamId) teamIds.add(a.homeTeamId);
      });

      if (!teamIds.size) {
        checks.push({
          check: "Guest-game rotation",
          scope: "Regular Season",
          result: "INFO",
          details: "Could not derive team list for rotation check.",
        });
      } else if (!guestAssignments.length) {
        checks.push({
          check: "Guest-game rotation",
          scope: "Regular Season",
          result: "WARN",
          details: "No guest assignments were produced.",
        });
      } else {
        const guestCounts = new Map();
        [...teamIds].forEach((teamId) => guestCounts.set(teamId, 0));
        guestAssignments.forEach((a) => {
          const teamId = a.homeTeamId;
          guestCounts.set(teamId, (guestCounts.get(teamId) || 0) + 1);
        });
        const counts = [...guestCounts.values()];
        const max = Math.max(...counts);
        const min = Math.min(...counts);
        const gap = max - min;
        const expectedMin = Math.floor(guestAssignments.length / guestCounts.size);
        const expectedMax = Math.ceil(guestAssignments.length / guestCounts.size);
        const allowedGap = expectedMax - expectedMin;
        const pass = gap <= allowedGap;

        checks.push({
          check: "Guest-game rotation",
          scope: "Regular Season",
          result: pass ? "PASS" : "WARN",
          details: `${buildGuestRotationDetail(guestCounts, guestAssignments.length)} ${
            pass
              ? `(gap ${gap}, expected <= ${allowedGap}).`
              : `(gap ${gap} exceeds expected <= ${allowedGap}).`
          }`,
        });
      }
    }

    if (unassignedRegularReport.totalMatchups <= 0) {
      checks.push({
        check: "Unassigned matchup balance",
        scope: "Regular Season",
        result: "PASS",
        details: "All regular-season matchups were assigned.",
      });
    } else if (!unassignedRegularReport.rows.length) {
      checks.push({
        check: "Unassigned matchup balance",
        scope: "Regular Season",
        result: "WARN",
        details: `${unassignedRegularReport.totalMatchups} regular-season matchup(s) remain unassigned.`,
      });
    } else {
      const counts = unassignedRegularReport.rows.map((row) => row.unassigned);
      const max = Math.max(...counts);
      const min = Math.min(...counts);
      const gap = max - min;
      const teamsAtMax = unassignedRegularReport.rows
        .filter((row) => row.unassigned === max)
        .slice(0, 4)
        .map((row) => row.teamId)
        .join(", ");
      const fitHint =
        unassignedRegularReport.openSlots > 0
          ? `${unassignedRegularReport.openSlots} open regular slot(s) remain; relaxing strict rules may fit more matchups.`
          : "No open regular slots remain; add or reclassify game-capable slots.";
      checks.push({
        check: "Unassigned matchup balance",
        scope: "Regular Season",
        result: gap <= 1 ? "PASS" : "WARN",
        details: `${unassignedRegularReport.totalMatchups} unassigned matchup(s). Highest team shortfall: ${teamsAtMax || "n/a"} (${max}). Gap ${gap}. ${fitHint}`,
      });
    }

    return checks;
  }, [preview, previewCollections, maxGamesPerWeek, noDoubleHeaders, guestGamesPerWeek, unassignedRegularReport]);

  const stepStatuses = useMemo(() => {
    const completed = [
      !basicsError,
      !postseasonError && step > 1,
      !slotPlanError && slotPlanSummary.gameCapable > 0 && step > 2,
      !rulesError && step > 3,
      !!preview && !previewError,
    ];
    return steps.map((_, idx) => {
      if (stepErrors[idx]) return "error";
      if (idx === step) return "active";
      if (completed[idx]) return "complete";
      return "neutral";
    });
  }, [
    stepErrors,
    basicsError,
    postseasonError,
    slotPlanError,
    rulesError,
    previewError,
    step,
    slotPlanSummary.gameCapable,
    preview,
    steps,
  ]);
  const completedStepCount = useMemo(
    () => stepStatuses.filter((status) => status === "complete").length,
    [stepStatuses]
  );
  const currentStepIssue = (stepErrors[step] || "").trim();
  const currentStepMessage = useMemo(() => {
    if (currentStepIssue) return currentStepIssue;
    if (step === 4 && !preview) return 'Run "Preview schedule" in the previous step to see results here.';
    if (step === 4) return "Review the preview details below before applying the schedule.";
    return "No blocking issues detected for this step.";
  }, [currentStepIssue, step, preview]);
  const currentStepNextAction = useMemo(() => {
    if (currentStepIssue) return "Resolve the blocking issue in this step before moving on.";
    if (step === 3) return 'Next checkpoint: run "Preview schedule" to validate these rules.';
    if (step === 4 && !preview) return 'Next checkpoint: go back to Rules and run "Preview schedule".';
    if (step === 4 && (previewApplyBlocked || previewIssueCount > 0)) {
      return "Next checkpoint: use Health & fixes to resolve the remaining blockers.";
    }
    if (step === 4) return 'Next checkpoint: review the preview, then use "Apply schedule" when ready.';
    const nextMeta = steps[step + 1];
    if (!nextMeta) return "Next checkpoint: apply the schedule once the preview looks right.";
    return `Next checkpoint: ${nextMeta.label}.`;
  }, [currentStepIssue, step, steps, preview, previewApplyBlocked, previewIssueCount]);

  function goToStep(targetStep) {
    const nextStep = Math.max(0, Math.min(steps.length - 1, Number(targetStep)));
    if (Number.isNaN(nextStep) || nextStep === step) return;
    if (nextStep < step || nextStep <= furthestUnlockedStep) {
      setStep(nextStep);
      return;
    }
    setToast({
      tone: "warning",
      duration: 3200,
      message: `Finish the current requirements before moving ahead. ${forwardBlockMessage || "A previous step still needs attention."}`,
    });
  }

  function setPreviewSectionStoredState(storageKey, isExpanded) {
    if (!storageKey || typeof window === "undefined") return;
    try {
      localStorage.setItem(`collapsible-${storageKey}`, JSON.stringify(!!isExpanded));
    } catch {
      // Ignore localStorage errors so the UI still responds.
    }
  }

  function setAllPreviewSectionsExpanded(isExpanded) {
    PREVIEW_SECTION_IDS.forEach((sectionId) => {
      const storageKey = PREVIEW_SECTION_STORAGE_KEYS[sectionId];
      setPreviewSectionStoredState(storageKey, isExpanded);
    });
    if (isExpanded) {
      previewSectionControl.expandAll();
      return;
    }
    previewSectionControl.collapseAll();
  }

  function handlePreviewSectionToggle(sectionId, isExpanded) {
    previewSectionControl.setExpanded(sectionId, isExpanded);
    setPreviewSectionStoredState(PREVIEW_SECTION_STORAGE_KEYS[sectionId], isExpanded);
  }

  function openAvailabilitySetup() {
    if (typeof window === "undefined") return;
    try {
      localStorage.setItem("collapsible-manage-availability-setup", JSON.stringify(true));
      localStorage.setItem("collapsible-manage-fields-import", JSON.stringify(false));
    } catch {
      // Ignore localStorage errors so navigation still works.
    }

    const nextSearch = new URLSearchParams(window.location.search);
    nextSearch.set("manageTab", "fields");
    const pathname = window.location.pathname || "/";
    const search = nextSearch.toString();
    window.history.replaceState({}, "", `${pathname}${search ? `?${search}` : ""}#main-content`);
    window.dispatchEvent(new Event("popstate"));
  }

  return (
    <div className="stack gap-3">
      {toast ? <Toast {...toast} onClose={() => setToast(null)} /> : null}
      {err ? <div className="callout callout--error">{err}</div> : null}

      <div className="callout callout--warning">
        <strong>⚠️ Important:</strong> This wizard will <strong>OVERWRITE all existing game assignments</strong> in the selected division when you click "Apply schedule" at the end.
        {resetGeneratedSlotsBeforeApply ? (
          <>
            {" "}
            It will also reset existing <strong>non-practice game and guest rows</strong> in this season window before previewing and applying the new run.
          </>
        ) : (
          <>
            {" "}
            It will <strong>not</strong> reset existing non-practice game and guest rows before previewing or applying.
          </>
        )}{" "}
        It does <strong>not</strong> clear recurring allocations or field blackouts. If you need a different underlying slot pool, clear or edit availability first, then rerun the wizard.
        <label className="inlineCheck" style={{ marginTop: "0.75rem" }}>
          <input
            type="checkbox"
            checked={resetGeneratedSlotsBeforeApply}
            onChange={(e) => setResetGeneratedSlotsBeforeApply(e.target.checked)}
          />
          Reset existing non-practice game and guest slots in this season window before preview and apply
        </label>
      </div>

      <div className="row row--wrap gap-2">
        {steps.map((meta, idx) => (
          <StepButton
            key={meta.label}
            active={step === idx}
            status={stepStatuses[idx]}
            onClick={() => goToStep(idx)}
            title={idx > furthestUnlockedStep ? `${meta.description} Locked: ${forwardBlockMessage}` : meta.description}
          >
            {idx + 1}. {meta.label}
          </StepButton>
        ))}
      </div>
      <div className="subtle">Step state: green = complete, red = needs attention, neutral = pending.</div>
      <div
        className={currentStepIssue ? "callout callout--error" : "callout"}
        style={{
          position: "sticky",
          top: "0.75rem",
          zIndex: 3,
          backgroundColor: "#fff",
          boxShadow: "0 10px 24px rgba(15, 23, 42, 0.08)",
        }}
      >
        <div className="row row--between gap-2" style={{ alignItems: "center" }}>
          <div>
            <div className="font-bold">
              Step {Math.min(step + 1, steps.length)} of {steps.length}: {currentStepMeta?.label || "Wizard"}
            </div>
            <div className="subtle">{currentStepMeta?.description || ""}</div>
          </div>
          <div className="subtle">
            Completed: <b>{completedStepCount}</b> / {Math.max(steps.length - 1, 0)}
          </div>
        </div>
        <div className="row row--wrap gap-2 mt-2">
          <span className="pill">{currentStepIssue ? "Blocked" : "In progress"}</span>
          <span className="pill">{currentStepNextAction}</span>
        </div>
        <div className="subtle mt-2">
          {currentStepIssue ? `Needs attention: ${currentStepMessage}` : currentStepMessage}
        </div>
      </div>

      {step === 0 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Season basics</div>
            <div className="subtle">Pick the division and season range.</div>
          </div>
          <div className="card__body grid2">
            <label>
              Division
              <select value={division} onChange={(e) => setDivision(e.target.value)}>
                {divisions.map((d) => (
                  <option key={d.code || d.division} value={d.code || d.division}>
                    {d.name ? `${d.name} (${d.code || d.division})` : d.code || d.division}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Season start
              <input
                type="date"
                value={seasonStart}
                max={seasonEnd || undefined}
                onChange={(e) => setSeasonStart(e.target.value)}
              />
            </label>
            <label>
              Season end
              <input
                type="date"
                value={seasonEnd}
                min={seasonStart || undefined}
                onChange={(e) => setSeasonEnd(e.target.value)}
              />
            </label>
          </div>
          <div className="row row--end">
            <button
              className="btn btn--primary"
              type="button"
              onClick={() => goToStep(1)}
              disabled={!!basicsError}
              title={basicsError || "Continue to Postseason"}
            >
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 1 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Postseason windows</div>
            <div className="subtle">Pool play defaults to the Sunday-Friday before championship weekend. Championship defaults to the last weekend. You can still edit any date.</div>
          </div>
          <div className="card__body grid2">
            <label>
              Pool play start
              <input
                type="date"
                value={poolStart}
                min={seasonStart || undefined}
                max={seasonEnd || undefined}
                onChange={(e) => setPoolStart(e.target.value)}
              />
            </label>
            <label>
              Pool play end
              <input
                type="date"
                value={poolEnd}
                min={poolStart || seasonStart || undefined}
                max={seasonEnd || undefined}
                onChange={(e) => setPoolEnd(e.target.value)}
              />
            </label>
            <label>
              Championship start
              <input
                type="date"
                value={bracketStart}
                min={seasonStart || undefined}
                max={seasonEnd || undefined}
                onChange={(e) => setBracketStart(e.target.value)}
              />
            </label>
            <label>
              Championship end
              <input
                type="date"
                value={bracketEnd}
                min={bracketStart || seasonStart || undefined}
                max={seasonEnd || undefined}
                onChange={(e) => setBracketEnd(e.target.value)}
              />
            </label>
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(0)}>
              Back
            </button>
            <button
              className="btn btn--primary"
              type="button"
              onClick={() => goToStep(2)}
              disabled={!!postseasonError}
              title={postseasonError || "Continue to Slot plan"}
            >
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 2 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Slot plan (all phases)</div>
            <div className="subtle">
              Mark each availability pattern as practice, game, or both; adjust timing; and reserve guest anchors before rule tuning.
            </div>
          </div>
          <div className="card__body stack gap-3">
            <div className="callout">
              <div className="row row--wrap gap-2" style={{ alignItems: "center" }}>
                <span className="pill">Total: {slotPlanSummary.total}</span>
                <span className="pill">Practice: {slotPlanSummary.practice}</span>
                <span className="pill">Game: {slotPlanSummary.game}</span>
                <span className="pill">Both: {slotPlanSummary.both}</span>
                <span className="pill">Ranked: {slotPlanSummary.ranked}</span>
                <button className="btn btn--ghost" type="button" onClick={openAvailabilitySetup}>
                  Open availability setup
                </button>
              </div>
              <div className="subtle mt-2">
                Regular season uses <b>Game</b> and <b>Both</b>. Pool play and bracket prioritize <b>Game</b>/<b>Both</b> first, then can consume remaining <b>Practice</b> slots as fallback game space.
              </div>
              <div className="subtle">
                Use <b>Both + Refactor</b> when a slot should remain dual-use but needs game-length timing ({effectiveGameSlotMinutes} min).
              </div>
              <div className="subtle">
                These actions immediately update the slot plan <b>Type</b> and <b>End / Dur</b> values (you will also see a confirmation toast).
              </div>
              <div className="subtle">
                Pattern count is for the exact weekday/time/field pattern. Field total is all availability openings for that field in the queried window.
              </div>
            </div>

            <div className="row row--wrap gap-2">
              <button className="btn btn--ghost" type="button" onClick={() => setAllSlotTypes("practice")}>
                Set all Practice
              </button>
              <button className="btn btn--ghost" type="button" onClick={() => setAllSlotTypes("both")}>
                Set all Both
              </button>
              <button
                className="btn btn--ghost"
                type="button"
                onClick={() => setAllSlotTypesWithRefactor("both", effectiveGameSlotMinutes)}
                title={`Set all slot types to Both and refactor to ${effectiveGameSlotMinutes} minutes`}
              >
                Set all Both + Refactor ({effectiveGameSlotMinutes}m)
              </button>
              <button className="btn btn--ghost" type="button" onClick={autoRankGameSlots}>
                Auto-rank Game/Both
              </button>
            </div>

            {slotPlan.length > 0 ? (
              <div className="callout mt-3">
                <div className="font-bold mb-2">Slot Plan Templates</div>
                <div className="subtle mb-2">
                  Save the current slot type configuration as a template to reuse for future seasons.
                </div>
                <div className="row row--wrap gap-2">
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={saveSlotPlanTemplate}
                  >
                    Save Current as Template
                  </button>
                  {getSlotPlanTemplates().map((template) => (
                    <div key={template.name} className="row gap-1" style={{ alignItems: 'center' }}>
                      <button
                        className="btn btn--ghost"
                        type="button"
                        onClick={() => loadSlotPlanTemplate(template)}
                        title={`Saved ${new Date(template.savedAt).toLocaleDateString()}`}
                      >
                        Load: {template.name}
                      </button>
                      <button
                        className="btn btn--ghost"
                        type="button"
                        onClick={() => {
                          if (window.confirm(`Delete template "${template.name}"?`)) {
                            deleteSlotPlanTemplate(template.name);
                          }
                        }}
                        style={{ padding: '0.25rem 0.5rem', fontSize: '0.75rem' }}
                        title="Delete template"
                      >
                        ✕
                      </button>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}

            <div className="grid2">
              <label>
                Guest anchor option 1
                <select value={guestAnchorPrimarySlotId} onChange={(e) => setGuestAnchorPrimarySlotId(e.target.value)}>
                  <option value="">None</option>
                  {guestAnchorOptions.map((opt) => (
                    <option key={opt.slotId} value={opt.slotId}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Guest anchor option 2
                <select value={guestAnchorSecondarySlotId} onChange={(e) => setGuestAnchorSecondarySlotId(e.target.value)}>
                  <option value="">None</option>
                  {guestAnchorOptions.map((opt) => (
                    <option key={opt.slotId} value={opt.slotId}>
                      {opt.label}
                    </option>
                  ))}
                </select>
              </label>
            </div>
            <div className="subtle">
              Guest games/week is reserved before regular scheduling. When you set guest anchors, the selected day, time, and field are treated as exact weekly requirements for guest slots.
            </div>

            {availabilityLoading ? <div className="muted">Loading availability slots...</div> : null}
            {!availabilityLoading && !slotPlan.length ? (
              <div className="callout callout--error">No availability slots found in the selected season window.</div>
            ) : null}

            {slotPatterns.length ? (
              <>
                <div className="card">
                  <div className="card__header">
                    <div className="h4">Weekly availability view</div>
                    <div className="subtle">Recurring patterns by weekday; useful for spotting time overlaps before ranking.</div>
                  </div>
                  <div className="card__body seasonWeekGrid">
                    {WEEKDAY_OPTIONS.map((day) => {
                      const dayPatterns = slotPatterns.filter((p) => p.weekday === day);
                      return (
                        <div key={day} className="card" style={{ border: "1px solid #cbd5e1" }}>
                          <div className="card__header" style={{ paddingBottom: "0.25rem" }}>
                            <div className="h5">{day}</div>
                          </div>
                          <div className="card__body stack gap-1" style={{ paddingTop: 0 }}>
                            {!dayPatterns.length ? (
                              <div className="subtle">No openings</div>
                            ) : (
                              dayPatterns.map((p) => (
                                <div
                                  key={p.key}
                                  className="callout"
                                  style={{
                                    marginBottom: 0,
                                    ...(() => {
                                      const typeAppearance = getSlotTypeAppearance(p.slotType);
                                      return {
                                        backgroundColor: typeAppearance.surfaceColor,
                                        border: `1px solid ${typeAppearance.borderColor}`,
                                        borderLeft: `6px solid ${typeAppearance.accentColor}`,
                                      };
                                    })(),
                                  }}
                                >
                                  {(() => {
                                    const startMin = parseMinutes(p.startTime);
                                    const endMin = parseMinutes(p.endTime);
                                    const duration = startMin != null && endMin != null && endMin > startMin ? endMin - startMin : null;
                                    const typeAppearance = getSlotTypeAppearance(p.slotType);
                                    const activeType = SLOT_TYPE_OPTIONS.find((opt) => opt.value === normalizeSlotType(p.slotType));
                                    return (
                                      <>
                                    <div className="row row--between gap-2">
                                    <div><b>{p.startTime}-{p.endTime}</b></div>
                                    <div className="row row--wrap gap-1">
                                      <span
                                        className="pill"
                                        title={slotTypeSelectTitle(p.slotType)}
                                        style={{
                                          backgroundColor: typeAppearance.selectSurfaceColor,
                                          borderColor: typeAppearance.borderColor,
                                          color: typeAppearance.textColor,
                                          fontWeight: 700,
                                        }}
                                      >
                                        {activeType?.shortLabel || "P"} {activeType?.label || "Practice"}
                                      </span>
                                      <span className="pill">Pattern: {p.count}</span>
                                      <span className="pill">Field total: {slotFieldTotals.get(p.fieldKey) || 0}</span>
                                    </div>
                                  </div>
                                  <div className="subtle">{p.fieldKey}</div>
                                  <div className="subtle">Duration: {duration ?? "?"} min</div>
                                  <div className="row row--wrap gap-2 mt-1">
                                    <label>
                                      Type
                                      <select
                                        value={p.slotType}
                                        onChange={(e) => updatePatternSlotType(p.key, p.priorityRank, e.target.value)}
                                        title={slotTypeSelectTitle(p.slotType)}
                                        style={{
                                          width: 68,
                                          backgroundColor: typeAppearance.selectSurfaceColor,
                                          borderColor: typeAppearance.borderColor,
                                          color: typeAppearance.textColor,
                                          fontWeight: 700,
                                        }}
                                      >
                                        {SLOT_TYPE_OPTIONS.map((opt) => (
                                          <option key={opt.value} value={opt.value}>
                                            {opt.shortLabel}
                                          </option>
                                        ))}
                                      </select>
                                    </label>
                                    <label>
                                      Rank
                                      <input
                                        type="number"
                                        min="1"
                                        value={p.priorityRank}
                                        disabled={p.slotType === "practice"}
                                        onChange={(e) =>
                                          updatePatternPlan(p.key, {
                                            priorityRank:
                                              p.slotType === "practice" ? "" : normalizePriorityRank(e.target.value),
                                          })
                                        }
                                        placeholder={p.slotType === "practice" ? "-" : "1"}
                                      />
                                    </label>
                                  </div>
                                  <div className="row row--wrap gap-1 mt-1">
                                    <button className="btn btn--ghost" type="button" onClick={() => quickConvertPattern(p.key, "practice", 90)}>
                                      Practice 90m
                                    </button>
                                    <button className="btn btn--ghost" type="button" onClick={() => quickConvertPattern(p.key, "game", 120)}>
                                      Game 120m
                                    </button>
                                    <button
                                      className="btn btn--ghost"
                                      type="button"
                                      onClick={() => quickConvertPattern(p.key, "both", effectiveGameSlotMinutes)}
                                      title={`Set to Both and refactor to ${effectiveGameSlotMinutes} minutes`}
                                    >
                                      Both + {effectiveGameSlotMinutes}m
                                    </button>
                                  </div>
                                      </>
                                    );
                                  })()}
                                </div>
                              ))
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>

                <div className={`tableWrap ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                  <table className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}>
                    <thead>
                      <tr>
                        <th>Day</th>
                        <th>Start</th>
                        <th>End</th>
                        <th>Dur (min)</th>
                        <th>Field</th>
                        <th>Pattern</th>
                        <th>Field total</th>
                        <th>Score</th>
                        <th title="P = Practice, G = Game, B = Both">P/G/B</th>
                        <th>Priority</th>
                      </tr>
                    </thead>
                    <tbody>
                      {slotPatterns.map((pattern) => (
                        <tr
                          key={pattern.key}
                          style={{
                            backgroundColor: getSlotTypeAppearance(pattern.slotType).surfaceColor,
                          }}
                        >
                          {(() => {
                            const startMin = parseMinutes(pattern.startTime);
                            const endMin = parseMinutes(pattern.endTime);
                            const duration = startMin != null && endMin != null && endMin > startMin ? endMin - startMin : "";
                            const typeAppearance = getSlotTypeAppearance(pattern.slotType);
                            return (
                              <>
                          <td
                            style={{
                              borderLeft: `4px solid ${typeAppearance.accentColor}`,
                              fontWeight: 600,
                            }}
                          >
                            {pattern.weekday}
                          </td>
                          <td>
                            <input
                              type="time"
                              value={pattern.startTime || ""}
                              onChange={(e) => updatePatternStartTime(pattern.key, e.target.value)}
                              title={`Applies to all ${pattern.count} opening(s) in this pattern.`}
                            />
                          </td>
                          <td>
                            <input
                              type="time"
                              value={pattern.endTime || ""}
                              onChange={(e) => updatePatternEndTime(pattern.key, e.target.value)}
                              title={`Applies to all ${pattern.count} opening(s) in this pattern.`}
                            />
                          </td>
                          <td>
                            <input
                              type="number"
                              min="15"
                              step="15"
                              value={duration}
                              onChange={(e) => updatePatternDurationMinutes(pattern.key, e.target.value)}
                              title={`Changes end time for all ${pattern.count} opening(s) in this pattern.`}
                              style={{ width: 90 }}
                            />
                          </td>
                          <td>{pattern.fieldKey}</td>
                          <td>{pattern.count}</td>
                          <td>{slotFieldTotals.get(pattern.fieldKey) || 0}</td>
                          <td title="Higher score means this pattern appears more consistently in the season window.">
                            {pattern.score ?? 0}
                          </td>
                          <td>
                            <select
                              value={pattern.slotType}
                              onChange={(e) => updatePatternSlotType(pattern.key, pattern.priorityRank, e.target.value)}
                              title={slotTypeSelectTitle(pattern.slotType)}
                              style={{
                                width: 68,
                                backgroundColor: typeAppearance.selectSurfaceColor,
                                borderColor: typeAppearance.borderColor,
                                color: typeAppearance.textColor,
                                fontWeight: 700,
                              }}
                            >
                              {SLOT_TYPE_OPTIONS.map((opt) => (
                                <option key={opt.value} value={opt.value}>
                                  {opt.shortLabel}
                                </option>
                              ))}
                            </select>
                            <div className="mt-1">
                              <button
                                className="btn btn--ghost"
                                type="button"
                                onClick={() => quickConvertPattern(pattern.key, "both", effectiveGameSlotMinutes)}
                                style={{ padding: "0.2rem 0.45rem", fontSize: "0.75rem", lineHeight: 1.2 }}
                                title={`Set to Both and refactor to ${effectiveGameSlotMinutes} minutes`}
                              >
                                Both + {effectiveGameSlotMinutes}m
                              </button>
                            </div>
                          </td>
                          <td>
                            <input
                              type="number"
                              min="1"
                              value={pattern.priorityRank}
                              disabled={pattern.slotType === "practice"}
                              onChange={(e) =>
                                updatePatternPlan(pattern.key, {
                                  priorityRank:
                                    pattern.slotType === "practice" ? "" : normalizePriorityRank(e.target.value),
                                })
                              }
                              placeholder={pattern.slotType === "practice" ? "-" : "1"}
                            />
                          </td>
                              </>
                            );
                          })()}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </>
            ) : null}
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(1)}>
              Back
            </button>
            <button
              className="btn btn--primary"
              type="button"
              onClick={() => goToStep(3)}
              disabled={!!slotPlanError}
              title={slotPlanError || "Continue to Rules"}
            >
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 3 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Scheduling rules</div>
            <div className="subtle">Set targets, hard stops, and matchup priorities with live feasibility checking.</div>
          </div>
          <div className="card__body stack gap-3">

            {/* Feasibility Loading Banner */}
            {feasibilityLoading ? (
              <div className="callout">
                <div className="subtle">Checking feasibility...</div>
              </div>
            ) : null}

            {/* Conflict Banner (Errors/Warnings) */}
            {feasibility && feasibility.conflicts && feasibility.conflicts.length > 0 ? (
              <div className={`callout ${feasibility.conflicts.some(c => c.severity === "error") ? "callout--error" : "callout--warning"}`}>
                <div className="font-bold mb-2">
                  {feasibility.conflicts.some(c => c.severity === "error") ? "Constraint Conflicts" : "Warnings"}
                </div>
                {feasibility.conflicts.map((conflict, idx) => (
                  <div key={idx} className="subtle mb-1">
                    {conflict.severity === "error" ? "❌" : "⚠️"} {conflict.message}
                  </div>
                ))}
              </div>
            ) : null}

            {/* Recommendation Banner (Success) */}
            {feasibility && (!feasibility.conflicts || feasibility.conflicts.length === 0) ? (
              <div className="callout callout--ok">
                <div className="font-bold mb-2">✅ Recommended Configuration</div>
                <div className="subtle mb-2">{feasibility.recommendations.message}</div>
                <div className="subtle">
                  Utilization: {feasibility.recommendations.utilizationStatus} ({feasibility.capacity.requiredRegularSlots} of {feasibility.capacity.availableRegularSlots} regular-season slots)
                </div>
                {feasibility.capacity.guestSlotsReserved > 0 ? (
                  <div className="subtle">
                    Guest games reserve {feasibility.capacity.guestSlotsReserved} slots, leaving {feasibility.capacity.effectiveSlotsRemaining} for regular season
                  </div>
                ) : null}
              </div>
            ) : null}

            <div className="callout">
              <div className="row row--between gap-2" style={{ alignItems: "center" }}>
                <div>
                  <div className="font-bold">Rule presets</div>
                  <div className="subtle">Start from a tuned baseline, then fine-tune individual settings below.</div>
                </div>
                <div className="row row--wrap gap-2">
                  {RULE_PRESETS.map((preset) => (
                    <button
                      key={preset.id}
                      type="button"
                      className="btn btn--ghost"
                      onClick={() => applyRulePreset(preset.id)}
                      title={preset.description}
                    >
                      {preset.label}
                    </button>
                  ))}
                </div>
              </div>
              <div className="subtle mt-2">
                Presets give you a fast starting point. Every field below can still be adjusted manually afterward.
              </div>
            </div>

            <div className="card">
              <div className="card__header">
                <div className="h4">Game targets & weekly limits</div>
                <div className="subtle">Define how many games to build and the caps that keep weeks balanced.</div>
              </div>
              <div className="card__body grid2">
                <label>
                  Min games per team (regular season)
                  <input
                    type="number"
                    min="0"
                    value={minGamesPerTeam}
                    onChange={(e) => setMinGamesPerTeam(e.target.value)}
                  />
                  {feasibility && feasibility.recommendations ? (
                    <div className="subtle text-sm mt-1">
                      Recommended: {feasibility.recommendations.minGamesPerTeam}-{feasibility.recommendations.maxGamesPerTeam}
                    </div>
                  ) : null}
                </label>
                <label>
                  Pool games per team (pool week, min 2)
                  <input
                    type="number"
                    min="2"
                    value={poolGamesPerTeam}
                    onChange={(e) => setPoolGamesPerTeam(e.target.value)}
                  />
                </label>
                <label>
                  Guest games per week
                  <input
                    type="number"
                    min="0"
                    value={guestGamesPerWeek}
                    onChange={(e) => setGuestGamesPerWeek(e.target.value)}
                  />
                  {feasibility && feasibility.recommendations && feasibility.recommendations.optimalGuestGamesPerWeek > 0 ? (
                    <div className="subtle text-sm mt-1">
                      Recommended: {feasibility.recommendations.optimalGuestGamesPerWeek} (helps balance odd team count)
                    </div>
                  ) : null}
                </label>
                <label>
                  Max guest/crossover offers per team (season)
                  <input
                    type="number"
                    min="0"
                    value={maxExternalOffersPerTeamSeason}
                    onChange={(e) => setMaxExternalOffersPerTeamSeason(e.target.value)}
                  />
                  <div className="subtle text-sm mt-1">
                    0 = no cap. Hard rule for external/guest offers in regular season.
                  </div>
                </label>
                <label>
                  Max games per team per week
                  <input
                    type="number"
                    min="0"
                    value={maxGamesPerWeek}
                    onChange={(e) => setMaxGamesPerWeek(e.target.value)}
                  />
                </label>
                <div className="stack gap-2">
                  <label className="inlineCheck">
                    <input type="checkbox" checked={noDoubleHeaders} onChange={(e) => setNoDoubleHeaders(e.target.checked)} />
                    No doubleheaders
                  </label>
                  <label className="inlineCheck">
                    <input type="checkbox" checked={balanceHomeAway} onChange={(e) => setBalanceHomeAway(e.target.checked)} />
                    Balance home/away
                  </label>
                  <div className="stack gap-1">
                    <label className="inlineCheck">
                      <input
                        type="checkbox"
                        checked={blockSpringBreak}
                        onChange={(e) => setBlockSpringBreak(e.target.checked)}
                        disabled={!springBreakRange}
                      />
                      Block Spring Break games {springBreakRange ? `(${springBreakRange.startDate} to ${springBreakRange.endDate})` : "(set season dates first)"}
                    </label>
                    {blockSpringBreak && springBreakRange ? (
                      <div className="subtle text-sm">
                        Slots in this range will be excluded from schedule preview and apply.
                      </div>
                    ) : null}
                  </div>

                  <div className="stack gap-1 mt-2">
                    <div className="font-bold" style={{ fontSize: "0.9rem" }}>Block Common Holidays</div>
                    <div className="subtle mb-1">Automatically exclude these holidays from the schedule</div>
                    {seasonStart ? (
                      <>
                        {getCommonHolidays(Number(seasonStart.split("-")[0])).map((holiday) => (
                          <label key={holiday.label} className="inlineCheck">
                            <input
                              type="checkbox"
                              checked={blockedHolidays.has(holiday.label)}
                              onChange={(e) => {
                                const newSet = new Set(blockedHolidays);
                                if (e.target.checked) {
                                  newSet.add(holiday.label);
                                } else {
                                  newSet.delete(holiday.label);
                                }
                                setBlockedHolidays(newSet);
                              }}
                            />
                            {holiday.label} ({holiday.startDate})
                          </label>
                        ))}
                      </>
                    ) : (
                      <div className="subtle">Set season start date to see available holidays</div>
                    )}
                  </div>
                </div>
              </div>
            </div>
            <div className="card">
              <div className="card__header">
                <div className="h4">League hard stops</div>
                <div className="subtle">Filter out blocked dates and times before the scheduler starts assigning games.</div>
              </div>
              <div className="card__body stack gap-2">
                <label>
                  No games on specific dates (YYYY-MM-DD, comma or newline separated)
                  <textarea
                    rows={3}
                    value={noGamesOnDatesText}
                    onChange={(e) => setNoGamesOnDatesText(e.target.value)}
                    placeholder={"2026-05-24\n2026-05-31"}
                  />
                  <div className="subtle text-sm mt-1">
                    {parsedNoGamesOnDates.values.length} valid date(s) configured.
                  </div>
                </label>
                <div className="grid2">
                  <label>
                    No games before (HH:MM)
                    <input
                      type="time"
                      value={normalizeClockInput(noGamesBeforeTime) || noGamesBeforeTime}
                      onChange={(e) => setNoGamesBeforeTime(e.target.value)}
                    />
                  </label>
                  <label>
                    No games after (HH:MM)
                    <input
                      type="time"
                      value={normalizeClockInput(noGamesAfterTime) || noGamesAfterTime}
                      onChange={(e) => setNoGamesAfterTime(e.target.value)}
                    />
                  </label>
                </div>
                {leagueRuleIssues.length ? (
                  <div className="callout callout--warning">
                    {leagueRuleIssues.slice(0, 2).map((issue, idx) => (
                      <div key={`league-rule-issue-${idx}`} className="subtle">{issue}</div>
                    ))}
                  </div>
                ) : (
                  <div className="subtle text-sm">
                    These rules are enforced as hard stops in Preview/Apply and filter slots before construction.
                  </div>
                )}
              </div>
            </div>
            <div className="card">
              <div className="row items-center justify-between gap-2 mb-2">
                <div>
                  <div className="font-bold">Priority matchups (late-season preference)</div>
                  <div className="subtle">
                    Mark rivalry/high-stakes pairs to bias them toward later regular-season slots. Repeated pairings are already prioritized automatically.
                  </div>
                </div>
                <div className="row gap-2">
                  <button
                    type="button"
                    className="btn btn--ghost"
                    onClick={suggestRivalryMatchups}
                    disabled={normalizedDivisionTeams.length < 2 || (Number(minGamesPerTeam) || 0) <= 0}
                    title={
                      normalizedDivisionTeams.length < 2
                        ? "Need at least two teams in the division."
                        : (Number(minGamesPerTeam) || 0) <= 0
                          ? "Set Min games per team above 0 to auto-suggest."
                          : "Suggest priority matchups from repeated pair demand, then fill with nearby-team pairings"
                    }
                  >
                    Suggest rivalries
                  </button>
                  <button
                    type="button"
                    className="btn btn--ghost"
                    onClick={addRivalryMatchupRow}
                    disabled={rivalryMatchups.length >= MAX_RIVALRY_MATCHUPS || normalizedDivisionTeams.length < 2}
                    title={normalizedDivisionTeams.length < 2 ? "Need at least two teams in the division." : "Add a priority matchup row"}
                  >
                    Add pair
                  </button>
                </div>
              </div>
              {rivalryRowIssues.length ? (
                <div className="callout callout--warning mb-2">
                  <div className="font-bold mb-1">Priority matchup issues</div>
                  {rivalryRowIssues.slice(0, 4).map((issue, idx) => (
                    <div key={`rivalry-issue-${idx}`} className="subtle">{issue}</div>
                  ))}
                  {rivalryRowIssues.length > 4 ? <div className="subtle">Showing first 4 issues.</div> : null}
                </div>
              ) : null}
              {rivalryMatchups.length === 0 ? (
                <div className="subtle">
                  No manual priority matchups configured. Backward scheduling and repeat-pair priority still apply automatically.
                </div>
              ) : (
                <div className="tableWrap">
                  <table className="table table--compact">
                    <thead>
                      <tr>
                        <th>Team A</th>
                        <th>Team B</th>
                        <th>Weight</th>
                        <th></th>
                      </tr>
                    </thead>
                    <tbody>
                      {rivalryMatchups.map((row, idx) => (
                        <tr key={`rivalry-row-${idx}`}>
                          <td>
                            <select
                              value={row?.teamA || ""}
                              onChange={(e) => updateRivalryMatchupRow(idx, { teamA: e.target.value })}
                            >
                              <option value="">Select team</option>
                              {normalizedDivisionTeams.map((team) => (
                                <option key={`rivalry-a-${idx}-${team.teamId}`} value={team.teamId}>
                                  {team.name && team.name !== team.teamId ? `${team.name} (${team.teamId})` : team.teamId}
                                </option>
                              ))}
                            </select>
                          </td>
                          <td>
                            <select
                              value={row?.teamB || ""}
                              onChange={(e) => updateRivalryMatchupRow(idx, { teamB: e.target.value })}
                            >
                              <option value="">Select team</option>
                              {normalizedDivisionTeams.map((team) => (
                                <option key={`rivalry-b-${idx}-${team.teamId}`} value={team.teamId}>
                                  {team.name && team.name !== team.teamId ? `${team.name} (${team.teamId})` : team.teamId}
                                </option>
                              ))}
                            </select>
                          </td>
                          <td style={{ width: 120 }}>
                            <input
                              type="number"
                              min="1"
                              max="10"
                              step="1"
                              value={row?.weight ?? 3}
                              onChange={(e) => updateRivalryMatchupRow(idx, { weight: e.target.value })}
                            />
                          </td>
                          <td style={{ width: 80 }}>
                            <button
                              type="button"
                              className="btn btn--ghost"
                              onClick={() => removeRivalryMatchupRow(idx)}
                            >
                              Remove
                            </button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
              <div className="subtle mt-2">
                Weight 1-10: higher values increase the penalty for placing that matchup early in the regular season.
              </div>
            </div>
            <div className={`callout ${planningIntel.totalShortfall > 0 ? "callout--error" : "callout--ok"}`}>
              <div className="font-bold mb-2">Capacity planner</div>
              <div className="subtle mb-2">
                Live estimate using current slot plan and team count ({planningIntel.teams}). Pool play assumes at least <b>2 games/team</b>.
              </div>
              <div className="subtle mb-2">
                Game-capable slots: <b>{planningIntel.totalGameCapableSlotsAvailable}</b> (game: {planningIntel.totalGameOnlySlots}, both: {planningIntel.totalBothSlots}) |
                Practice-only slots: <b>{planningIntel.totalPracticeSlotsAvailable}</b> ({planningIntel.totalPracticeOnlySlots} total tagged practice)
              </div>
              <div className="subtle mb-2">
                Regular season only schedules from game-capable slots. Pool + bracket can use all slot types, with practice fallback available after game/both slots.
              </div>
              <div className="subtle mb-2">
                Regular-season game slots per week: <b>avg {planningIntel.avgGameSlotsPerWeek.toFixed(1)}</b>, <b>max {planningIntel.maxGameSlotsPerWeek}</b>,
                <b> min {planningIntel.minGameSlotsPerWeek}</b> across {planningIntel.regularWeeksCount} week{planningIntel.regularWeeksCount === 1 ? "" : "s"}.
              </div>
              <div className="subtle mb-2">
                Max games supported in a week: <b>{planningIntel.maxGamesSupportedPerWeek}</b>
                {planningIntel.teamRuleGamesCapPerWeek == null
                  ? ` (slot capacity only; max games/team/week is currently unlimited).`
                  : ` (min of slot max ${planningIntel.maxGameSlotsPerWeek} and team-rule cap ${planningIntel.teamRuleGamesCapPerWeek}).`}
              </div>
              <div className="tableWrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Phase</th>
                      <th>Available slots</th>
                      <th>Min target slots</th>
                      <th>Gap</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr>
                      <td>Regular season</td>
                      <td>{planningIntel.regularSlotsAvailable}</td>
                      <td>{planningIntel.regularRequiredMinimum}</td>
                      <td>{planningIntel.regularShortfall > 0 ? `-${planningIntel.regularShortfall}` : "OK"}</td>
                    </tr>
                    <tr>
                      <td>
                        Pool play
                        <div className="subtle">Practice fallback: {planningIntel.poolPracticeFallbackSlots}</div>
                      </td>
                      <td>{planningIntel.poolSlotsAvailable}</td>
                      <td>{planningIntel.poolRequiredSlots}</td>
                      <td>{planningIntel.poolShortfall > 0 ? `-${planningIntel.poolShortfall}` : "OK"}</td>
                    </tr>
                    <tr>
                      <td>
                        Bracket
                        <div className="subtle">Practice fallback: {planningIntel.bracketPracticeFallbackSlots}</div>
                      </td>
                      <td>{planningIntel.bracketSlotsAvailable}</td>
                      <td>{planningIntel.bracketRequiredSlots}</td>
                      <td>{planningIntel.bracketShortfall > 0 ? `-${planningIntel.bracketShortfall}` : "OK"}</td>
                    </tr>
                    <tr>
                      <td><b>Total</b></td>
                      <td><b>{planningIntel.totalPhaseSlotsAvailable}</b></td>
                      <td><b>{planningIntel.totalRequiredSlotsMinimum}</b></td>
                      <td><b>{planningIntel.totalShortfall > 0 ? `-${planningIntel.totalShortfall}` : "OK"}</b></td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div className="subtle mt-2">
                Minimum target responds to every game-count change. Round-robin cycle estimate: {planningIntel.regularRequiredCycleSlots} regular-season slots
                ({planningIntel.roundRobinRounds} cycle{planningIntel.roundRobinRounds === 1 ? "" : "s"} of {planningIntel.roundRobinMatchups} matchup slots each).
              </div>
              {planningIntel.totalCycleShortfall > 0 ? (
                <div className="subtle">
                  Cycle-model shortfall: {planningIntel.totalCycleShortfall} slot(s). This is stricter than the minimum model.
                </div>
              ) : (
                <div className="subtle">
                  Cycle-model capacity also fits current targets.
                </div>
              )}
              <div className="subtle">
                If 10 to 9 does not change cycle estimate, that means both values are in the same round-robin cycle band ({planningIntel.gamesPerTeamRound} games/team per full cycle).
              </div>
              {planningIntel.blockedOutSlots > 0 ? (
                <div className="subtle">
                  {planningIntel.blockedOutSlots} slot(s) excluded by blocked date ranges.
                </div>
              ) : null}
            </div>
            {Number(guestGamesPerWeek) > 0 ? (
              <div className="callout">
                Guest games are enabled. Anchor options from Slot plan are used first each week; otherwise highest-priority remaining slots are used.
              </div>
            ) : null}
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(2)}>
              Back
            </button>
            <button
              className="btn btn--primary"
              type="button"
              onClick={runPreview}
              disabled={loading || !!rulesError}
              title={rulesError || 'Generate a preview to unlock the final step.'}
            >
              {loading ? "Previewing..." : "Preview schedule"}
            </button>
          </div>
        </div>
      ) : null}

      {step === 4 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Preview & apply</div>
            <div className="subtle">Review assignments and apply when ready.</div>
          </div>
          <div className="card__body stack gap-3">
            {!preview ? (
              <div className="muted">Run a preview to see results.</div>
            ) : (
              <>
                <div className="card">
                  <div className="card__header">
                    <div className="h4">Preview overview</div>
                    <div className="subtle">Start here. Check readiness, then move through fixes, coverage, and assignments below.</div>
                  </div>
                  <div className="card__body stack gap-3">
                    <div className="layoutStatRow">
                      <div className="layoutStat">
                        <div className="layoutStat__value">{preview.summary?.regularSeason?.slotsAssigned ?? 0}</div>
                        <div className="layoutStat__label">Regular season games</div>
                      </div>
                      <div className="layoutStat">
                        <div className="layoutStat__value">{preview.summary?.poolPlay?.slotsAssigned ?? 0}</div>
                        <div className="layoutStat__label">Pool play games</div>
                      </div>
                      <div className="layoutStat">
                        <div className="layoutStat__value">{preview.summary?.bracket?.slotsAssigned ?? 0}</div>
                        <div className="layoutStat__label">Bracket games</div>
                      </div>
                      <div className="layoutStat">
                        <div className="layoutStat__value">{preview.summary?.totalSlots ?? 0}</div>
                        <div className="layoutStat__label">Game-capable slots</div>
                      </div>
                    </div>
                    <div className={(previewApplyBlocked || previewIssueCount > 0) ? "callout callout--warning" : "callout callout--ok"}>
                      <div className="font-bold mb-2">At a glance</div>
                      <div className="row row--wrap gap-2">
                        <span className="pill">{previewApplyBlocked ? "Apply blocked" : "Apply ready"}</span>
                        <span className="pill">Issues: {previewIssueCount}</span>
                        <span className="pill">Warnings: {previewWarningCount}</span>
                        <span className="pill">Repairs: {previewRepairProposals.length}</span>
                        <span className="pill">Recommendations: {previewRecommendations.length}</span>
                        <span className="pill">Assignments: {previewAssignmentCount}</span>
                      </div>
                      <div className="subtle mt-2">
                        {previewApplyBlocked || previewIssueCount > 0
                          ? "Start with Health & fixes, then review the coverage sections before applying."
                          : "No blocking issues are currently reported. Review coverage and assignments, then apply if the schedule looks right."}
                      </div>
                      <div className="subtle mt-1">
                        The sections below are collapsible so you can focus on one review track at a time.
                      </div>
                    </div>
                  </div>
                </div>

                <div className="row row--wrap gap-2">
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={() => setAllPreviewSectionsExpanded(true)}
                  >
                    Expand all
                  </button>
                  <button
                    className="btn btn--ghost"
                    type="button"
                    onClick={() => setAllPreviewSectionsExpanded(false)}
                  >
                    Collapse all
                  </button>
                </div>

                <CollapsibleSection
                  title="Health & fixes"
                  subtitle="Use this section to triage rule conflicts, repairs, and preview diagnostics."
                  defaultExpanded={previewApplyBlocked || previewIssueCount > 0 || previewRepairProposals.length > 0}
                  storageKey={PREVIEW_SECTION_STORAGE_KEYS.health}
                  isExpanded={previewSectionControl.expanded.health}
                  onChange={(isExpanded) => handlePreviewSectionToggle("health", isExpanded)}
                >
                  <div className="stack gap-3">

              {previewRuleHealth ? (
                <div
                  className={
                    previewRuleHealth.status === "red"
                      ? "callout callout--error"
                      : (previewRuleHealth.status === "yellow" ? "callout callout--warning" : "callout")
                  }
                >
                  <div className="font-bold mb-2">Rule Health</div>
                  <div className="subtle">
                    Status: <b>{String(previewRuleHealth.status || "").toUpperCase() || "-"}</b> | Hard:{" "}
                    <b>{Number(previewRuleHealth.hardViolationCount || 0)}</b> | Soft:{" "}
                    <b>{Number(previewRuleHealth.softViolationCount || 0)}</b> | Soft score:{" "}
                    <b>{Number(previewRuleHealth.softScore || 0)}</b>
                    {previewApplyBlocked ? " | Apply is blocked until hard violations are resolved." : ""}
                  </div>
                  <div className="subtle">
                    Strategy: <b>{preview.constructionStrategy || "legacy_greedy_v1"}</b> | Seed: <b>{preview.seed ?? "-"}</b>
                  </div>
                  {selectedRuleFocusScope ? (
                    <div className="callout mt-2">
                      <div className="row row--between gap-2" style={{ alignItems: "center" }}>
                        <div>
                          <div className="font-bold" style={{ fontSize: "0.95rem" }}>Focused rule: {selectedRuleFocusScope.ruleId}</div>
                          <div className="subtle">
                            Severity: {selectedRuleFocusScope.severity || "-"} | Teams: {selectedRuleFocusScope.teamIds.length} | Weeks: {selectedRuleFocusScope.weekKeys.length} | Slots: {selectedRuleFocusScope.slotIds.length}
                          </div>
                        </div>
                        <button
                          type="button"
                          className="btn btn--ghost"
                          onClick={() => setSelectedRuleFocusKey("")}
                        >
                          Clear focus
                        </button>
                      </div>
                    </div>
                  ) : null}
                  {Array.isArray(previewRuleHealth.groups) && previewRuleHealth.groups.length ? (
                    <div className="tableWrap mt-2">
                      <table className="table">
                        <thead>
                          <tr>
                            <th>Rule</th>
                            <th>Severity</th>
                            <th>Count</th>
                            <th>Summary</th>
                            <th>Focus</th>
                          </tr>
                        </thead>
                        <tbody>
                          {previewRuleHealth.groups.slice(0, 12).map((g, idx) => {
                            const focusKey = `${String(g?.severity || "").toLowerCase()}:${String(g?.ruleId || "")}`;
                            const isFocused = !!selectedRuleFocusKey && selectedRuleFocusKey === focusKey;
                            return (
                            <tr
                              key={`rule-health-group-${g.ruleId || idx}-${idx}`}
                              style={isFocused ? { backgroundColor: "#fffbeb" } : undefined}
                            >
                              <td>{g.ruleId || ""}</td>
                              <td>{g.severity || ""}</td>
                              <td>{g.count || 0}</td>
                              <td>{g.summary || ""}</td>
                              <td>
                                <button
                                  type="button"
                                  className="btn btn--ghost"
                                  onClick={() => {
                                    setSelectedRepairProposalId("");
                                    setSelectedRuleFocusKey((prev) => (prev === focusKey ? "" : focusKey));
                                  }}
                                >
                                  {isFocused ? "Hide" : "Focus"}
                                </button>
                              </td>
                            </tr>
                          );})}
                        </tbody>
                      </table>
                      {previewRuleHealth.groups.length > 12 ? (
                        <div className="subtle mt-2">Showing first 12 rule groups.</div>
                      ) : null}
                    </div>
                  ) : null}
                </div>
              ) : null}

              {previewRepairProposals.length ? (
                <div className="callout">
                  <div className="font-bold mb-2">Repair proposals</div>
                  <div className="subtle mb-2">
                    Ranked minimal-change proposals to reduce hard rule violations. Manual-action proposals require changing rules/capacity and rerunning preview.
                  </div>
                  <div className="subtle mb-2">
                    Priority badge is a tie-break hint only: prefer <b>Priority +</b> proposals when hard-fix impact is otherwise similar.
                  </div>
                  {selectedRepairScope ? (
                    <div className="callout callout--warning mb-2">
                      <div className="row row--between gap-2" style={{ alignItems: "center" }}>
                        <div>
                          <div className="font-bold" style={{ fontSize: "0.95rem" }}>Showing affected games: {selectedRepairScope.title || selectedRepairScope.proposalId}</div>
                          <div className="subtle">
                            Rules: {(selectedRepairScope.ruleIds || []).join(", ") || "-"} | Teams: {(selectedRepairScope.teamIds || []).length} | Weeks: {(selectedRepairScope.weekKeys || []).length} | Slots: {(selectedRepairScope.slotIds || []).length}
                          </div>
                        </div>
                        <button
                          type="button"
                          className="btn btn--ghost"
                          onClick={() => setSelectedRepairProposalId("")}
                        >
                          Clear highlights
                        </button>
                      </div>
                      {selectedRepairScope.moveSummaries?.length ? (
                        <div className="subtle mt-1">
                          {selectedRepairScope.moveSummaries.slice(0, 3).map((move) => {
                            const from = move?.from;
                            const after = move?.after;
                            const fromLabel = from ? `${from.gameDate || "?"} ${from.startTime || "?"}-${from.endTime || "?"} ${from.fieldKey || ""}`.trim() : "?";
                            const toLabel = after ? `${after.gameDate || "?"} ${after.startTime || "?"}-${after.endTime || "?"} ${after.fieldKey || ""}`.trim() : "?";
                            return `${fromLabel} -> ${toLabel}`;
                          }).join(" | ")}
                          {selectedRepairScope.moveSummaries.length > 3 ? ` | +${selectedRepairScope.moveSummaries.length - 3} more move(s)` : ""}
                        </div>
                      ) : null}
                      {selectedRepairPriorityImpact ? (
                        <div className={`subtle mt-1 ${selectedRepairPriorityImpact.hasManualEarlier || selectedRepairPriorityImpact.hasRepeatEarlier ? "" : ""}`}>
                          {selectedRepairPriorityBadge ? (
                            <span
                              className="pill mr-2"
                              title={selectedRepairPriorityBadge.title}
                              style={repairPriorityBadgeStyle(selectedRepairPriorityBadge.tone)}
                            >
                              {selectedRepairPriorityBadge.label}
                            </span>
                          ) : null}
                          Priority pair impact: {selectedRepairPriorityImpact.summary || "priority pair moves detected."}
                          {selectedRepairPriorityImpact.pairDetails?.length ? (
                            <> | {selectedRepairPriorityImpact.pairDetails.join(" | ")}</>
                          ) : null}
                        </div>
                      ) : null}
                    </div>
                  ) : null}
                  <div className="tableWrap">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Proposal</th>
                          <th>Rules</th>
                          <th>Impact</th>
                          <th>Type</th>
                          <th>Rationale</th>
                        </tr>
                      </thead>
                      <tbody>
                        {previewRepairProposals.slice(0, 10).map((p, idx) => {
                          const proposalId = String(p?.proposalId || "");
                          const rules = Array.isArray(p?.fixesRuleIds) ? p.fixesRuleIds : [];
                          const changes = Array.isArray(p?.changes) ? p.changes : [];
                          const canApplyPreviewFix = !p?.requiresUserAction && changes.some((c) => String(c?.changeType || "").toLowerCase() === "move");
                          const isApplyingThis = repairApplyingId && String(p?.proposalId || "") === repairApplyingId;
                          const isSelectedProposal = !!selectedRepairProposalId && proposalId === selectedRepairProposalId;
                          const hardResolved = Number(p?.hardViolationsResolved || 0);
                          const hardRemaining = Number(p?.hardViolationsRemaining || 0);
                          const gamesMoved = Number(p?.gamesMoved || 0);
                          const teamsTouched = Number(p?.teamsTouched || 0);
                          const weeksTouched = Number(p?.weeksTouched || 0);
                          const proposalScope = buildRepairProposalScope(p);
                          const proposalPriorityImpact = summarizeRepairProposalPriorityImpact(proposalScope, priorityPairInfoByKey);
                          const proposalPriorityBadge = classifyRepairPriorityImpact(proposalPriorityImpact);
                          return (
                            <tr
                              key={`repair-proposal-${p?.proposalId || idx}-${idx}`}
                              style={isSelectedProposal ? { backgroundColor: "#fffbeb", outline: "1px solid #fcd34d" } : undefined}
                            >
                              <td>
                                <div>{p?.title || "Proposal"}</div>
                                {proposalPriorityBadge ? (
                                  <div className="mt-1">
                                    <span
                                      className="pill"
                                      title={proposalPriorityBadge.title}
                                      style={repairPriorityBadgeStyle(proposalPriorityBadge.tone)}
                                    >
                                      {proposalPriorityBadge.label}
                                    </span>
                                  </div>
                                ) : null}
                                <div className="subtle">
                                  Hard fix: {hardResolved} | Remaining: {hardRemaining}
                                </div>
                                {Array.isArray(p?.changes) && p.changes.length ? (
                                  <div className="subtle">
                                    {p.changes[0]?.changeType || "change"}{p.changes.length > 1 ? ` (+${p.changes.length - 1})` : ""}
                                  </div>
                                ) : null}
                              </td>
                              <td>{rules.length ? rules.join(", ") : "-"}</td>
                              <td>
                                {gamesMoved} game{gamesMoved === 1 ? "" : "s"} moved
                                <div className="subtle">{teamsTouched} team(s), {weeksTouched} week(s)</div>
                              </td>
                              <td>{p?.requiresUserAction ? "Manual action" : "Move/swap"}</td>
                              <td>
                                <div>{p?.rationale || ""}</div>
                                {proposalPriorityImpact ? (
                                  <div className="subtle mt-1">
                                    Priority pair impact: {proposalPriorityImpact.summary || "priority pair moves detected."}
                                  </div>
                                ) : null}
                                <div className="mt-1 row row--wrap gap-2">
                                  <button
                                    type="button"
                                    className="btn btn--ghost"
                                    onClick={() => {
                                      setSelectedRuleFocusKey("");
                                      setSelectedRepairProposalId((prev) => (prev === proposalId ? "" : proposalId));
                                    }}
                                    disabled={!proposalId}
                                  >
                                    {isSelectedProposal ? "Hide affected" : "Show affected games"}
                                  </button>
                                  {canApplyPreviewFix ? (
                                    <button
                                      type="button"
                                      className="btn btn--ghost"
                                      onClick={() => applyPreviewRepairProposal(p)}
                                      disabled={!!repairApplyingId}
                                    >
                                      {isApplyingThis ? "Applying fix..." : "Apply Fix (Preview)"}
                                    </button>
                                  ) : null}
                                </div>
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                    {previewRepairProposals.length > 10 ? (
                      <div className="subtle mt-2">Showing first 10 proposals.</div>
                    ) : null}
                  </div>
                </div>
              ) : null}

              {preview.warnings?.length ? (
                <div className="callout">
                  {preview.warnings.map((w, idx) => (
                    <div key={idx} className="subtle">{w.message}</div>
                  ))}
                </div>
              ) : null}
              {previewRecommendations.length ? (
                <div className="callout callout--warning">
                  <div className="font-bold mb-2">Preview recommendations</div>
                  <div className="stack gap-2">
                    {previewRecommendations.map((rec, idx) => (
                      <div key={`${rec.code || "rec"}-${idx}`}>
                        <div className="subtle">{rec.message}</div>
                        {Number(rec.suggestedGuestGamesPerWeek) > 0 ? (
                          <div className="row gap-2 mt-1">
                            <button
                              className="btn btn--ghost"
                              type="button"
                              onClick={() => {
                                setGuestGamesPerWeek(String(rec.suggestedGuestGamesPerWeek));
                                setPreview(null);
                                setStep(3);
                              }}
                            >
                              Set Guest games/week = {rec.suggestedGuestGamesPerWeek}
                            </button>
                          </div>
                        ) : null}
                        {Number(rec.suggestedMaxGamesPerWeek) > 0 ? (
                          <div className="row gap-2 mt-1">
                            <button
                              className="btn btn--ghost"
                              type="button"
                              onClick={() => {
                                setMaxGamesPerWeek(String(rec.suggestedMaxGamesPerWeek));
                                setPreview(null);
                                setStep(3);
                              }}
                            >
                              Set Max games/week = {rec.suggestedMaxGamesPerWeek}
                            </button>
                          </div>
                        ) : null}
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
              {planningChecksReport.length ? (
                <div className="callout">
                  <div className="font-bold mb-2">Planning checks report</div>
                  <div className="tableWrap">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Check</th>
                          <th>Scope</th>
                          <th>Result</th>
                          <th>Details</th>
                        </tr>
                      </thead>
                      <tbody>
                        {planningChecksReport.map((row, idx) => (
                          <tr key={`${row.check}-${idx}`}>
                            <td>{row.check}</td>
                            <td>{row.scope}</td>
                            <td>{row.result}</td>
                            <td>{row.details}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : null}
              {preview.issues?.length ? (
                <div className="callout callout--error">
                  <div className="font-bold mb-2">Schedule rule issues ({preview.totalIssues || preview.issues.length})</div>
                  <div className="tableWrap">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Phase</th>
                          <th>Rule</th>
                          <th>Severity</th>
                          <th>Message</th>
                          <th>Hint</th>
                        </tr>
                      </thead>
                      <tbody>
                        {preview.issues.map((issue, idx) => (
                          <tr
                            key={`${getIssuePhase(issue)}-${issue.ruleId || "issue"}-${idx}`}
                            style={activeHighlightLookup && activeHighlightLookup.ruleIds.has(String(issue?.ruleId || "")) ? { backgroundColor: "#fffbeb" } : undefined}
                          >
                            <td>{getIssuePhase(issue)}</td>
                            <td>{issue.ruleId || ""}</td>
                            <td>{issue.severity || ""}</td>
                            <td>{issue.message || ""}</td>
                            <td>{buildIssueHint(issue, preview.summary)}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : null}
              {preview.summary ? (
                <div className="callout">
                  <div className="font-bold mb-2">Why issues happen</div>
                  {buildContextNotes(preview.summary, preview.issues).length ? (
                    <div className="stack gap-1">
                      {buildContextNotes(preview.summary, preview.issues).map((note, idx) => (
                        <div key={idx} className="subtle">{note}</div>
                      ))}
                    </div>
                  ) : (
                    <div className="subtle">No extra context available for these issues.</div>
                  )}
                </div>
              ) : null}
                  </div>
                </CollapsibleSection>

                <CollapsibleSection
                  title="Coverage & balance"
                  subtitle="Review phase coverage, matchup balance, and where the schedule is still thin."
                  defaultExpanded={!previewApplyBlocked && previewIssueCount === 0}
                  storageKey={PREVIEW_SECTION_STORAGE_KEYS.coverage}
                  isExpanded={previewSectionControl.expanded.coverage}
                  onChange={(isExpanded) => handlePreviewSectionToggle("coverage", isExpanded)}
                >
                  <div className="stack gap-3">
              {preview.summary ? (
                <div className="callout">
                  <div className="font-bold mb-2">Scheduling context</div>
                  <div className="subtle">Teams: {preview.summary.teamCount || 0} {preview.summary.teamCount % 2 === 1 ? "(odd team count adds byes)" : ""}</div>
                  <div className="tableWrap mt-2">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Phase</th>
                          <th>Slots</th>
                          <th>Matchups</th>
                          <th>Assigned</th>
                          <th>Unassigned</th>
                        </tr>
                      </thead>
                      <tbody>
                        {[preview.summary.regularSeason, preview.summary.poolPlay, preview.summary.bracket].map((phase) => (
                          <tr key={phase.phase}>
                            <td>{phase.phase}</td>
                            <td>{phase.slotsTotal}</td>
                            <td>{phase.matchupsTotal}</td>
                            <td>{phase.slotsAssigned}</td>
                            <td>{phase.unassignedMatchups}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </div>
              ) : null}
              {regularBalanceReport.teams.length ? (
                <div className="callout">
                  <div className="font-bold mb-2">Regular-season balance matrix</div>
                  <div className="subtle mb-2">
                    Cell format: <b>assigned/target</b> with row-team home/away in parentheses. Example: <code>2/3 (1H/1A)</code>.
                  </div>
                  <div className="subtle mb-2">
                    Guest games are tracked separately and are not counted in the matchup matrix.
                  </div>
                  <div className="subtle mb-2">
                    Priority markers: <b>M</b> = manual priority matchup, <b>R</b> = auto repeat-pair priority.
                    {" "}Manual pairs: <b>{regularBalanceReport.manualPriorityPairCount || 0}</b>.
                    {" "}Repeat-priority pairs: <b>{regularBalanceReport.autoRepeatPriorityPairCount || 0}</b>.
                  </div>

                  <div className="tableWrap mt-2">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Team</th>
                          <th>Games</th>
                          <th>Home</th>
                          <th>Away</th>
                          <th>Guest</th>
                          <th>Unassigned</th>
                          <th>Target</th>
                          <th>Coverage</th>
                          <th>H/A gap</th>
                          <th>BYE weeks*</th>
                        </tr>
                      </thead>
                      <tbody>
                        {regularBalanceReport.teamRows.map((row) => (
                          <tr key={`regular-balance-team-${row.teamId}`}>
                            <td>{row.teamId}</td>
                            <td>{row.games}</td>
                            <td>{row.home}</td>
                            <td>{row.away}</td>
                            <td>{row.guest}</td>
                            <td>{row.unassigned}</td>
                            <td>{row.target}</td>
                            <td>{row.coveragePct}%</td>
                            <td>{row.homeAwayGap}</td>
                            <td>{row.byeWeeks}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {regularBalanceReport.weekCount > 0 ? (
                    <div className="subtle mt-2">
                      *BYE weeks estimate is based on regular-season weeks with at least one regular slot in the preview ({regularBalanceReport.weekCount} week{regularBalanceReport.weekCount === 1 ? "" : "s"}).
                    </div>
                  ) : null}

                  <div className={`tableWrap mt-3 ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                    <table className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}>
                      <thead>
                        <tr>
                          <th>Team</th>
                          {regularBalanceReport.teams.map((teamId) => (
                            <th key={`matrix-col-${teamId}`}>{teamId}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {regularBalanceReport.matrixRows.map((row) => (
                          <tr key={`matrix-row-${row.teamId}`}>
                            <th>{row.teamId}</th>
                            {row.cells.map((cell, idx) => {
                              const colTeamId = regularBalanceReport.teams[idx];
                              if (!cell) return <td key={`matrix-cell-${row.teamId}-${colTeamId}`}>-</td>;
                              const priorityTags = [];
                              if ((cell.manualPriorityWeight || 0) > 0) priorityTags.push(`M${cell.manualPriorityWeight}`);
                              if (cell.autoRepeatPriority) priorityTags.push("R");
                              const titleBits = [`${row.teamId} vs ${colTeamId}`];
                              if (priorityTags.length) titleBits.push(`Priority: ${priorityTags.join(" + ")}`);
                              return (
                                <td key={`matrix-cell-${row.teamId}-${colTeamId}`} title={titleBits.join(" | ")}>
                                  {cell.assigned}/{cell.target}
                                  {priorityTags.length ? (
                                    <div className="subtle" style={{ whiteSpace: "nowrap" }}>
                                      [{priorityTags.join("+")}]
                                    </div>
                                  ) : null}
                                  <div className="subtle" style={{ whiteSpace: "nowrap" }}>
                                    ({cell.homeForRow}H/{cell.awayForRow}A)
                                  </div>
                                </td>
                              );
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  {regularBalanceReport.pairRows.length ? (
                    <div className="tableWrap mt-3">
                      <table className="table">
                        <thead>
                          <tr>
                            <th>Pair</th>
                            <th>Assigned</th>
                            <th>Target</th>
                            <th>Unassigned</th>
                            <th>Home split</th>
                            <th>Priority</th>
                          </tr>
                        </thead>
                        <tbody>
                          {regularBalanceReport.pairRows.slice(0, 40).map((pair) => (
                            <tr key={`pair-row-${pair.key}`}>
                              <td>{pair.teamA} vs {pair.teamB}</td>
                              <td>{pair.assigned}</td>
                              <td>{pair.target}</td>
                              <td>{pair.unassigned}</td>
                              <td>{pair.teamA}:{pair.homeForA} / {pair.teamB}:{pair.homeForB}</td>
                              <td>
                                {pair.priorityLabel ? (
                                  <span className="pill">{pair.priorityLabel}</span>
                                ) : (
                                  <span className="subtle">-</span>
                                )}
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      {regularBalanceReport.pairRows.length > 40 ? (
                        <div className="subtle mt-2">Showing first 40 pair rows.</div>
                      ) : null}
                    </div>
                  ) : null}
                </div>
              ) : null}
              {teamLanesReport.weekKeys.length && teamLanesReport.lanes.length ? (
                <div className="callout">
                  <div className="font-bold mb-2">Team lanes (Regular Season)</div>
                  <div className="subtle mb-2">
                    Each team is a lane across regular-season weeks. <b>Green = 1 game</b>, <b>red = 0 games</b>, <b>amber = 2+</b>. Late-season weeks are on the right.
                  </div>
                  <div className={`tableWrap ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                    <table className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}>
                      <thead>
                        <tr>
                          <th>Team</th>
                          <th>0w</th>
                          <th>1w</th>
                          <th>2+w</th>
                          <th>Longest idle</th>
                          {teamLanesReport.weekKeys.map((weekKey, idx) => (
                            <th key={`lane-week-${weekKey}`} title={weekKey}>
                              W{idx + 1}
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {teamLanesReport.lanes.map((lane) => (
                          <tr key={`team-lane-${lane.teamId}`}>
                            <td>
                              <div>{lane.teamId}</div>
                              <div className="subtle">{lane.games} reg / {lane.guest} guest</div>
                            </td>
                            <td>{lane.zeroWeeks}</td>
                            <td>{lane.singleWeeks}</td>
                            <td>{lane.multiWeeks}</td>
                            <td>{lane.longestIdleGap}</td>
                            {lane.cells.map((cell, idx) => {
                              const lateFactor = teamLanesReport.weekKeys.length > 1 ? (idx / (teamLanesReport.weekKeys.length - 1)) : 0;
                              const isRepairHighlighted = isTeamWeekHighlightedByRepair(lane.teamId, cell.weekKey);
                              const dragAssignment = cell?.dragAssignment || null;
                              const dragKey = dragAssignment ? assignmentExplainKey(dragAssignment) : "";
                              const isDragEligible = !!dragAssignment && canDragSwapAssignment(dragAssignment);
                              const isDragSource = !!dragKey && dragSwapSourceKey === dragKey;
                              const isDragTarget = !!dragKey && dragSwapTargetKey === dragKey && dragSwapSourceKey && dragSwapSourceKey !== dragKey;
                              const base =
                                cell.status === "gap"
                                  ? { bg: `rgba(239,68,68,${0.12 + (lateFactor * 0.08)})`, border: "#fecaca", text: "#991b1b" }
                                  : cell.status === "single"
                                    ? { bg: `rgba(34,197,94,${0.12 + (lateFactor * 0.08)})`, border: "#bbf7d0", text: "#166534" }
                                    : { bg: `rgba(245,158,11,${0.14 + (lateFactor * 0.08)})`, border: "#fde68a", text: "#92400e" };
                              const label = cell.total > 0 ? String(cell.total) : "0";
                              const detail = `${lane.teamId} • ${cell.weekKey} • total ${cell.total} (regular ${cell.regular}, guest ${cell.guest})`;
                              return (
                                <td
                                  key={`lane-cell-${lane.teamId}-${cell.weekKey}`}
                                  title={detail}
                                  draggable={isDragEligible}
                                  onDragStart={isDragEligible ? (event) => handleAssignmentDragStart(event, dragAssignment) : undefined}
                                  onDragOver={isDragEligible ? (event) => handleAssignmentDragOver(event, dragAssignment) : undefined}
                                  onDrop={isDragEligible ? (event) => handleAssignmentDrop(event, dragAssignment) : undefined}
                                  onDragEnd={isDragEligible ? clearAssignmentDragSwap : undefined}
                                  style={{
                                    textAlign: "center",
                                    minWidth: 42,
                                    background: base.bg,
                                    borderColor: isDragTarget ? "#7c3aed" : (isRepairHighlighted ? "#f59e0b" : base.border),
                                    boxShadow:
                                      isDragSource
                                        ? "inset 0 0 0 2px rgba(37,99,235,0.45)"
                                        : (isDragTarget
                                          ? "inset 0 0 0 2px rgba(124,58,237,0.45)"
                                          : (isRepairHighlighted ? "inset 0 0 0 2px rgba(245,158,11,0.45)" : "none")),
                                    color: base.text,
                                    fontWeight: 700,
                                    cursor: isDragEligible ? "move" : "default",
                                  }}
                                >
                                  {label}
                                  {cell.guest > 0 ? <div className="subtle" style={{ fontSize: "0.7rem" }}>G{cell.guest}</div> : null}
                                  {isDragEligible ? <div className="subtle" style={{ fontSize: "0.65rem" }}>swap</div> : null}
                                </td>
                              );
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {teamLanesReport.totalTeams > teamLanesReport.shownTeams ? (
                    <div className="subtle mt-2">
                      Showing {teamLanesReport.shownTeams} of {teamLanesReport.totalTeams} teams (prioritized by zero-game weeks and longest idle gaps).
                    </div>
                  ) : null}
                </div>
              ) : null}
              {fieldHeatmapReport.weekKeys.length && fieldHeatmapReport.rows.length ? (
                <div className="callout">
                  <div className="font-bold mb-2">Field utilization heatmap (Regular Season)</div>
                  <div className="subtle mb-2">
                    Field x week capacity. Cells show <b>used/capacity</b>; darker cells indicate higher utilization. Late-season weeks are on the right.
                  </div>
                  <div className="subtle mb-2">
                    Heatmap cells with exactly one regular game can be dragged onto another single-game heatmap cell to try a preview swap.
                  </div>
                  <div className={`tableWrap ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                    <table className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}>
                      <thead>
                        <tr>
                          <th>Field</th>
                          <th>Total</th>
                          <th>Unused</th>
                          {fieldHeatmapReport.weekKeys.map((weekKey, idx) => (
                            <th key={`heat-week-${weekKey}`} title={weekKey}>
                              W{idx + 1}
                            </th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {fieldHeatmapReport.rows.map((row) => (
                          <tr key={`heat-row-${row.fieldKey}`}>
                            <td>{row.fieldKey}</td>
                            <td>{row.totalUsed}/{row.totalCapacity}</td>
                            <td>{row.totalUnused}</td>
                            {row.cells.map((cell, idx) => {
                              const lateFactor = fieldHeatmapReport.weekKeys.length > 1 ? (idx / (fieldHeatmapReport.weekKeys.length - 1)) : 0;
                              const isRepairHighlighted = isFieldWeekHighlightedByRepair(row.fieldKey, cell.weekKey);
                              const dragAssignment = cell?.dragAssignment || null;
                              const dragKey = dragAssignment ? assignmentExplainKey(dragAssignment) : "";
                              const isDragEligible = !!dragAssignment && canDragSwapAssignment(dragAssignment);
                              const isDragSource = !!dragKey && dragSwapSourceKey === dragKey;
                              const isDragTarget = !!dragKey && dragSwapTargetKey === dragKey && dragSwapSourceKey && dragSwapSourceKey !== dragKey;
                              const util = cell.capacity > 0 ? (cell.used / cell.capacity) : 0;
                              const alpha = cell.capacity > 0 ? (0.08 + (util * 0.38) + (lateFactor * 0.08)) : 0.03;
                              const hueColor = cell.capacity === 0
                                ? "rgba(148,163,184,0.08)"
                                : util >= 0.9
                                  ? `rgba(15,118,110,${Math.min(0.62, alpha)})`
                                  : util >= 0.5
                                    ? `rgba(59,130,246,${Math.min(0.54, alpha)})`
                                    : `rgba(148,163,184,${Math.min(0.32, alpha)})`;
                              const label = cell.capacity > 0 ? `${cell.used}/${cell.capacity}` : "-";
                              const detail = `${row.fieldKey} • ${cell.weekKey} • used ${cell.used}/${cell.capacity}` +
                                (cell.guest ? ` • guest ${cell.guest}` : "") +
                                (cell.regular ? ` • regular ${cell.regular}` : "");
                              return (
                                <td
                                  key={`heat-cell-${row.fieldKey}-${cell.weekKey}`}
                                  title={detail}
                                  draggable={isDragEligible}
                                  onDragStart={isDragEligible ? (event) => handleAssignmentDragStart(event, dragAssignment) : undefined}
                                  onDragOver={isDragEligible ? (event) => handleAssignmentDragOver(event, dragAssignment) : undefined}
                                  onDrop={isDragEligible ? (event) => handleAssignmentDrop(event, dragAssignment) : undefined}
                                  onDragEnd={isDragEligible ? clearAssignmentDragSwap : undefined}
                                  style={{
                                    textAlign: "center",
                                    minWidth: 46,
                                    background: hueColor,
                                    boxShadow:
                                      isDragSource
                                        ? "inset 0 0 0 2px rgba(37,99,235,0.45)"
                                        : (isDragTarget
                                          ? "inset 0 0 0 2px rgba(124,58,237,0.45)"
                                          : (isRepairHighlighted ? "inset 0 0 0 2px rgba(245,158,11,0.6)" : "none")),
                                    color: cell.capacity > 0 && util >= 0.75 ? "#ffffff" : "inherit",
                                    fontWeight: cell.capacity > 0 ? 600 : 400,
                                    cursor: isDragEligible ? "move" : "default",
                                  }}
                                >
                                  {label}
                                  {cell.guest > 0 ? <div className="subtle" style={{ fontSize: "0.7rem", color: "inherit" }}>G{cell.guest}</div> : null}
                                  {isDragEligible ? <div className="subtle" style={{ fontSize: "0.65rem", color: "inherit" }}>swap</div> : null}
                                </td>
                              );
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {fieldHeatmapReport.totalFields > fieldHeatmapReport.shownFields ? (
                    <div className="subtle mt-2">
                      Showing {fieldHeatmapReport.shownFields} of {fieldHeatmapReport.totalFields} fields (sorted by utilization).
                    </div>
                  ) : null}
                </div>
              ) : null}
              {regularCalendarTimelineReport.weekRows.length ? (
                <div className="callout">
                  <div className="font-bold mb-2">Season timeline + calendar (Regular Season)</div>
                  <div className="subtle mb-2">
                    Timeline cards emphasize late-season weeks (weather reliability weighting) and show week-level utilization/rule pressure. Calendar grid shows games/open slots by day.
                  </div>
                  <div className="subtle mb-2">
                    Calendar day cells marked with a single regular game can be dragged onto another single-game day cell to try a preview swap.
                  </div>
                  <div className="row row--wrap gap-2">
                    {regularCalendarTimelineReport.weekRows.map((week) => {
                      const isRepairWeek = isWeekHighlightedByRepair(week.weekKey);
                      const lateFactor = regularCalendarTimelineReport.weekRows.length > 1
                        ? ((week.weekIndex - 1) / (regularCalendarTimelineReport.weekRows.length - 1))
                        : 1;
                      const utilFactor = Math.max(0, Math.min(1, Number(week.utilizationPct || 0) / 100));
                      const bg = week.hardRuleTouches > 0
                        ? `rgba(239,68,68,${0.10 + (lateFactor * 0.08)})`
                        : week.utilizationPct >= 75
                          ? `rgba(15,118,110,${0.08 + (utilFactor * 0.18) + (lateFactor * 0.06)})`
                          : `rgba(59,130,246,${0.06 + (utilFactor * 0.12) + (lateFactor * 0.05)})`;
                      const border = week.hardRuleTouches > 0
                        ? "#fecaca"
                        : (week.blocked ? "#fcd34d" : "#cbd5e1");
                      return (
                        <div
                          key={`timeline-card-${week.weekKey}`}
                          style={{
                            border: "1px solid " + (isRepairWeek ? "#f59e0b" : border),
                            borderRadius: 10,
                            background: bg,
                            padding: "0.55rem 0.65rem",
                            minWidth: 170,
                            flex: "1 1 170px",
                            boxShadow: isRepairWeek ? "0 0 0 2px rgba(245,158,11,0.18)" : "none",
                          }}
                          title={`${week.weekKey} | games ${week.totalGames}/${week.totalCapacity || 0} | hard ${week.hardRuleTouches} | soft ${week.softRuleTouches}`}
                        >
                          <div className="row row--between gap-2" style={{ alignItems: "center" }}>
                            <div style={{ fontWeight: 700 }}>W{week.weekIndex}</div>
                            <span className="pill">{week.phaseBand}</span>
                          </div>
                          <div className="subtle">{week.weekStart}{week.weekEnd ? ` to ${week.weekEnd}` : ""}</div>
                          <div className="subtle">
                            Util: <b>{week.totalGames}/{week.totalCapacity}</b> ({week.utilizationPct}%)
                            {week.totalGuest > 0 ? <> | G{week.totalGuest}</> : null}
                          </div>
                          <div className="subtle">
                            Weather weight: <b>{week.weatherWeight.toFixed(2)}</b>
                            {week.blocked ? <> | <b>blocked overlap</b></> : null}
                          </div>
                          <div className="subtle">
                            Rules: hard <b>{week.hardRuleTouches}</b> / soft <b>{week.softRuleTouches}</b>
                          </div>
                        </div>
                      );
                    })}
                  </div>

                  <div className={`tableWrap mt-3 ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                    <table className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}>
                      <thead>
                        <tr>
                          <th>Week</th>
                          <th>Range</th>
                          <th>Util</th>
                          <th>Rules</th>
                          {regularCalendarTimelineReport.weekdayColumns.map((day) => (
                            <th key={`calendar-day-${day}`}>{day}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody>
                        {regularCalendarTimelineReport.weekRows.map((week) => (
                          <tr
                            key={`calendar-week-${week.weekKey}`}
                            style={isWeekHighlightedByRepair(week.weekKey) ? { backgroundColor: "#fffbeb" } : undefined}
                          >
                            <td>
                              <div>W{week.weekIndex}</div>
                              <div className="subtle">{week.phaseBand}</div>
                            </td>
                            <td>
                              <div>{week.weekStart}</div>
                              <div className="subtle">{week.weekEnd}</div>
                            </td>
                            <td>
                              <div>{week.totalGames}/{week.totalCapacity}</div>
                              <div className="subtle">{week.utilizationPct}%</div>
                            </td>
                            <td>
                              <div>H{week.hardRuleTouches} / S{week.softRuleTouches}</div>
                              {week.blocked ? <div className="subtle">Blocked overlap</div> : null}
                            </td>
                            {week.cells.map((cell) => {
                              const isRepairWeek = isWeekHighlightedByRepair(week.weekKey);
                              const dragAssignment = cell?.dragAssignment || null;
                              const dragKey = dragAssignment ? assignmentExplainKey(dragAssignment) : "";
                              const isDragEligible = !!dragAssignment && canDragSwapAssignment(dragAssignment);
                              const isDragSource = !!dragKey && dragSwapSourceKey === dragKey;
                              const isDragTarget = !!dragKey && dragSwapTargetKey === dragKey && dragSwapSourceKey && dragSwapSourceKey !== dragKey;
                              const totalLoad = Number(cell.games || 0) + Number(cell.open || 0);
                              const bg = totalLoad <= 0
                                ? "rgba(148,163,184,0.06)"
                                : (cell.games > 0 && cell.open === 0
                                  ? "rgba(34,197,94,0.10)"
                                  : (cell.games > 0
                                    ? "rgba(59,130,246,0.10)"
                                    : "rgba(148,163,184,0.10)"));
                              return (
                                <td
                                  key={`calendar-cell-${week.weekKey}-${cell.day}`}
                                  draggable={isDragEligible}
                                  onDragStart={isDragEligible ? (event) => handleAssignmentDragStart(event, dragAssignment) : undefined}
                                  onDragOver={isDragEligible ? (event) => handleAssignmentDragOver(event, dragAssignment) : undefined}
                                  onDrop={isDragEligible ? (event) => handleAssignmentDrop(event, dragAssignment) : undefined}
                                  onDragEnd={isDragEligible ? clearAssignmentDragSwap : undefined}
                                  style={{
                                    background: bg,
                                    minWidth: 66,
                                    boxShadow:
                                      isDragSource
                                        ? "inset 0 0 0 2px rgba(37,99,235,0.45)"
                                        : (isDragTarget
                                          ? "inset 0 0 0 2px rgba(124,58,237,0.45)"
                                          : (isRepairWeek ? "inset 0 0 0 1px rgba(245,158,11,0.35)" : "none")),
                                    cursor: isDragEligible ? "move" : "default",
                                  }}
                                  title={`${cell.day} | ${cell.firstDate || week.weekKey} | games ${cell.games} | open ${cell.open}`}
                                >
                                  <div style={{ fontWeight: 600 }}>{cell.games > 0 ? `G${cell.games}` : "-"}</div>
                                  <div className="subtle">
                                    {cell.open > 0 ? `Open ${cell.open}` : "Open 0"}
                                    {cell.guest > 0 ? ` | Guest ${cell.guest}` : ""}
                                  </div>
                                  {cell.firstDate ? <div className="subtle">{cell.firstDate}</div> : null}
                                  {isDragEligible ? <div className="subtle" style={{ fontSize: "0.65rem" }}>swap</div> : null}
                                </td>
                              );
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  {regularCalendarTimelineReport.totalWeeks > regularCalendarTimelineReport.shownWeeks ? (
                    <div className="subtle mt-2">
                      Showing first {regularCalendarTimelineReport.shownWeeks} of {regularCalendarTimelineReport.totalWeeks} regular-season weeks in the timeline/calendar views.
                    </div>
                  ) : null}
                </div>
              ) : null}
              <div className={unassignedRegularReport.totalMatchups > 0 ? "callout callout--error" : "callout"}>
                <div className="font-bold mb-2">Unassigned matchup impact (Regular Season)</div>
                {unassignedRegularReport.totalMatchups > 0 ? (
                  <>
                    <div className="subtle">
                      {unassignedRegularReport.totalMatchups} matchup(s) are still unassigned. Teams with higher unassigned counts are most at risk of missing games.
                    </div>
                    <div className="subtle">
                      {unassignedRegularReport.openSlots > 0
                        ? `${unassignedRegularReport.openSlots} open regular slot(s) remain. Try relaxing rules (max games/week or no-doubleheaders) to fit more games.`
                        : "No open regular slots remain; add or reclassify game-capable slots to place the remaining matchups."}
                    </div>
                    <div className="tableWrap mt-2">
                      <table className="table">
                        <thead>
                          <tr>
                            <th>Team</th>
                            <th>Assigned</th>
                            <th>Unassigned</th>
                            <th>Target</th>
                            <th>Coverage</th>
                          </tr>
                        </thead>
                        <tbody>
                          {unassignedRegularReport.rows.map((row) => (
                            <tr key={`team-impact-${row.teamId}`}>
                              <td>{row.teamId}</td>
                              <td>{row.assigned}</td>
                              <td>{row.unassigned}</td>
                              <td>{row.target}</td>
                              <td>{row.coveragePct}%</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                    {unassignedRegularReport.matchupRows.length ? (
                      <div className="tableWrap mt-2">
                        <table className="table">
                          <thead>
                            <tr>
                              <th>Unassigned matchup</th>
                              <th>Count</th>
                            </tr>
                          </thead>
                          <tbody>
                            {unassignedRegularReport.matchupRows.slice(0, 120).map((row) => (
                              <tr key={`unassigned-pair-${row.homeTeamId}-${row.awayTeamId}`}>
                                <td>{row.homeTeamId} vs {row.awayTeamId}</td>
                                <td>{row.count}</td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                        {unassignedRegularReport.matchupRows.length > 120 ? (
                          <div className="subtle mt-2">Showing first 120 unassigned matchup pairs.</div>
                        ) : null}
                      </div>
                    ) : null}
                  </>
                ) : (
                  <div className="subtle">All regular-season matchups were assigned.</div>
                )}
              </div>
                  </div>
                </CollapsibleSection>

                <CollapsibleSection
                  title="Assignments & explainability"
                  subtitle="Inspect the actual placements, try preview-only swaps, and drill into one game at a time."
                  defaultExpanded={!!selectedGameExplain}
                  storageKey={PREVIEW_SECTION_STORAGE_KEYS.assignments}
                  isExpanded={previewSectionControl.expanded.assignments}
                  onChange={(isExpanded) => handlePreviewSectionToggle("assignments", isExpanded)}
                >
                  <div className="stack gap-3">
                <div className="subtle">
                  Tip: drag one <b>Regular Season</b> game row onto another to try a preview-only swap. Guest/external rows stay locked, and the drop is revalidated immediately.
                </div>
                <div className="callout">
                  <div className="row row--wrap gap-2" style={{ alignItems: "end" }}>
                    <label>
                      Phase filter
                      <select
                        aria-label="Phase filter"
                        value={assignmentPhaseFilter}
                        onChange={(e) => setAssignmentPhaseFilter(e.target.value)}
                      >
                        <option value="">All phases</option>
                        {assignmentPhaseOptions.map((phase) => (
                          <option key={phase} value={phase}>{phase}</option>
                        ))}
                      </select>
                    </label>
                    <label>
                      Team filter
                      <select
                        aria-label="Team filter"
                        value={assignmentTeamFilter}
                        onChange={(e) => setAssignmentTeamFilter(e.target.value)}
                      >
                        <option value="">All teams</option>
                        {assignmentTeamOptions.map((team) => (
                          <option key={team.teamId} value={team.teamId}>
                            {team.label} ({team.teamId})
                          </option>
                        ))}
                      </select>
                    </label>
                    <label>
                      Field filter
                      <select
                        aria-label="Field filter"
                        value={assignmentFieldFilter}
                        onChange={(e) => setAssignmentFieldFilter(e.target.value)}
                      >
                        <option value="">All fields</option>
                        {assignmentFieldOptions.map((fieldKey) => (
                          <option key={fieldKey} value={fieldKey}>{fieldKey}</option>
                        ))}
                      </select>
                    </label>
                    <label className="inlineCheck" style={{ marginBottom: "0.35rem" }}>
                      <input
                        type="checkbox"
                        checked={showHighlightedAssignmentsOnly}
                        onChange={(e) => setShowHighlightedAssignmentsOnly(e.target.checked)}
                        disabled={!activeHighlightLookup}
                      />
                      Only highlighted issues
                    </label>
                    <button
                      type="button"
                      className="btn btn--ghost"
                      onClick={() => {
                        setAssignmentPhaseFilter("");
                        setAssignmentTeamFilter("");
                        setAssignmentFieldFilter("");
                        setShowHighlightedAssignmentsOnly(false);
                      }}
                      disabled={!assignmentPhaseFilter && !assignmentTeamFilter && !assignmentFieldFilter && !showHighlightedAssignmentsOnly}
                    >
                      Clear filters
                    </button>
                  </div>
                  <div className="subtle mt-2">
                    Showing <b>{Math.min(filteredPreviewAssignments.length, 250)}</b> of <b>{filteredPreviewAssignments.length}</b> matching assignment(s)
                    {" "}out of {previewCollections.assignments.length} total.
                  </div>
                  {!activeHighlightLookup ? (
                    <div className="subtle mt-1">
                      Select a repair proposal or rule-health item above to enable the highlighted-only filter.
                    </div>
                  ) : null}
                </div>
                <div className={`tableWrap ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                  <table
                    aria-label="Preview assignments"
                    className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}
                  >
                    <thead>
                      <tr>
                        <th>Phase</th>
                        <th>Day</th>
                        <th>Date</th>
                        <th>Time</th>
                        <th>Field</th>
                        <th>Home</th>
                        <th>Away</th>
                        <th>Explain</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredPreviewAssignments.length === 0 ? (
                        <tr>
                          <td colSpan="8" className="subtle">No assignments match the current filters.</td>
                        </tr>
                      ) : filteredPreviewAssignments.slice(0, 250).map((a) => {
                        const rowKey = assignmentExplainKey(a);
                        const isSelected = selectedGameExplain && assignmentExplainKey(selectedGameExplain.selected) === rowKey;
                        const isRepairHighlighted = isAssignmentHighlightedByRepair(a);
                        const isDragEligible = canDragSwapAssignment(a);
                        const isDragSource = dragSwapSourceKey === rowKey;
                        const isDragTarget = dragSwapTargetKey === rowKey && dragSwapSourceKey && dragSwapSourceKey !== rowKey;
                        const rowStyle = isSelected
                          ? { backgroundColor: "#ecfeff", outline: "1px solid #99f6e4" }
                          : (isDragSource
                            ? { backgroundColor: "#dbeafe", outline: "1px dashed #2563eb" }
                            : (isDragTarget
                              ? { backgroundColor: "#ede9fe", outline: "1px dashed #7c3aed" }
                              : (isRepairHighlighted ? { backgroundColor: "#fffbeb" } : undefined)));
                        return (
                          <tr
                            key={rowKey}
                            style={rowStyle}
                            draggable={isDragEligible}
                            onDragStart={(event) => handleAssignmentDragStart(event, a)}
                            onDragOver={(event) => handleAssignmentDragOver(event, a)}
                            onDrop={(event) => handleAssignmentDrop(event, a)}
                            onDragEnd={clearAssignmentDragSwap}
                            title={
                              isDragEligible
                                ? "Drag onto another regular-season game to preview a swap."
                                : a?.isExternalOffer
                                  ? "Locked guest/external slot. Preview fixes and swaps will not move it."
                                  : ""
                            }
                          >
                            <td>{a.phase}</td>
                            <td>{isoDayShort(a.gameDate) || "-"}</td>
                            <td>{a.gameDate}</td>
                            <td>{a.startTime}-{a.endTime}</td>
                            <td>{a.fieldKey}</td>
                            <td>{a.homeTeamId || "-"}</td>
                            <td>{a.awayTeamId || "-"}</td>
                            <td>
                              <button
                                type="button"
                                className="btn btn--ghost"
                                onClick={() => setSelectedExplainGameKey(rowKey)}
                                aria-pressed={isSelected ? "true" : "false"}
                                title="Show placement rationale and rule impacts for this game"
                              >
                                {isSelected ? "Selected" : "Explain"}
                              </button>
                            </td>
                          </tr>
                        );
                      })}
                    </tbody>
                  </table>
                  {filteredPreviewAssignments.length > 250 ? (
                    <div className="subtle mt-2">Showing first 250 matching assignments.</div>
                  ) : null}
                </div>
                <div className={`callout mt-2 ${
                  selectedGameExplain?.hardRuleTouches > 0 ? "callout--error" : "callout--ok"
                }`}>
                  <div className="row row--between gap-2" style={{ alignItems: "center" }}>
                    <div className="font-bold">Game explainability (preview)</div>
                    {selectedGameExplain ? (
                      <button
                        type="button"
                        className="btn btn--ghost"
                        onClick={() => setSelectedExplainGameKey("")}
                      >
                        Clear selection
                      </button>
                    ) : null}
                  </div>
                  {!selectedGameExplain ? (
                    <div className="subtle mt-2">
                      Click <b>Explain</b> on any assignment to inspect placement context, weekly team load, field utilization, and rule-health touchpoints.
                    </div>
                  ) : (
                    <>
                      <div className="subtle mt-2">
                        <b>{selectedGameExplain.selected.homeTeamId || "-"}</b>
                        {selectedGameExplain.selected.isExternalOffer ? " vs Guest" : ` vs ${selectedGameExplain.selected.awayTeamId || "-"}`}
                        {" | "}
                        {selectedGameExplain.selected.phase}
                        {" | "}
                        {selectedGameExplain.selected.gameDate} {selectedGameExplain.selected.startTime}-{selectedGameExplain.selected.endTime}
                        {" | "}
                        {selectedGameExplain.selected.fieldKey || "(Unknown field)"}
                      </div>
                      <div className="subtle">
                        Strategy: <b>{selectedGameExplain.constructionStrategy || "legacy"}</b>
                        {selectedGameExplain.seed != null ? <> | Seed: <b>{selectedGameExplain.seed}</b></> : null}
                        {selectedGameExplain.weekNumber && selectedGameExplain.weekCount ? (
                          <> | Regular season week: <b>W{selectedGameExplain.weekNumber}</b> of {selectedGameExplain.weekCount}</>
                        ) : null}
                        {selectedGameExplain.lateSeasonFactor != null ? (
                          <> | Late-season position: <b>{Math.round(selectedGameExplain.lateSeasonFactor * 100)}%</b></>
                        ) : null}
                      </div>

                      <div className="grid2 mt-2">
                        <div className="callout">
                          <div className="font-bold mb-1">Placement factors (heuristic)</div>
                          <div className="stack gap-1">
                            {selectedGameExplain.scoringFactors.map((factor) => (
                              <div
                                key={`explain-factor-${factor.key}`}
                                style={{
                                  border: "1px solid #e2e8f0",
                                  borderLeftWidth: 4,
                                  borderLeftColor:
                                    factor.tone === "good" ? "#16a34a" :
                                      factor.tone === "warn" ? "#d97706" :
                                        "#64748b",
                                  borderRadius: 8,
                                  padding: "0.5rem 0.65rem",
                                  background: "#fff",
                                }}
                              >
                                <div style={{ fontWeight: 600 }}>{factor.label}</div>
                                <div className="subtle">{factor.detail}</div>
                              </div>
                            ))}
                          </div>
                        </div>
                        <div className={`callout ${selectedGameExplain.hardRuleTouches > 0 ? "callout--error" : ""}`}>
                          <div className="font-bold mb-1">Rule touchpoints</div>
                          <div className="subtle mb-2">
                            Hard matches: <b>{selectedGameExplain.hardRuleTouches}</b> | Soft matches: <b>{selectedGameExplain.softRuleTouches}</b>
                          </div>
                          {selectedGameExplain.relatedRuleGroups.length ? (
                            <div className="stack gap-1">
                              {selectedGameExplain.relatedRuleGroups.slice(0, 8).map((group) => (
                                <div
                                  key={`explain-group-${group.ruleId}`}
                                  style={{
                                    border: "1px solid #e2e8f0",
                                    borderRadius: 8,
                                    padding: "0.5rem 0.65rem",
                                    background: "#fff",
                                  }}
                                >
                                  <div className="row gap-2" style={{ alignItems: "center" }}>
                                    <span
                                      className="pill"
                                      style={group.severity === "hard"
                                        ? { backgroundColor: "#fee2e2", color: "#991b1b", borderColor: "#fecaca" }
                                        : undefined}
                                    >
                                      {group.severity || "soft"}
                                    </span>
                                    <b>{group.ruleId}</b>
                                    <span className="subtle">({group.matchedViolations.length} match{group.matchedViolations.length === 1 ? "" : "es"})</span>
                                  </div>
                                  <div className="subtle mt-1">{group.summary || ISSUE_HINTS[group.ruleId] || "Rule impact detected for this game."}</div>
                                  <div className="subtle mt-1">
                                    {group.matchedViolations.slice(0, 3).map((violation) => violation?.message).filter(Boolean).join(" | ")}
                                  </div>
                                </div>
                              ))}
                              {selectedGameExplain.relatedRuleGroups.length > 8 ? (
                                <div className="subtle">Showing first 8 related rule groups.</div>
                              ) : null}
                            </div>
                          ) : (
                            <div className="subtle">No direct rule-health matches for this game in the current preview.</div>
                          )}
                        </div>
                      </div>
                      {isEngineTraceSource(selectedGameExplain.backendExplanation?.source) ? (
                        <div className="callout mt-2">
                          <div className="font-bold mb-1">Scheduler trace details (engine)</div>
                          <div className="subtle mb-2">
                            Placement rank: <b>{selectedGameExplain.backendExplanation.placementRank ?? "-"}</b>
                            {" | "}Slot order: <b>{selectedGameExplain.backendExplanation.slotOrderDirection || "unknown"}</b>
                            {" | "}Candidates: <b>{selectedGameExplain.backendExplanation.feasibleCandidateCount ?? "?"}</b> feasible / {selectedGameExplain.backendExplanation.candidateCount ?? "?"} total
                          </div>
                          {selectedGameExplain.backendExplanation.scoreBreakdown ? (
                            <div className="tableWrap">
                              <table className="table table--compact">
                                <thead>
                                  <tr>
                                    <th>Score term</th>
                                    <th>Penalty</th>
                                  </tr>
                                </thead>
                                <tbody>
                                  <tr>
                                    <td>Team volume</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.teamVolumePenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Team imbalance</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.teamImbalancePenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Load spread</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.teamLoadSpreadPenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Weekly participation</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.weeklyParticipationPenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Opponent repeat</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.pairRepeatPenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Idle gap reduction (bonus)</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.idleGapReductionBonus ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Late priority matchup</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.latePriorityPenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Weather reliability (slot)</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.weatherReliabilityPenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td>Home/away</td>
                                    <td>{selectedGameExplain.backendExplanation.scoreBreakdown.homeAwayPenalty ?? 0}</td>
                                  </tr>
                                  <tr>
                                    <td><b>Total</b></td>
                                    <td><b>{selectedGameExplain.backendExplanation.scoreBreakdown.totalScore ?? selectedGameExplain.backendExplanation.selectedScore ?? 0}</b></td>
                                  </tr>
                                </tbody>
                              </table>
                            </div>
                          ) : null}
                          <div className="grid2 mt-2">
                            <div>
                              <div className="font-bold mb-1" style={{ fontSize: "0.95rem" }}>Top feasible alternatives</div>
                              {Array.isArray(selectedGameExplain.backendExplanation.topFeasibleAlternatives) && selectedGameExplain.backendExplanation.topFeasibleAlternatives.length ? (
                                <div className="tableWrap">
                                  <table className="table table--compact">
                                    <thead>
                                      <tr>
                                        <th>Matchup</th>
                                        <th>Score</th>
                                      </tr>
                                    </thead>
                                    <tbody>
                                      {selectedGameExplain.backendExplanation.topFeasibleAlternatives.slice(0, 5).map((row, idx) => (
                                        <tr key={`trace-feasible-${idx}`}>
                                          <td>{row?.homeTeamId || "-"} vs {row?.awayTeamId || "-"}</td>
                                          <td>{row?.score ?? "-"}</td>
                                        </tr>
                                      ))}
                                    </tbody>
                                  </table>
                                </div>
                              ) : (
                                <div className="subtle">No feasible alternative list available.</div>
                              )}
                            </div>
                            <div>
                              <div className="font-bold mb-1" style={{ fontSize: "0.95rem" }}>Top rejected alternatives</div>
                              {Array.isArray(selectedGameExplain.backendExplanation.topRejectedAlternatives) && selectedGameExplain.backendExplanation.topRejectedAlternatives.length ? (
                                <div className="tableWrap">
                                  <table className="table table--compact">
                                    <thead>
                                      <tr>
                                        <th>Matchup</th>
                                        <th>Reject reason</th>
                                      </tr>
                                    </thead>
                                    <tbody>
                                      {selectedGameExplain.backendExplanation.topRejectedAlternatives.slice(0, 5).map((row, idx) => (
                                        <tr key={`trace-rejected-${idx}`}>
                                          <td>{row?.homeTeamId || "-"} vs {row?.awayTeamId || "-"}</td>
                                          <td>{row?.rejectReason || "-"}</td>
                                        </tr>
                                      ))}
                                    </tbody>
                                  </table>
                                </div>
                              ) : (
                                <div className="subtle">No rejected alternatives recorded.</div>
                              )}
                            </div>
                          </div>
                        </div>
                      ) : null}
                      {selectedGameExplain.backendExplanation?.source === "preview_repair_move_v1" ? (
                        <div className="callout mt-2">
                          <div className="font-bold mb-1">Preview repair trace</div>
                          <div className="subtle">
                            {selectedGameExplain.backendExplanation.note || "This game was moved by an Apply Fix (Preview) action."}
                          </div>
                          <div className="tableWrap mt-2">
                            <table className="table table--compact">
                              <thead>
                                <tr>
                                  <th></th>
                                  <th>Date</th>
                                  <th>Time</th>
                                  <th>Field</th>
                                  <th>Slot</th>
                                </tr>
                              </thead>
                              <tbody>
                                <tr>
                                  <td>From</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedFrom?.gameDate || "-"}</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedFrom?.startTime || "-"}-{selectedGameExplain.backendExplanation?.movedFrom?.endTime || "-"}</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedFrom?.fieldKey || "-"}</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedFrom?.slotId || "-"}</td>
                                </tr>
                                <tr>
                                  <td>To</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedTo?.gameDate || "-"}</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedTo?.startTime || "-"}-{selectedGameExplain.backendExplanation?.movedTo?.endTime || "-"}</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedTo?.fieldKey || "-"}</td>
                                  <td>{selectedGameExplain.backendExplanation?.movedTo?.slotId || "-"}</td>
                                </tr>
                              </tbody>
                            </table>
                          </div>
                          {selectedGameExplain.backendExplanation?.originalTrace ? (
                            <div className="subtle mt-2">
                              Original engine trace preserved in backend response (`originalTrace`) for debugging/reference.
                            </div>
                          ) : null}
                        </div>
                      ) : null}
                    </>
                  )}
                </div>
                  </div>
                </CollapsibleSection>
              </>
            )}
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(3)}>
              Back
            </button>
            {previewApplyBlocked ? (
              <div className="subtle" style={{ alignSelf: "center", marginRight: "0.5rem" }}>
                Apply blocked by hard rule violations. Use Rule Health to review the failing rules.
              </div>
            ) : null}
            <button className="btn btn--primary" type="button" onClick={applySchedule} disabled={loading || !preview || previewApplyBlocked}>
              {loading ? "Applying..." : (previewApplyBlocked ? "Apply blocked" : "Apply schedule")}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
