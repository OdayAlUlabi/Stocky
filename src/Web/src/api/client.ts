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

/** Structured error details extracted from an API response. */
export interface ApiErrorDetails {
  message: string;
  detail?: string;
  status?: number;
  /** Validation errors keyed by field name. */
  fieldErrors?: Record<string, string[]>;
}

/**
 * Extracts rich error details from any thrown value.
 * Handles ASP.NET Core Problem Details (including validation errors),
 * plain string bodies, and generic Error objects.
 */
export function getApiErrorDetails(e: unknown): ApiErrorDetails {
  if (e instanceof ApiError) {
    const body = e.body;
    const result: ApiErrorDetails = { message: e.message, status: e.status };

    if (body && typeof body === 'object') {
      const b = body as Record<string, unknown>;
      if (typeof b.detail === 'string' && b.detail) result.detail = b.detail;
      // ASP.NET validation errors: { errors: { field: string[] } }
      if (b.errors && typeof b.errors === 'object' && !Array.isArray(b.errors)) {
        const raw = b.errors as Record<string, unknown>;
        const fieldErrors: Record<string, string[]> = {};
        for (const [field, msgs] of Object.entries(raw)) {
          if (Array.isArray(msgs)) fieldErrors[field] = msgs.map(String);
        }
        if (Object.keys(fieldErrors).length) result.fieldErrors = fieldErrors;
      }
    }
    return result;
  }

  if (e instanceof Error) return { message: e.message };
  return { message: String(e) };
}

/**
 * Returns a human-readable error string. For toasts and simple displays.
 * Includes validation field errors concatenated when present.
 */
export function formatApiError(e: unknown): string {
  const details = getApiErrorDetails(e);
  const parts: string[] = [details.message];
  if (details.detail && details.detail !== details.message) parts.push(details.detail);
  if (details.fieldErrors) {
    for (const [field, msgs] of Object.entries(details.fieldErrors)) {
      parts.push(`${field}: ${msgs.join(', ')}`);
    }
  }
  return parts.join(' — ');
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
