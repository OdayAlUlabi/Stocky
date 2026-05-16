import { config } from './config';

export interface PortfolioDto {
  id: string;
  name: string;
  baseCurrency: string;
  createdAt: string;
}

async function http<T>(path: string, init: RequestInit = {}, token?: string): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set('Content-Type', 'application/json');
  if (token) headers.set('Authorization', `Bearer ${token}`);
  const res = await fetch(`${config.apiBaseUrl}${path}`, { ...init, headers });
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
  return res.status === 204 ? (undefined as T) : (await res.json() as T);
}

export const api = {
  listPortfolios: (token?: string) => http<PortfolioDto[]>('/api/portfolios', {}, token),
  createPortfolio: (body: { name: string; baseCurrency?: string }, token?: string) =>
    http<PortfolioDto>('/api/portfolios', { method: 'POST', body: JSON.stringify(body) }, token),
  health: () => http<{ status: string; utc: string }>('/health')
};
