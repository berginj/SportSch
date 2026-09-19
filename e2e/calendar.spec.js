import { test, expect } from "@playwright/test";
import { signIn, calendarUrl } from "./session";

test("calendar is usable on a phone and keeps filters out of the way", async ({ page, context }, testInfo) => {
  await signIn(context);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(calendarUrl);
  // The status summary lives inside the collapsed mobile filter panel. Its
  // presence proves the calendar loaded; it is intentionally not required to
  // be visible when the filters are closed.
  await expect(page.getByText("Showing calendar items for e2e-league-a")).toBeAttached({ timeout: 15000 });
  await expect(page.getByRole("button", { name: "Request Practice Space" })).toBeVisible();
  await expect(page.locator("details.calendarFilters")).not.toHaveAttribute("open", "");
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await testInfo.attach("mobile-calendar", { body: await page.screenshot({ fullPage: true }), contentType: "image/png" });
});

test("commissioner edit carries a version and stale edits are rejected", async ({ page, context }, testInfo) => {
  test.skip(testInfo.project.name === "mobile-chromium", "Versioned edit is covered in the desktop browser profile; mobile profile covers layout.");
  await signIn(context, "LeagueAdmin");
  await page.goto(calendarUrl);
  await expect(page.getByText("Showing calendar items for e2e-league-a")).toBeAttached({ timeout: 15000 });
  const response = await context.request.get("/api/slots?division=10U", { headers: { "x-league-id": "e2e-league-a" } });
  expect(response.ok()).toBe(true);
  const data = (await response.json()).data;
  const slot = (data.items || data).find((item) => item.slotId === "e2e-game");
  expect(slot.etag).toBeTruthy();
  const stale = await context.request.patch("/api/slots/10U/e2e-game", {
    headers: { "x-league-id": "e2e-league-a", "If-Match": "stale" }, data: { startTime: "18:30" }
  });
  expect(stale.status()).toBe(409);
  expect((await stale.json()).error.code).toBe("STALE_SLOT");
});

test("coach can create a bounded open game opportunity", async ({ context }, testInfo) => {
  test.skip(testInfo.project.name === "mobile-chromium", "Slot creation workflow is covered in the desktop browser profile.");
  await signIn(context, "Coach");

  const day = new Date(Date.now() + 14 * 24 * 60 * 60 * 1000);
  const gameDate = day.toISOString().slice(0, 10);
  const response = await context.request.post("/api/slots", {
    headers: { "x-league-id": "e2e-league-a" },
    data: {
      division: "10U",
      offeringTeamId: "HOME",
      gameDate,
      startTime: "20:00",
      endTime: "21:30",
      fieldKey: "PARK/ONE",
      parkName: "Test park",
      fieldName: "Diamond 1",
      gameType: "Swap",
    },
  });

  expect(response.status()).toBe(201);
  const body = await response.json();
  expect(body.data.status).toBe("Open");
  expect(body.data.slotId).toBeTruthy();
  expect(body.data.division).toBe("10U");
});
