# E2E Testing with Playwright

This document describes the end-to-end (E2E) testing setup using Playwright.

## Overview

E2E tests verify the application works correctly from a user's perspective by automating browser interactions. These tests run against the full stack (frontend + API).

## Setup

### Prerequisites

- Node.js >= 22.12.0
- Frontend dev server (automatically started)
- API server (optional, can be mocked)

### Installation

Playwright is already installed. To install browsers:

```bash
npx playwright install
```

This installs Chromium, Firefox, and WebKit browsers.

## Running Tests

### Run All E2E Tests

```bash
npm run test:e2e
```

This runs tests in headless mode across all configured browsers (Chromium, Firefox, WebKit, Mobile Chrome, Mobile Safari).

### Interactive UI Mode

```bash
npm run test:e2e:ui
```

Opens Playwright's interactive UI for:
- Running tests with live preview
- Debugging test failures
- Inspecting DOM snapshots
- Time-travel debugging

### Debug Mode

```bash
npm run test:e2e:debug
```

Runs tests in debug mode with:
- Browser visible
- Playwright Inspector
- Step-through debugging

### View Test Report

```bash
npm run test:e2e:report
```

Opens the HTML report with:
- Test results and screenshots
- Video recordings of failures
- Trace files for debugging

## Test Structure

```
e2e/
├── auth.spec.js           # Authentication flows
├── calendar.spec.js       # Calendar/slots features
└── ...                    # Additional feature tests
```

## Writing Tests

### Basic Test Structure

```javascript
import { test, expect } from '@playwright/test';

test.describe('Feature Name', () => {
  test.beforeEach(async ({ page, context }) => {
    // Setup: authenticate, mock data, etc.
    await context.addInitScript(() => {
      localStorage.setItem('userId', 'test-user');
      localStorage.setItem('leagueId', 'test-league');
    });

    await page.goto('/');
  });

  test('should do something', async ({ page }) => {
    // Arrange: navigate, fill forms
    await page.click('button:has-text("Calendar")');

    // Act: perform user action
    await page.fill('input[name="teamName"]', 'Test Team');
    await page.click('button:has-text("Save")');

    // Assert: verify expected result
    await expect(page.locator('text=Test Team')).toBeVisible();
  });
});
```

### Authentication Setup

```javascript
test.beforeEach(async ({ page, context }) => {
  // Mock authenticated user
  await context.addInitScript(() => {
    localStorage.setItem('userId', 'test-user-123');
    localStorage.setItem('userEmail', 'test@example.com');
    localStorage.setItem('leagueId', 'test-league');
  });

  await page.goto('/');
});
```

### API Mocking

```javascript
test('should handle API response', async ({ page }) => {
  // Mock API endpoint
  await page.route('**/api/slots', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        data: {
          items: [
            { slotId: '1', gameDate: '2026-06-01' },
          ],
        },
      }),
    });
  });

  await page.goto('/');
  await page.click('button:has-text("Calendar")');

  // Verify mocked data appears
  await expect(page.locator('text=2026-06-01')).toBeVisible();
});
```

### Error Handling Tests

```javascript
test('should handle 500 error gracefully', async ({ page }) => {
  await page.route('**/api/**', (route) => {
    route.fulfill({
      status: 500,
      body: JSON.stringify({
        error: {
          code: 'INTERNAL_ERROR',
          message: 'Server error',
        },
      }),
    });
  });

  await page.goto('/');

  // App should not crash
  await expect(page.locator('header')).toBeVisible();

  // Error message might appear
  await expect(page.locator('text=/error|failed/i')).toBeVisible({
    timeout: 5000,
  }).catch(() => {
    // Error message is optional
  });
});
```

### Responsive Testing

```javascript
test('should work on mobile', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 667 });

  await page.goto('/');

  // Verify mobile layout
  await expect(page.locator('.mobile-menu-button')).toBeVisible();
});
```

## Configuration

### Browser Configuration

Edit `playwright.config.js` to customize browsers:

```javascript
projects: [
  { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  { name: 'firefox', use: { ...devices['Desktop Firefox'] } },
  // Add more browsers/devices
],
```

### Base URLs

```javascript
use: {
  baseURL: process.env.BASE_URL || 'http://localhost:5173',
  apiURL: process.env.API_URL || 'http://localhost:7071',
},
```

### Timeouts

```javascript
use: {
  actionTimeout: 10000,        // 10s for actions like click, fill
  navigationTimeout: 30000,    // 30s for page navigations
},
timeout: 60000,                // 60s global test timeout
```

## Best Practices

