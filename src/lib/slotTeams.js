function readTeamId(...values) {
  for (const value of values) {
    const normalized = String(value || "").trim();
    if (normalized) return normalized;
  }
  return "";
}

export function getSlotHomeTeamId(slot) {
  return readTeamId(slot?.homeTeamId, slot?.offeringTeamId);
}

export function getSlotOpponentTeamId(slot) {
  return readTeamId(slot?.awayTeamId, slot?.confirmedTeamId);
}

export function getSlotMatchupLabel(slot) {
  const gameType = String(slot?.gameType || "").trim().toLowerCase();
  if (gameType === "practice") {
    const team = readTeamId(slot?.confirmedTeamId, slot?.offeringTeamId);
    return team ? `Practice: ${team}` : "Practice";
  }

  const homeTeamId = getSlotHomeTeamId(slot);
  const awayTeamId = getSlotOpponentTeamId(slot);
  if (homeTeamId && awayTeamId) return `${homeTeamId} vs ${awayTeamId}`;
  if (homeTeamId && slot?.isExternalOffer) return `${homeTeamId} vs TBD (external)`;
  if (homeTeamId) return `${homeTeamId} vs TBD`;
  if (String(slot?.status || "").trim() === "Open") return "Open Slot";
  return awayTeamId;
}

export function getSlotPerspective(slot, teamId) {
  const homeTeamId = getSlotHomeTeamId(slot);
  const awayTeamId = getSlotOpponentTeamId(slot);
  const currentTeamId = String(teamId || "").trim();
  const isHome = !!currentTeamId && homeTeamId === currentTeamId;
  const isAway = !!currentTeamId && awayTeamId === currentTeamId;
  const opponentTeamId = isHome ? awayTeamId : isAway ? homeTeamId : awayTeamId || homeTeamId;

  return {
    homeTeamId,
    awayTeamId,
    isHome,
    isAway,
    opponentTeamId,
  };
}
