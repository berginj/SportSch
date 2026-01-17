import { test, expect } from '@playwright/test';

/**
 * Authentication Flow E2E Tests
 *
 * Tests the user authentication and session management flows.
 */

test.describe('Authentication', () => {
  test('should load the home page', async ({ page }) => {
    await page.goto('/');

    // Check that the page loaded
    await expect(page).toHaveTitle(/SportSch/i);

    // Should see TopNav
    await expect(page.locator('header.topnav')).toBeVisible();
  });

  test('should display user info when authenticated', async ({ page, context }) => {
    // Mock authenticated user in localStorage
    await context.addInitScript(() => {
      localStorage.setItem('userId', 'test-user-123');
      localStorage.setItem('userEmail', 'test@example.com');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');

    // Should see user email in TopNav
    await expect(page.locator('.topnav__user')).toContainText('test@example.com');
  });

  test('should handle unauthenticated user', async ({ page }) => {
    await page.goto('/');

    // Should not crash, might show login prompt or guest mode
    await expect(page.locator('header.topnav')).toBeVisible();
  });

  test('should persist league selection', async ({ page, context }) => {
    await context.addInitScript(() => {
      localStorage.setItem('userId', 'test-user-123');
      localStorage.setItem('userEmail', 'test@example.com');
    });

    await page.goto('/');

    // Select a league from dropdown if available
    const leagueSelect = page.locator('.topnav__league-select');
    if (await leagueSelect.isVisible()) {
      await leagueSelect.selectOption({ index: 1 });

      // Wait for localStorage to update
      await page.waitForTimeout(500);

      // Reload and verify league is still selected
      await page.reload();
      const selectedValue = await leagueSelect.inputValue();
      expect(selectedValue).not.toBe('');
    }
  });
});

test.describe('Navigation', () => {
  test('should navigate between tabs', async ({ page, context }) => {
    await context.addInitScript(() => {
      localStorage.setItem('userId', 'test-user-123');
      localStorage.setItem('userEmail', 'test@example.com');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');

    // Click Calendar tab
    await page.click('button:has-text("Calendar")');
    await expect(page.locator('text=Calendar')).toBeVisible();

    // Click Manage tab if user has permission
    const manageTab = page.locator('button:has-text("Manage")');
    if (await manageTab.isVisible()) {
      await manageTab.click();
      await expect(page.locator('text=Manage')).toBeVisible();
    }
  });

  test('should show admin tab for admin users', async ({ page, context }) => {
    await context.addInitScript(() => {
      localStorage.setItem('userId', 'admin-user');
      localStorage.setItem('userEmail', 'admin@example.com');
      localStorage.setItem('leagueId', 'test-league');
      localStorage.setItem('isGlobalAdmin', 'true');
    });

    await page.goto('/');

    // Admin tab should be visible
    await expect(page.locator('button:has-text("Admin")')).toBeVisible();
  });
});
