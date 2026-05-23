import { test, expect } from '@playwright/test';

/**
 * API health smoke tests.
 * These call the backend API directly (not via the browser), so they run
 * independently of the frontend dev server.
 */

const API_BASE = process.env.API_BASE_URL ?? 'http://localhost:5170';

test.describe('API health endpoint', () => {
  test('GET /health returns 200', async ({ request }) => {
    const response = await request.get(`${API_BASE}/health`);
    expect(response.status()).toBe(200);
  });

  test('GET /health response body is healthy', async ({ request }) => {
    const response = await request.get(`${API_BASE}/health`);
    const text = await response.text();
    expect(text.toLowerCase()).toContain('healthy');
  });
});
