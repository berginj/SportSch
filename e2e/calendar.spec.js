import { test, expect } from '@playwright/test';

/**
 * Calendar/Slots E2E Tests
 *
 * Tests the main calendar view and slot management functionality.
 */

test.describe('Calendar View', () => {
  test.beforeEach(async ({ page, context }) => {
    // Setup authenticated user
    await context.addInitScript(() => {
      localStorage.setItem('userId', 'test-user-123');
      localStorage.setItem('userEmail', 'coach@example.com');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');
  });

  test('should navigate to calendar view', async ({ page }) => {
    // Click Calendar tab
    await page.click('button:has-text("Calendar")');

    // Should see calendar content
    await expect(page.locator('text=/calendar|schedule|slots/i')).toBeVisible({
      timeout: 10000,
    });
  });

  test('should display league picker', async ({ page }) => {
    await page.click('button:has-text("Calendar")');

    // League picker should be visible
    const leagueSelect = page.locator('.topnav__league-select');
    await expect(leagueSelect).toBeVisible();
  });

  test('should filter by division', async ({ page }) => {
    await page.click('button:has-text("Calendar")');

    // Look for division filter/selector if available
    const divisionFilter = page.locator('select, [role="combobox"]').filter({
      hasText: /division|div|10u|12u/i,
    }).first();

    if (await divisionFilter.isVisible({ timeout: 5000 }).catch(() => false)) {
      // Select a division
      await divisionFilter.click();
      await page.keyboard.press('ArrowDown');
      await page.keyboard.press('Enter');

      // Wait for content to update
      await page.waitForTimeout(1000);
    }
  });

  test('should handle empty state gracefully', async ({ page, context }) => {
    // Set up a league with no data
    await context.addInitScript(() => {
      localStorage.setItem('userId', 'new-user');
      localStorage.setItem('userEmail', 'new@example.com');
      localStorage.setItem('leagueId', 'empty-league');
    });

    await page.goto('/');
    await page.click('button:has-text("Calendar")');

    // Should not crash, might show empty state message
    await page.waitForTimeout(2000);
    await expect(page.locator('header.topnav')).toBeVisible();
  });
});

test.describe('API Integration', () => {
  test('should handle API errors gracefully', async ({ page, context }) => {
    // Intercept API calls and simulate error
    await page.route('**/api/**', (route) => {
      route.fulfill({
        status: 500,
        contentType: 'application/json',
        body: JSON.stringify({
          error: {
            code: 'INTERNAL_ERROR',
            message: 'Internal Server Error',
          },
        }),
      });
    });

    await context.addInitScript(() => {
      localStorage.setItem('userId', 'test-user');
      localStorage.setItem('userEmail', 'test@example.com');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');
    await page.click('button:has-text("Calendar")');

    // Should show error but not crash
    await page.waitForTimeout(2000);
    await expect(page.locator('header.topnav')).toBeVisible();
  });

  test('should handle rate limiting', async ({ page, context }) => {
    // Intercept API calls and simulate rate limit
    await page.route('**/api/**', (route) => {
      route.fulfill({
        status: 429,
        headers: {
          'X-RateLimit-Limit': '100',
          'X-RateLimit-Remaining': '0',
          'Retry-After': '60',
        },
        contentType: 'application/json',
        body: JSON.stringify({
          error: {
            code: 'RATE_LIMIT_EXCEEDED',
            message: 'Rate limit exceeded. Maximum 100 requests per minute allowed.',
          },
        }),
      });
    });

    await context.addInitScript(() => {
      localStorage.setItem('userId', 'test-user');
      localStorage.setItem('userEmail', 'test@example.com');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');

    // App should handle rate limit gracefully
    await page.waitForTimeout(2000);
    await expect(page.locator('header.topnav')).toBeVisible();
  });
});

test.describe('Responsive Design', () => {
  test('should work on mobile viewport', async ({ page, context }) => {
    await page.setViewportSize({ width: 375, height: 667 }); // iPhone SE

    await context.addInitScript(() => {
      localStorage.setItem('userId', 'mobile-user');
      localStorage.setItem('userEmail', 'mobile@example.com');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');

    // TopNav should be visible and functional on mobile
    await expect(page.locator('header.topnav')).toBeVisible();

    // Navigation should work
    await page.click('button:has-text("Calendar")');
    await page.waitForTimeout(1000);
  });

  test('should work on tablet viewport', async ({ page, context }) => {
    await page.setViewportSize({ width: 768, height: 1024 }); // iPad

    await context.addInitScript(() => {
      localStorage.setItem('userId', 'tablet-user');
      localStorage.setItem('userEmail', 'tablet@example.com');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');

    await expect(page.locator('header.topnav')).toBeVisible();
    await page.click('button:has-text("Calendar")');
  });
});
