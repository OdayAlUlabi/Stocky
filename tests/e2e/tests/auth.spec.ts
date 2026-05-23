import { test, expect } from '@playwright/test';

/**
 * Auth flow smoke tests.
 * The frontend redirects unauthenticated users to Google OAuth.
 * These tests verify the public-facing shell of the app loads and
 * that protected routes are not accessible without a token.
 */

test.describe('Authentication gate', () => {
  test('home page loads without errors', async ({ page }) => {
    // Should load the React shell (200, not a network error)
    const response = await page.goto('/');
    expect(response?.status()).toBeLessThan(400);
  });

  test('unauthenticated user sees login UI or is redirected', async ({ page }) => {
    await page.goto('/');

    // Give the React app time to render and decide whether to redirect
    await page.waitForTimeout(1000);

    const url = page.url();
    const bodyText = await page.locator('body').innerText();

    // Either the page contains a "sign in" call-to-action OR the app
    // redirected to Google OAuth (accounts.google.com)
    const hasLoginText = /sign in|log in|login|get started/i.test(bodyText);
    const isGoogleAuth = url.includes('accounts.google.com');

    expect(hasLoginText || isGoogleAuth).toBeTruthy();
  });
});
