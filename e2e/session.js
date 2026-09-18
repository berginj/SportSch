export async function signIn(context, role = "Coach", leagueId = "e2e-league-a") {
  // The actual API accepts these only in Development on localhost. Local storage carries scope, never identity.
  await context.setExtraHTTPHeaders({ "x-user-id": `e2e-${role}`, "x-user-email": `${role}@example.test` });
  await context.addInitScript((league) => localStorage.setItem("gameswap_leagueId", league), leagueId);
}

export const calendarUrl = "/?dateFrom=2026-10-01&dateTo=2026-10-31&showSlots=1&showEvents=1#calendar";
