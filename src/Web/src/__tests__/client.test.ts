/**
 * Tests for src/api/client.ts — request() function
 *
 * Uses vi.stubGlobal to replace fetch so no network calls are made and
 * no MSW server is needed for these pure-logic unit tests.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { request } from '../api/client';

// ── helpers ──────────────────────────────────────────────────────────────────

function makeResponse(status: number, body: unknown = null, ok?: boolean) {
  const text = body === null ? '' : JSON.stringify(body);
  return {
    ok: ok ?? status < 400,
    status,
    statusText: status === 200 ? 'OK' : status === 401 ? 'Unauthorized' : 'Error',
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(text),
  } as unknown as Response;
}

// ── tests ─────────────────────────────────────────────────────────────────────

describe('request()', () => {
  const fetchSpy = vi.fn<typeof fetch>();

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchSpy);
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it('sets Content-Type when body is provided', async () => {
    fetchSpy.mockResolvedValue(makeResponse(200, { ok: true }));

    await request('/api/test', { method: 'POST', body: { name: 'x' } });

    const [, init] = fetchSpy.mock.calls[0];
    const headers = init?.headers as Headers;
    expect(headers.get('Content-Type')).toBe('application/json');
  });

  it('returns undefined for 204 No Content', async () => {
    fetchSpy.mockResolvedValue(makeResponse(204));

    const result = await request('/api/test', { method: 'DELETE' });

    expect(result).toBeUndefined();
  });

  it('appends query string correctly', async () => {
    fetchSpy.mockResolvedValue(makeResponse(200, []));

    await request('/api/test', { query: { page: 1, q: 'hello', skip: undefined } });

    const [url] = fetchSpy.mock.calls[0];
    expect(String(url)).toContain('page=1');
    expect(String(url)).toContain('q=hello');
    expect(String(url)).not.toContain('skip');
  });

  it('throws ApiError with correct status on non-OK response', async () => {
    fetchSpy.mockResolvedValue(makeResponse(404, { title: 'Not Found' }));

    await expect(request('/api/missing')).rejects.toMatchObject({
      status: 404,
    });
  });

});
