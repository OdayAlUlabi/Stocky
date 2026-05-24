import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useEffect, useRef } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { config } from '../config';
import type { PriceTickDto } from './types';

/// M8 #1 — subscribe to the /hubs/prices SignalR hub and invalidate the
/// quotes query when ticks land. One shared connection per app instance.
let connection: HubConnection | null = null;
const subscribed = new Map<string, number>(); // symbol → refcount

async function ensureConnection() {
  if (connection && connection.state === HubConnectionState.Connected) return connection;
  if (!connection) {
    connection = new HubConnectionBuilder()
      .withUrl(`${config.apiBaseUrl}/hubs/prices`)
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
  }
  if (connection.state === HubConnectionState.Disconnected) {
    try { await connection.start(); } catch { /* ignore — will retry on next subscribe */ }
  }
  return connection;
}

export function usePriceTicks(symbols: string[], onTick?: (t: PriceTickDto) => void) {
  const qc = useQueryClient();
  const handlerRef = useRef(onTick);
  handlerRef.current = onTick;

  useEffect(() => {
    if (!symbols.length) return;
    let cancelled = false;
    const up = symbols.map(s => s.toUpperCase());

    const onPrice = (tick: PriceTickDto) => {
      handlerRef.current?.(tick);
      qc.invalidateQueries({ queryKey: ['quotes'], exact: false });
    };

    (async () => {
      const conn = await ensureConnection();
      if (cancelled) return;
      conn.off('price', onPrice);
      conn.on('price', onPrice);
      for (const s of up) subscribed.set(s, (subscribed.get(s) ?? 0) + 1);
      try { await conn.invoke('Subscribe', up); } catch { /* will reconnect */ }
    })();

    return () => {
      cancelled = true;
      const toDrop: string[] = [];
      for (const s of up) {
        const n = (subscribed.get(s) ?? 1) - 1;
        if (n <= 0) { subscribed.delete(s); toDrop.push(s); }
        else subscribed.set(s, n);
      }
      if (connection && connection.state === HubConnectionState.Connected && toDrop.length) {
        connection.invoke('Unsubscribe', toDrop).catch(() => { /* noop */ });
      }
    };
  }, [symbols.join(','), qc]);
}

/// M14 #92 — subscribe to a portfolio group on the same /hubs/prices hub.
/// Server emits "portfolioUpdated" when transactions or quote ticks affect
/// the portfolio. We invalidate the relevant TanStack-Query keys so the
/// Dashboard refreshes within ~1s.
const portfolioSubscribed = new Map<string, number>();

export function usePortfolioStream(portfolioId: string | undefined) {
  const qc = useQueryClient();

  useEffect(() => {
    if (!portfolioId) return;
    let cancelled = false;

    const onUpdate = (payload: { portfolioId: string }) => {
      const pid = payload?.portfolioId;
      qc.invalidateQueries({ queryKey: ['dashboard'] });
      if (pid) {
        qc.invalidateQueries({ queryKey: ['portfolios', pid, 'holdings'] });
        qc.invalidateQueries({ queryKey: ['portfolios', pid, 'transactions'] });
        qc.invalidateQueries({ queryKey: ['portfolios', pid, 'allocation'] });
        qc.invalidateQueries({ queryKey: ['portfolios', pid, 'history'] });
      }
    };

    (async () => {
      const conn = await ensureConnection();
      if (cancelled) return;
      conn.off('portfolioUpdated', onUpdate);
      conn.on('portfolioUpdated', onUpdate);
      portfolioSubscribed.set(portfolioId, (portfolioSubscribed.get(portfolioId) ?? 0) + 1);
      try { await conn.invoke('SubscribePortfolio', portfolioId); } catch { /* will retry */ }
    })();

    return () => {
      cancelled = true;
      const n = (portfolioSubscribed.get(portfolioId) ?? 1) - 1;
      if (n <= 0) {
        portfolioSubscribed.delete(portfolioId);
        if (connection && connection.state === HubConnectionState.Connected) {
          connection.invoke('UnsubscribePortfolio', portfolioId).catch(() => { /* noop */ });
        }
      } else {
        portfolioSubscribed.set(portfolioId, n);
      }
    };
  }, [portfolioId, qc]);
}
