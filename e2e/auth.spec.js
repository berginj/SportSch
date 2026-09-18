import { test, expect } from "@playwright/test";
import { signIn, calendarUrl } from "./session";

test("unauthenticated API requests return a standard error", async ({ request }) => {
  const response = await request.get("/api/me");
  expect(response.status()).toBe(401);
  expect((await response.json()).error.code).toBe("UNAUTHENTICATED");
});

test("real session survives refresh and scopes league selection", async ({ page, context }) => {
  await signIn(context);
  await page.goto(calendarUrl);
  await expect(page.getByText("Showing calendar items for e2e-league-a")).toBeVisible();
  await page.reload();
  await expect(page.getByText("Showing calendar items for e2e-league-a")).toBeVisible();
  expect(await page.evaluate(() => localStorage.getItem("gameswap_leagueId"))).toBe("e2e-league-a");
});

test("a forged localStorage role cannot grant administrator access", async ({ page, context, request }) => {
  await signIn(context, "Viewer");
  await page.addInitScript(() => localStorage.setItem("isGlobalAdmin", "true"));
  await page.goto(calendarUrl);
  await expect(page.getByRole("link", { name: "Manage league scheduling" })).toHaveCount(0);
  const response = await request.get("/api/slots?division=10U", { headers: { "x-user-id": "e2e-Viewer", "x-league-id": "not-a-member" } });
  expect(response.status()).toBe(403);
});