### 1. Use Data-Test-IDs

```jsx
// Component
<button data-testid="save-button">Save</button>

// Test
await page.click('[data-testid="save-button"]');
```

### 2. Wait for Network Idle

```javascript
await page.goto('/', { waitUntil: 'networkidle' });
```

### 3. Use Soft Assertions

```javascript
await expect.soft(page.locator('.warning')).toBeVisible();
await expect.soft(page.locator('.error')).not.toBeVisible();
// Test continues even if soft assertions fail
```

### 4. Isolate Tests

```javascript
test.describe.configure({ mode: 'parallel' });

test('test 1', async ({ page }) => {
  // Runs independently
});

test('test 2', async ({ page }) => {
  // Runs independently
});
```

### 5. Use Page Object Model

```javascript
// pages/CalendarPage.js
export class CalendarPage {
  constructor(page) {
    this.page = page;
    this.calendarTab = page.locator('button:has-text("Calendar")');
    this.divisionFilter = page.locator('select[name="division"]');
  }

  async navigate() {
    await this.calendarTab.click();
  }

  async filterByDivision(division) {
    await this.divisionFilter.selectOption(division);
  }
}

// test
import { CalendarPage } from './pages/CalendarPage';

test('filter calendar', async ({ page }) => {
  const calendar = new CalendarPage(page);
  await calendar.navigate();
  await calendar.filterByDivision('10U');
});
```

## Debugging

### Visual Debugging

```bash
npm run test:e2e:debug
```

Opens Playwright Inspector with:
- Step through tests line by line
- Hover over locators to highlight elements
- View action log
- Inspect DOM snapshots

### Screenshots

Automatically captured on failure. Access in the HTML report.

### Video Recording

Recorded for failed tests. View in the HTML report:

```bash
npm run test:e2e:report
```

### Trace Viewer

Captures detailed execution trace on failure:

```bash
npx playwright show-trace trace.zip
```

## CI/CD Integration

### GitHub Actions

```yaml
name: E2E Tests

on: [push, pull_request]

jobs:
  e2e:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3

      - uses: actions/setup-node@v3
        with:
          node-version: 22

      - run: npm ci

      - run: npx playwright install --with-deps

      - run: npm run test:e2e

      - uses: actions/upload-artifact@v3
        if: failure()
        with:
          name: playwright-report
          path: playwright-report/
          retention-days: 7
```

### Azure Pipelines

```yaml
- task: NodeTool@0
  inputs:
    versionSpec: '22.x'

- script: npm ci
  displayName: 'Install dependencies'

- script: npx playwright install --with-deps
  displayName: 'Install Playwright browsers'

- script: npm run test:e2e
  displayName: 'Run E2E tests'

- task: PublishTestResults@2
  condition: always()
  inputs:
    testResultsFormat: 'JUnit'
    testResultsFiles: 'test-results/junit.xml'
```

## Troubleshooting

### "Timeout waiting for page"

**Solution:**
- Increase timeout: `test.setTimeout(60000)`
- Wait for specific elements: `await page.waitForSelector('.loaded')`
- Check if dev server started: `npm run dev`

### "Element not found"

**Solution:**
- Use `waitForSelector`: `await page.waitForSelector('.my-element')`
- Check element selector: `await page.locator('.my-element').first()`
- Use more specific selectors: `[data-testid="my-element"]`

### Tests pass locally but fail in CI

**Solution:**
- Use `waitForLoadState`: `await page.waitForLoadState('networkidle')`
- Increase timeouts for slower CI environments
- Use `test.slow()` for known slow tests
- Disable browser extensions in CI

## Test Coverage

Current E2E test coverage:

- ✅ Authentication flows
- ✅ Navigation between tabs
- ✅ Calendar view loading
- ✅ Division filtering
- ✅ API error handling
- ✅ Rate limiting handling
- ✅ Responsive design (mobile/tablet)

### Recommended Additional Tests

- Slot creation and management
- Team management (CRUD operations)
- League administration
- Access request approval workflow
- Schedule generation

## Performance Testing

Playwright can measure performance:

```javascript
test('should load calendar quickly', async ({ page }) => {
  const start = Date.now();

  await page.goto('/');
  await page.click('button:has-text("Calendar")');
  await page.waitForSelector('.calendar-loaded');

  const duration = Date.now() - start;
  expect(duration).toBeLessThan(3000); // 3 seconds
});
```

## Support

For Playwright issues:
- Documentation: https://playwright.dev
- Check test report: `npm run test:e2e:report`
- Run in debug mode: `npm run test:e2e:debug`
- Review trace files in the report
