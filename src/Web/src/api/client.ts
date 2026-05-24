import { config } from '../config';

export class ApiError extends Error {
  status: number;
  body: unknown;
  constructor(status: number, body: unknown, message: string) {
    super(message);
    this.status = status;
    this.body = body;
  }
}

export interface RequestOptions {
  method?: string;
  body?: unknown;
  query?: Record<string, string | number | boolean | undefined | null>;
}

function qs(query?: RequestOptions['query']) {
  if (!query) return '';
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v === undefined || v === null || v === '') continue;
    params.set(k, String(v));
  }
  const s = params.toString();
  return s ? `?${s}` : '';
}

export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const headers = new Headers();
  if (opts.body !== undefined) headers.set('Content-Type', 'application/json');

  const res = await fetch(`${config.apiBaseUrl}${path}${qs(opts.query)}`, {
    method: opts.method ?? 'GET',
    headers,
    body: opts.body === undefined ? undefined : JSON.stringify(opts.body)
  });

  if (!res.ok) {
    let body: unknown = null;
    const text = await res.text().catch(() => '');
    try { body = text ? JSON.parse(text) : null; } catch { body = text; }
    const message = typeof body === 'string' && body
      ? body
      : (body && typeof body === 'object' && 'title' in (body as Record<string, unknown>)
        ? String((body as Record<string, unknown>).title)
        : `${res.status} ${res.statusText}`);
    throw new ApiError(res.status, body, message);
  }
  if (res.status === 204) return undefined as T;
  return await res.json() as T;
}
