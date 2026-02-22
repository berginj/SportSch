import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../lib/api";
import { validateIsoDates } from "../lib/date";
import { buildAvailabilityInsights } from "../lib/availabilityInsights";
import { trackEvent } from "../lib/telemetry";
import Toast from "../components/Toast";

const WEEKDAY_OPTIONS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const MAX_PREFERRED_WEEKNIGHTS = 3;
const SLOT_TYPE_OPTIONS = [
  { value: "practice", label: "Practice" },
  { value: "game", label: "Game" },
  { value: "both", label: "Both" },
];
const ISSUE_HINTS = {
  "unassigned-matchups": "Not enough availability slots, or constraints are too tight for the slot pool.",
  "unassigned-slots": "More availability than matchups. These can become extra offers or remain unused.",
  "double-header": "Not enough slots to spread games across dates. Add slots or relax no-doubleheaders.",
  "double-header-balance": "Doubleheaders are not evenly distributed. Shift slot priorities/times to spread same-day load across teams.",
  "max-games-per-week": "Max games/week is a hard cap. Add slots or widen the season window if assignments are short.",
  "missing-opponent": "A slot is missing an opponent. Check team count or external/guest game settings.",
};

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

function StepButton({ active, status = "neutral", onClick, children }) {
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
  const [seasonStart, setSeasonStart] = useState("");
  const [seasonEnd, setSeasonEnd] = useState("");
  const [poolStart, setPoolStart] = useState("");
  const [poolEnd, setPoolEnd] = useState("");
  const [bracketStart, setBracketStart] = useState("");
  const [bracketEnd, setBracketEnd] = useState("");

  const [minGamesPerTeam, setMinGamesPerTeam] = useState(0);
  const [poolGamesPerTeam, setPoolGamesPerTeam] = useState(2);
  const [preferredWeeknights, setPreferredWeeknights] = useState([]);
  const [strictPreferredWeeknights, setStrictPreferredWeeknights] = useState(false);
  const [guestGamesPerWeek, setGuestGamesPerWeek] = useState(0);
  const [blockSpringBreak, setBlockSpringBreak] = useState(false);
  const [maxGamesPerWeek, setMaxGamesPerWeek] = useState(2);
  const [noDoubleHeaders, setNoDoubleHeaders] = useState(true);
  const [balanceHomeAway, setBalanceHomeAway] = useState(true);
  const [teamCount, setTeamCount] = useState(0);

  const [step, setStep] = useState(0);
  const [preview, setPreview] = useState(null);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState("");
  const [toast, setToast] = useState(null);
  const [slotPlan, setSlotPlan] = useState([]);
  const [guestAnchorPrimarySlotId, setGuestAnchorPrimarySlotId] = useState("");
  const [guestAnchorSecondarySlotId, setGuestAnchorSecondarySlotId] = useState("");
  const [availabilityInsights, setAvailabilityInsights] = useState(null);
  const [autoAppliedPreferred, setAutoAppliedPreferred] = useState(false);
  const [availabilityLoading, setAvailabilityLoading] = useState(false);
  const [availabilityErr, setAvailabilityErr] = useState("");
  const [preferredTouched, setPreferredTouched] = useState(false);

  // Feasibility state
  const [feasibility, setFeasibility] = useState(null);
  const [feasibilityLoading, setFeasibilityLoading] = useState(false);
  const [hasAutoApplied, setHasAutoApplied] = useState(false);

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

  const steps = useMemo(
    () => ["Basics", "Postseason", "Slot plan (all phases)", "Rules", "Preview"],
    []
  );

  const springBreakRange = useMemo(
    () => buildSpringBreakRange(seasonStart, seasonEnd),
    [seasonStart, seasonEnd]
  );

  const activeBlockedRanges = useMemo(
    () => (blockSpringBreak && springBreakRange ? [springBreakRange] : []),
    [blockSpringBreak, springBreakRange]
  );

  const slotPlanSummary = useMemo(() => {
    const total = slotPlan.length;
    const practice = slotPlan.filter((s) => s.slotType === "practice").length;
    const game = slotPlan.filter((s) => s.slotType === "game").length;
    const both = slotPlan.filter((s) => s.slotType === "both").length;
    const ranked = slotPlan.filter((s) => Number(s.priorityRank) > 0);
    const uniqueRankedPatterns = new Set(ranked.map((s) => s.basePatternKey)).size;
    return { total, practice, game, both, ranked: uniqueRankedPatterns, gameCapable: game + both };
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

    const isBlocked = (gameDate) =>
      activeBlockedRanges.some((range) => isIsoDateInRange(gameDate, range.startDate, range.endDate));

    const datedSlots = (slotPlan || []).filter((slot) => isIsoDate(slot?.gameDate));
    const gameOnlySlots = datedSlots.filter((slot) => slot?.slotType === "game");
    const bothSlots = datedSlots.filter((slot) => slot?.slotType === "both");
    const practiceOnlySlots = datedSlots.filter((slot) => slot?.slotType === "practice");

    const gameCapableSlots = [...gameOnlySlots, ...bothSlots];
    const availableAllSlots = datedSlots.filter((slot) => !isBlocked(slot.gameDate));
    const availableGameCapableSlots = gameCapableSlots.filter((slot) => !isBlocked(slot.gameDate));
    const practiceSlotsAvailable = practiceOnlySlots.filter((slot) => !isBlocked(slot.gameDate));
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

    const preferredSet = new Set(preferredWeeknights);
    const preferredRegularSlotsAvailable = regularGameSlots.filter(
      (slot) => preferredSet.size === 0 || preferredSet.has(isoDayShort(slot.gameDate))
    ).length;

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
      preferredRegularSlotsAvailable,
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
      strictCapacityShortfall: strictPreferredWeeknights
        ? Math.max(0, regularRequiredMinimum - preferredRegularSlotsAvailable)
        : 0,
    };
  }, [
    activeBlockedRanges,
    bracketEnd,
    bracketStart,
    maxGamesPerWeek,
    minGamesPerTeam,
    poolEnd,
    poolGamesPerTeam,
    poolStart,
    preferredWeeknights,
    seasonEnd,
    seasonStart,
    slotPlan,
    strictPreferredWeeknights,
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

  function toggleWeeknight(day) {
    setPreferredTouched(true);
    setPreferredWeeknights((prev) => {
      if (prev.includes(day)) return prev.filter((d) => d !== day);
      return [...prev, day].slice(0, MAX_PREFERRED_WEEKNIGHTS);
    });
  }

  function updatePatternPlan(patternKey, patch) {
    setSlotPlan((prev) =>
      prev.map((item) => (item.basePatternKey === patternKey ? { ...item, ...patch } : item))
    );
    setPreview(null);
  }

  function updatePatternSlotType(patternKey, currentPriorityRank, nextTypeRaw) {
    const nextType = normalizeSlotType(nextTypeRaw);
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
    setErr("");
    updatePatternPlan(patternKey, { endTime: end });
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
    setErr("");
    updatePatternPlan(patternKey, { endTime: end });
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
    setErr("");
    updatePatternPlan(patternKey, {
      slotType: nextType,
      priorityRank: nextPriority,
      endTime,
    });
    const changed =
      priorType !== nextType ||
      priorEndTime !== endTime ||
      String(priorPriority || "") !== String(nextPriority || "");
    setToast({
      tone: changed ? "success" : "info",
      duration: 2800,
      message: changed
        ? `${representative.weekday} ${representative.fieldKey}: set to ${nextType.toUpperCase()} (${Number(durationMinutes || 0)}m). Updated ${representative.count || 1} opening(s).`
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

  useEffect(() => {
    if (!division) return;
    setPreferredTouched(false);
    setPreferredWeeknights([]);
    setAutoAppliedPreferred(false);
    setSlotPlan([]);
    setGuestAnchorPrimarySlotId("");
    setGuestAnchorSecondarySlotId("");
  }, [division]);

  useEffect(() => {
    if (!leagueId || !division) {
      setTeamCount(0);
      return;
    }
    (async () => {
      try {
        const qs = new URLSearchParams();
        qs.set("division", division);
        const data = await apiFetch(`/api/teams?${qs.toString()}`);
        const list = Array.isArray(data) ? data : [];
        setTeamCount(list.length);
      } catch {
        setTeamCount(0);
      }
    })();
  }, [leagueId, division]);

  useEffect(() => {
    if (!leagueId || !division) return;
    if (!seasonStart || !seasonEnd) return;
    (async () => {
      setAvailabilityErr("");
      setAvailabilityLoading(true);
      try {
        const planningDateTo = maxIsoDate(seasonEnd, bracketEnd) || seasonEnd;
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

        const insights = buildAvailabilityInsights(availability);
        const dayCounts = new Map(
          (insights?.dayStats || []).map((day) => [day.day, day.slots])
        );
        const patternCounts = new Map();
        for (const slot of availability) {
          const weekday = isoDayShort(slot.gameDate || "");
          const patternKey = `${weekday}|${slot.startTime || ""}|${slot.endTime || ""}|${slot.fieldKey || ""}`;
          patternCounts.set(patternKey, (patternCounts.get(patternKey) || 0) + 1);
        }

        setSlotPlan((prev) => {
          const previousById = new Map((prev || []).map((item) => [item.slotId, item]));
          return availability.map((slot) => {
            const prior = previousById.get(slot.slotId);
            const weekday = isoDayShort(slot.gameDate || "");
            const baseStartTime = slot.startTime || "";
            const baseEndTime = slot.endTime || "";
            const basePatternKey = patternKeyFromParts(weekday, baseStartTime, baseEndTime, slot.fieldKey || "");
            const nextStartTime = prior?.startTime || baseStartTime;
            const nextEndTime = prior?.endTime || baseEndTime;
            const allocationSlotType = normalizeSlotType(slot.allocationSlotType || "practice");
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
        });

        setAvailabilityInsights(insights);
        if (!preferredTouched && insights.suggested.length) {
          setPreferredWeeknights(insights.suggested);
          setAutoAppliedPreferred(true);
        }
      } catch (e) {
        setAvailabilityErr(e?.message || "Failed to load availability insights.");
        setAvailabilityInsights(null);
        setSlotPlan([]);
        setGuestAnchorPrimarySlotId("");
        setGuestAnchorSecondarySlotId("");
      } finally {
        setAvailabilityLoading(false);
      }
    })();
  }, [leagueId, division, seasonStart, seasonEnd, bracketEnd, preferredTouched]);

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

  // Feasibility check with debouncing (triggered when rules change in Step 4)
  useEffect(() => {
    if (step !== 3) return; // Only run on Step 4 (Rules)
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

  function buildWizardPayload() {
    const slotPlanPayload = slotPlan.map((slot) => {
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
      preferredWeeknights: preferredWeeknights.slice(0, MAX_PREFERRED_WEEKNIGHTS),
      strictPreferredWeeknights,
      externalOfferPerWeek: Number(guestGamesPerWeek) || 0,
      maxGamesPerWeek: Number(maxGamesPerWeek) || 0,
      noDoubleHeaders,
      balanceHomeAway,
      slotPlan: slotPlanPayload,
    };
    if (blockedDateRanges.length) payload.blockedDateRanges = blockedDateRanges;

    const primaryAnchor = guestAnchorPayloadFromSlotId(guestAnchorPrimarySlotId);
    const secondaryAnchor = guestAnchorPayloadFromSlotId(guestAnchorSecondarySlotId);
    if (primaryAnchor) payload.guestAnchorPrimary = primaryAnchor;
    if (secondaryAnchor) payload.guestAnchorSecondary = secondaryAnchor;
    return payload;
  }

  async function fetchFeasibility() {
    if (!division || !seasonStart || !seasonEnd) return;
    if (slotPlan.length === 0) return;

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
    setLoading(true);
    try {
      const payload = buildWizardPayload();
      const data = await apiFetch("/api/schedule/wizard/preview", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setPreview(data || null);
      setStep(4);
      trackEvent("ui_season_wizard_preview", { leagueId, division });
    } catch (e) {
      setErr(e?.message || "Preview failed.");
      setPreview(null);
    } finally {
      setLoading(false);
    }
  }

  async function applySchedule() {
    if (!preview) return;
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
    setLoading(true);
    try {
      const payload = buildWizardPayload();
      await apiFetch("/api/schedule/wizard/apply", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      setToast({ tone: "success", message: "Wizard schedule applied." });
      trackEvent("ui_season_wizard_apply", { leagueId, division });
    } catch (e) {
      setErr(e?.message || "Apply failed.");
    } finally {
      setLoading(false);
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

  function buildIssueHint(issue, summary) {
    if (!issue) return "";
    const base = ISSUE_HINTS[issue.ruleId] || "";
    const issuePhase = getIssuePhase(issue);
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
    if (issue.ruleId === "double-header" && summary.teamCount && summary.teamCount % 2 === 1) {
      return `${base} With an odd team count (${summary.teamCount}), some byes help reduce doubleheaders.`;
    }
    if (issue.ruleId === "double-header-balance") {
      const max = Number(issue?.details?.maxDoubleHeaders ?? issue?.details?.max ?? NaN);
      const min = Number(issue?.details?.minDoubleHeaders ?? issue?.details?.min ?? NaN);
      if (Number.isFinite(max) && Number.isFinite(min)) {
        return `${base} Current spread is max ${max} vs min ${min} doubleheaders.`;
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
      notes.push(`Odd team count (${summary.teamCount}) adds BYEs and can create gaps.`);
    }
    if ((issues || []).some((i) => i.ruleId === "double-header")) {
      notes.push("Doubleheaders indicate tight slot density or too few usable dates.");
    }
    if ((issues || []).some((i) => i.ruleId === "double-header-balance")) {
      notes.push("Doubleheaders are allowed, but current assignment is uneven across teams.");
    }
    if ((issues || []).some((i) => i.ruleId === "max-games-per-week")) {
      notes.push("Max games/week is a hard limit and is restricting assignments; add slots or widen date range.");
      if (summary.teamCount % 2 === 1) {
        notes.push("Odd team count means one team will have a BYE and can have one fewer game in a given week.");
      }
    }
    if ((issues || []).some((i) => i.ruleId === "missing-opponent")) {
      notes.push("Guest games or external offers may be enabled; missing opponents are expected there.");
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
    if ((bracketStart && !bracketEnd) || (!bracketStart && bracketEnd)) return "Bracket start and end must both be set.";
    if (poolStart && (!isIsoDate(poolStart) || !isIsoDate(poolEnd))) return "Pool play dates must be YYYY-MM-DD.";
    if (bracketStart && (!isIsoDate(bracketStart) || !isIsoDate(bracketEnd))) return "Bracket dates must be YYYY-MM-DD.";
    if (poolStart && poolEnd && poolStart > poolEnd) return "Pool play start must be before pool play end.";
    if (bracketStart && bracketEnd && bracketStart > bracketEnd) return "Bracket start must be before bracket end.";
    if (poolStart && poolEnd && (poolStart < seasonStart || poolEnd > seasonEnd)) return "Pool play must stay within the season range.";
    if (bracketStart && bracketStart < seasonStart) return "Bracket must start on or after season start.";
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
    if (!Number.isFinite(maxGames) || maxGames < 0) return "Max games/week must be 0 or greater.";
    if (!Number.isFinite(minGames) || minGames < 0) return "Min games/team must be 0 or greater.";
    if (!Number.isFinite(poolGames) || poolGames < 2) return "Pool games/team must be 2 or greater.";
    if (!Number.isFinite(guestGames) || guestGames < 0) return "Guest games/week must be 0 or greater.";
    return "";
  }, [maxGamesPerWeek, minGamesPerTeam, poolGamesPerTeam, guestGamesPerWeek]);

  const previewError = useMemo(() => {
    if (!preview) return "";
    if ((preview.totalIssues || 0) > 0) return `${preview.totalIssues} validation issue(s) in preview.`;
    return "";
  }, [preview]);

  const unassignedRegularReport = useMemo(() => {
    if (!preview) {
      return {
        rows: [],
        matchupRows: [],
        totalMatchups: 0,
        openSlots: 0,
      };
    }

    const assignments = Array.isArray(preview.assignments) ? preview.assignments : [];
    const unassignedMatchups = Array.isArray(preview.unassignedMatchups) ? preview.unassignedMatchups : [];
    const unassignedSlots = Array.isArray(preview.unassignedSlots) ? preview.unassignedSlots : [];
    const regularAssignments = assignments.filter((a) => a?.phase === "Regular Season" && !a?.isExternalOffer);
    const regularUnassignedMatchups = unassignedMatchups.filter((m) => getIssuePhase(m) === "Regular Season");
    const regularOpenSlots = unassignedSlots.filter((s) => s?.phase === "Regular Season");

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
  }, [preview]);

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

    const assignments = Array.isArray(preview.assignments) ? preview.assignments : [];
    const unassignedSlots = Array.isArray(preview.unassignedSlots) ? preview.unassignedSlots : [];
    const unassignedMatchups = Array.isArray(preview.unassignedMatchups) ? preview.unassignedMatchups : [];

    const regularAssignments = assignments.filter((a) => a?.phase === "Regular Season");
    const regularGames = regularAssignments.filter((a) => !a?.isExternalOffer && a?.homeTeamId && a?.awayTeamId);
    const regularGuestGames = regularAssignments.filter((a) => a?.isExternalOffer && a?.homeTeamId);
    const regularUnassignedSlots = unassignedSlots.filter((s) => s?.phase === "Regular Season");
    const regularUnassignedMatchups = unassignedMatchups.filter((m) => getIssuePhase(m) === "Regular Season");

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
        return {
          assigned: record.assigned,
          target: record.assigned + record.unassigned,
          unassigned: record.unassigned,
          homeForRow,
          awayForRow,
        };
      }),
    }));

    const pairRows = [...pairMap.values()]
      .map((record) => ({
        ...record,
        target: record.assigned + record.unassigned,
        homeAwayGap: Math.abs(record.homeForA - record.homeForB),
      }))
      .sort((a, b) => {
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
    };
  }, [preview, seasonStart, seasonEnd, poolStart, minGamesPerTeam]);

  const previewRecommendations = useMemo(() => {
    if (!preview) return [];

    const summary = preview.summary || {};
    const regularSummary = summary.regularSeason || {};
    const teamCountValue = Number(summary.teamCount) || 0;
    const oddTeamCount = teamCountValue > 0 && teamCountValue % 2 === 1;
    const guestGamesValue = Math.max(0, Number(guestGamesPerWeek) || 0);
    const recommendedGuestGames = Math.max(
      1,
      Number(feasibility?.recommendations?.optimalGuestGamesPerWeek) || 0
    );

    const rows = [];

    if (oddTeamCount) {
      const byeCounts = regularBalanceReport.teamRows.map((row) => row.byeWeeks);
      if (byeCounts.length && regularBalanceReport.weekCount > 0) {
        const maxBye = Math.max(...byeCounts);
        const minBye = Math.min(...byeCounts);
        const heavyByeTeams = regularBalanceReport.teamRows
          .filter((row) => row.byeWeeks === maxBye)
          .slice(0, 4)
          .map((row) => row.teamId);
        rows.push({
          code: "ODD_TEAM_BYE_CONTEXT",
          tone: maxBye - minBye > 1 ? "warning" : "info",
          message:
            `Odd team count (${teamCountValue}) means BYEs are unavoidable. ` +
            `Estimated BYE weeks across ${regularBalanceReport.weekCount} regular-season week(s): min ${minBye}, max ${maxBye}` +
            (heavyByeTeams.length ? ` (highest: ${heavyByeTeams.join(", ")}).` : "."),
        });
      } else {
        rows.push({
          code: "ODD_TEAM_BYE_CONTEXT",
          tone: "info",
          message: `Odd team count (${teamCountValue}) means BYEs are unavoidable. Use guest games and slot priorities to spread BYEs evenly.`,
        });
      }

      if (
        guestGamesValue <= 0 &&
        ((regularSummary.unassignedSlots || 0) > 0 || unassignedRegularReport.openSlots > 0)
      ) {
        rows.push({
          code: "GUEST_GAME_RECOMMENDATION",
          tone: "warning",
          suggestedGuestGamesPerWeek: recommendedGuestGames,
          message:
            `This division has an odd team count and open regular slots (${unassignedRegularReport.openSlots || regularSummary.unassignedSlots || 0}). ` +
            `Try Guest games/week = ${recommendedGuestGames} to reduce idle weeks and use spare capacity.`,
        });
      } else if (guestGamesValue > 0 && regularBalanceReport.totalGuestGames === 0 && (regularSummary.unassignedSlots || 0) > 0) {
        rows.push({
          code: "GUEST_GAME_NOT_PLACED",
          tone: "warning",
          message:
            `Guest games are enabled (${guestGamesValue}/week) but none were placed. Check Slot plan game/both tags, priorities, and guest anchor options.`,
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
  }, [preview, guestGamesPerWeek, feasibility, regularBalanceReport, unassignedRegularReport.openSlots]);

  const planningChecksReport = useMemo(() => {
    if (!preview) return [];

    const checks = [];
    const issues = Array.isArray(preview.issues) ? preview.issues : [];
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
      const assignments = Array.isArray(preview.assignments) ? preview.assignments : [];
      const regularAssignments = assignments.filter((a) => a?.phase === "Regular Season");
      const guestAssignments = regularAssignments.filter((a) => a?.isExternalOffer && a?.homeTeamId);
      const unassignedMatchups = Array.isArray(preview.unassignedMatchups) ? preview.unassignedMatchups : [];

      const teamIds = new Set();
      regularAssignments.forEach((a) => {
        if (a?.homeTeamId) teamIds.add(a.homeTeamId);
        if (a?.awayTeamId) teamIds.add(a.awayTeamId);
      });
      unassignedMatchups
        .filter((m) => m?.phase === "Regular Season")
        .forEach((m) => {
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
  }, [preview, maxGamesPerWeek, noDoubleHeaders, guestGamesPerWeek, unassignedRegularReport]);

  const stepStatuses = useMemo(() => {
    const errors = [basicsError, postseasonError, slotPlanError, rulesError, previewError];
    if (err && step >= 0 && step < errors.length) {
      errors[step] = errors[step] || err;
    }
    const completed = [
      !basicsError,
      !postseasonError && step > 1,
      !slotPlanError && slotPlanSummary.gameCapable > 0 && step > 2,
      !rulesError && step > 3,
      !!preview && !previewError,
    ];
    return steps.map((_, idx) => {
      if (errors[idx]) return "error";
      if (idx === step) return "active";
      if (completed[idx]) return "complete";
      return "neutral";
    });
  }, [
    basicsError,
    postseasonError,
    slotPlanError,
    rulesError,
    previewError,
    err,
    step,
    slotPlanSummary.gameCapable,
    preview,
    steps,
  ]);

  return (
    <div className="stack gap-3">
      {toast ? <Toast {...toast} onClose={() => setToast(null)} /> : null}
      {err ? <div className="callout callout--error">{err}</div> : null}

      <div className="row row--wrap gap-2">
        {steps.map((label, idx) => (
          <StepButton key={label} active={step === idx} status={stepStatuses[idx]} onClick={() => setStep(idx)}>
            {label}
          </StepButton>
        ))}
      </div>
      <div className="subtle">Step state: green = complete, red = needs attention, neutral = pending.</div>

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
              <input value={seasonStart} onChange={(e) => setSeasonStart(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Season end
              <input value={seasonEnd} onChange={(e) => setSeasonEnd(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
          </div>
          <div className="row row--end">
            <button className="btn btn--primary" type="button" onClick={() => setStep(1)}>
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 1 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Postseason windows</div>
            <div className="subtle">Reserve the last week for pool play and the following week for the bracket.</div>
          </div>
          <div className="card__body grid2">
            <label>
              Pool play start
              <input value={poolStart} onChange={(e) => setPoolStart(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Pool play end
              <input value={poolEnd} onChange={(e) => setPoolEnd(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Bracket start
              <input value={bracketStart} onChange={(e) => setBracketStart(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
            <label>
              Bracket end
              <input value={bracketEnd} onChange={(e) => setBracketEnd(e.target.value)} placeholder="YYYY-MM-DD" />
            </label>
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(0)}>
              Back
            </button>
            <button className="btn btn--primary" type="button" onClick={() => setStep(2)}>
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 2 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Slot planning</div>
            <div className="subtle">
              Mark each availability as practice/game/both (defaults to practice), set priority rank, and pick guest game anchor options for all phases.
            </div>
          </div>
          <div className="card__body stack gap-3">
            <div className="callout">
              <div className="row row--wrap gap-2">
                <span className="pill">Total: {slotPlanSummary.total}</span>
                <span className="pill">Practice: {slotPlanSummary.practice}</span>
                <span className="pill">Game: {slotPlanSummary.game}</span>
                <span className="pill">Both: {slotPlanSummary.both}</span>
                <span className="pill">Ranked: {slotPlanSummary.ranked}</span>
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
                Score is based on how consistently the same weekday/time/field pattern appears in the queried window.
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
              Guest anchors now reserve matching weekly slots first for external/guest games (when matches exist), so regular matchups are scheduled around them.
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
                                <div key={p.key} className="callout" style={{ marginBottom: 0 }}>
                                  {(() => {
                                    const startMin = parseMinutes(p.startTime);
                                    const endMin = parseMinutes(p.endTime);
                                    const duration = startMin != null && endMin != null && endMin > startMin ? endMin - startMin : null;
                                    return (
                                      <>
                                  <div className="row row--between gap-2">
                                    <div><b>{p.startTime}-{p.endTime}</b></div>
                                    <span className="pill">Openings: {p.count}</span>
                                  </div>
                                  <div className="subtle">{p.fieldKey}</div>
                                  <div className="subtle">Duration: {duration ?? "?"} min</div>
                                  <div className="row row--wrap gap-2 mt-1">
                                    <label>
                                      Type
                                      <select
                                        value={p.slotType}
                                        onChange={(e) => updatePatternSlotType(p.key, p.priorityRank, e.target.value)}
                                      >
                                        {SLOT_TYPE_OPTIONS.map((opt) => (
                                          <option key={opt.value} value={opt.value}>
                                            {opt.label}
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
                        <th>Openings</th>
                        <th>Score</th>
                        <th>Type</th>
                        <th>Priority</th>
                      </tr>
                    </thead>
                    <tbody>
                      {slotPatterns.map((pattern) => (
                        <tr key={pattern.key}>
                          {(() => {
                            const startMin = parseMinutes(pattern.startTime);
                            const endMin = parseMinutes(pattern.endTime);
                            const duration = startMin != null && endMin != null && endMin > startMin ? endMin - startMin : "";
                            return (
                              <>
                          <td>{pattern.weekday}</td>
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
                          <td title="Higher score means this pattern appears more consistently in the season window.">
                            {pattern.score ?? 0}
                          </td>
                          <td>
                            <select
                              value={pattern.slotType}
                              onChange={(e) => updatePatternSlotType(pattern.key, pattern.priorityRank, e.target.value)}
                            >
                              {SLOT_TYPE_OPTIONS.map((opt) => (
                                <option key={opt.value} value={opt.value}>
                                  {opt.label}
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
            <button className="btn btn--primary" type="button" onClick={() => setStep(3)}>
              Next
            </button>
          </div>
        </div>
      ) : null}

      {step === 3 ? (
        <div className="card">
          <div className="card__header">
            <div className="h3">Scheduling rules</div>
            <div className="subtle">Set regular season and pool play constraints with live feasibility checking.</div>
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

            <div className="grid2">
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
              Max games per team per week
              <input
                type="number"
                min="0"
                value={maxGamesPerWeek}
                onChange={(e) => setMaxGamesPerWeek(e.target.value)}
              />
            </label>
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
            <div className="stack gap-2">
              <div className="muted text-sm">Preferred weeknights (pick up to three; other nights can still be used)</div>
              {availabilityLoading ? (
                <div className="muted text-sm">Analyzing availability for recommended nights...</div>
              ) : availabilityInsights?.suggested?.length ? (
                <div className="callout">
                  Recommended nights based on availability: <b>{availabilityInsights.suggested.join(", ")}</b>
                  {autoAppliedPreferred ? (
                    <span className="pill ml-2">Auto-selected</span>
                  ) : null}
                </div>
              ) : availabilityErr ? (
                <div className="callout callout--error">{availabilityErr}</div>
              ) : null}
              <div className="row row--wrap gap-2">
                {WEEKDAY_OPTIONS.map((day) => (
                  <button
                    key={day}
                    className={`pill ${preferredWeeknights.includes(day) ? "is-active" : ""}`}
                    type="button"
                    onClick={() => toggleWeeknight(day)}
                  >
                    {day}
                  </button>
                ))}
              </div>
              <div className="muted text-sm">
                Selected: {preferredWeeknights.length}/{MAX_PREFERRED_WEEKNIGHTS}
              </div>
              <label className="inlineCheck">
                <input
                  type="checkbox"
                  checked={strictPreferredWeeknights}
                  onChange={(e) => setStrictPreferredWeeknights(e.target.checked)}
                />
                Only use preferred nights (ignore other days)
              </label>
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
              {strictPreferredWeeknights ? (
                <div className="subtle">
                  Preferred-night capacity: {planningIntel.preferredRegularSlotsAvailable} regular-season slot(s) on selected nights.
                  {planningIntel.strictCapacityShortfall > 0 ? ` Short by ${planningIntel.strictCapacityShortfall}.` : ""}
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
            <button className="btn btn--primary" type="button" onClick={runPreview} disabled={loading}>
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
                          <tr key={`${getIssuePhase(issue)}-${issue.ruleId || "issue"}-${idx}`}>
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
                              return (
                                <td key={`matrix-cell-${row.teamId}-${colTeamId}`} title={`${row.teamId} vs ${colTeamId}`}>
                                  {cell.assigned}/{cell.target}
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
              <div className={unassignedRegularReport.totalMatchups > 0 ? "callout callout--error" : "callout"}>
                <div className="font-bold mb-2">Unassigned matchup impact (Regular Season)</div>
                {unassignedRegularReport.totalMatchups > 0 ? (
                  <>
                    <div className="subtle">
                      {unassignedRegularReport.totalMatchups} matchup(s) are still unassigned. Teams with higher unassigned counts are most at risk of missing games.
                    </div>
                    <div className="subtle">
                      {unassignedRegularReport.openSlots > 0
                        ? `${unassignedRegularReport.openSlots} open regular slot(s) remain. Try relaxing rules (max games/week, no-doubleheaders, preferred nights) to fit more games.`
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

                <div className={`tableWrap ${tableView === "B" ? "tableWrap--sticky" : ""}`}>
                  <table className={`table ${tableView === "B" ? "table--compact table--sticky" : ""}`}>
                    <thead>
                      <tr>
                        <th>Phase</th>
                        <th>Day</th>
                        <th>Date</th>
                        <th>Time</th>
                        <th>Field</th>
                        <th>Home</th>
                        <th>Away</th>
                      </tr>
                    </thead>
                    <tbody>
                      {(preview.assignments || []).slice(0, 250).map((a) => (
                        <tr key={`${a.phase}-${a.slotId}`}>
                          <td>{a.phase}</td>
                          <td>{isoDayShort(a.gameDate) || "-"}</td>
                          <td>{a.gameDate}</td>
                          <td>{a.startTime}-{a.endTime}</td>
                          <td>{a.fieldKey}</td>
                          <td>{a.homeTeamId || "-"}</td>
                          <td>{a.awayTeamId || "-"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  {(preview.assignments || []).length > 250 ? (
                    <div className="subtle mt-2">Showing first 250 assignments.</div>
                  ) : null}
                </div>
              </>
            )}
          </div>
          <div className="row row--end gap-2">
            <button className="btn btn--ghost" type="button" onClick={() => setStep(3)}>
              Back
            </button>
            <button className="btn btn--primary" type="button" onClick={applySchedule} disabled={loading || !preview}>
              {loading ? "Applying..." : "Apply schedule"}
            </button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
