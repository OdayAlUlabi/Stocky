import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query';
import { useApiToken } from '../auth/useApiToken';
import { request } from './client';
import { config } from '../config';
import type {
  AllocationDto,
  AlertDto,
  CapitalGainsDto,
  CreateAlertRequest,
  CreateTransactionRequest,
  DashboardDto,
  DividendRowDto,
  EarningsEventDto,
  HoldingDto,
  ImportResultDto,
  InstrumentDto,
  NewsItemDto,
  PerformanceDto,
  PortfolioDto,
  PortfolioHistoryDto,
  PortfolioAnalyticsDto,
  PositionDetailDto,
  CorrelationDto,
  QuoteDto,
  ReportSummaryDto,
  TransactionDto,
  UpdateAlertRequest,
  UserSettingsDto,
  WatchlistDto,
  WashSaleReportDto,
  RebalanceReportDto,
  RebalanceTargetDto,
  ScreenerFacetsDto,
  ScreenerQuery,
  ScreenerResultDto,
  CashTransactionDto,
  CreateCashTransactionRequest,
  CashBalanceDto,
  PositionNoteDto,
  CreatePositionNoteRequest,
  UpdatePositionNoteRequest,
  AuditEntryDto,
  ModelPortfolioTemplateDto,
  ApplyTemplateRequest
} from './types';

type Opts<T> = Omit<UseQueryOptions<T, Error, T, readonly unknown[]>, 'queryKey' | 'queryFn'>;

export function usePortfolios(opts?: Opts<PortfolioDto[]>) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios'] as const,
    queryFn: async () => request<PortfolioDto[]>('/api/portfolios', { token: await getToken() }),
    ...opts
  });
}

export function useCreatePortfolio() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: { name: string; baseCurrency?: string }) =>
      request<PortfolioDto>('/api/portfolios', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['portfolios'] })
  });
}

export function useDeletePortfolio() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/portfolios/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['portfolios'] })
  });
}

export function useUpdatePortfolio() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: { name: string; baseCurrency: string; costBasisMethod?: string } }) =>
      request<PortfolioDto>(`/api/portfolios/${id}`, { method: 'PUT', body, token: await getToken() }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['portfolios'] });
      qc.invalidateQueries({ queryKey: ['portfolios', vars.id, 'capital-gains'] });
      qc.invalidateQueries({ queryKey: ['portfolios', vars.id, 'reports'] });
    }
  });
}

export function useHoldings(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'holdings'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<HoldingDto[]>(`/api/portfolios/${portfolioId}/holdings`, { token: await getToken() })
  });
}

export function useTransactions(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'transactions'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<TransactionDto[]>(`/api/portfolios/${portfolioId}/transactions`, { token: await getToken() })
  });
}

function invalidatePortfolio(qc: ReturnType<typeof useQueryClient>, portfolioId: string) {
  qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'transactions'] });
  qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'holdings'] });
  qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'position'] });
  qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'allocation'] });
  qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'capital-gains'] });
  qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'reports'] });
  qc.invalidateQueries({ queryKey: ['dashboard'] });
}

export function useCreateTransaction(portfolioId: string) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: CreateTransactionRequest) =>
      request<TransactionDto>(`/api/portfolios/${portfolioId}/transactions`, { method: 'POST', body, token: await getToken() }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useUpdateTransaction(portfolioId: string) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: CreateTransactionRequest }) =>
      request<TransactionDto>(`/api/portfolios/${portfolioId}/transactions/${id}`, { method: 'PUT', body, token: await getToken() }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useDeleteTransaction(portfolioId: string) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/portfolios/${portfolioId}/transactions/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useImportTransactions(portfolioId: string) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (csv: string) =>
      request<ImportResultDto>(`/api/portfolios/${portfolioId}/transactions/import`, {
        method: 'POST',
        body: { csv },
        token: await getToken()
      }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useDashboard(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['dashboard', portfolioId ?? 'all'] as const,
    queryFn: async () => request<DashboardDto>('/api/dashboard', { query: { portfolioId }, token: await getToken() })
  });
}

export function useWatchlists() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['watchlists'] as const,
    queryFn: async () => request<WatchlistDto[]>('/api/watchlists', { token: await getToken() })
  });
}

export function useCreateWatchlist() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: { name: string }) =>
      request<WatchlistDto>('/api/watchlists', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['watchlists'] })
  });
}

export function useAddWatchlistItem(watchlistId: string) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: { symbol: string }) =>
      request(`/api/watchlists/${watchlistId}/items`, { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['watchlists'] })
  });
}

export function useRemoveWatchlistItem(watchlistId: string) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (itemId: string) =>
      request<void>(`/api/watchlists/${watchlistId}/items/${itemId}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['watchlists'] })
  });
}

export function useQuotes(symbols: string[]) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['quotes', [...symbols].sort().join(',')] as const,
    enabled: symbols.length > 0,
    queryFn: async () => request<QuoteDto[]>('/api/quotes', { query: { symbols: symbols.join(',') }, token: await getToken() })
  });
}

export function useSecuritySearch(q: string) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['securities-search', q] as const,
    enabled: q.length >= 1,
    queryFn: async () => request<InstrumentDto[]>('/api/securities/search', { query: { q }, token: await getToken() })
  });
}

export function usePositionDetail(portfolioId: string | undefined, symbol: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'position', symbol] as const,
    enabled: Boolean(portfolioId && symbol),
    queryFn: async () => request<PositionDetailDto>(`/api/portfolios/${portfolioId}/positions/${symbol}`, { token: await getToken() })
  });
}

export function useReportSummary(portfolioId: string | undefined, from?: string, to?: string) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'reports', 'summary', from ?? '', to ?? ''] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<ReportSummaryDto>(`/api/portfolios/${portfolioId}/reports/summary`, { query: { from, to }, token: await getToken() })
  });
}

export function useDividends(portfolioId: string | undefined, year?: number) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'reports', 'dividends', year ?? 'all'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<DividendRowDto[]>(`/api/portfolios/${portfolioId}/reports/dividends`, { query: { year }, token: await getToken() })
  });
}

export function usePerformance(portfolioId: string | undefined, days = 90) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'performance', days] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PerformanceDto>(`/api/portfolios/${portfolioId}/performance-series`, { query: { days }, token: await getToken() })
  });
}

export function usePortfolioHistory(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'history'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PortfolioHistoryDto>(`/api/portfolios/${portfolioId}/history`, { token: await getToken() })
  });
}

export function useAnalytics(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'analytics'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PortfolioAnalyticsDto>(`/api/portfolios/${portfolioId}/analytics`, { token: await getToken() })
  });
}

export function useCorrelation(portfolioId: string | undefined, days = 90) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'correlation', days] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<CorrelationDto>(`/api/portfolios/${portfolioId}/correlation`, { query: { days }, token: await getToken() })
  });
}

export function usePortfolioAnalytics(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'analytics'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PortfolioAnalyticsDto>(`/api/portfolios/${portfolioId}/analytics`, { token: await getToken() })
  });
}

export function useAllocation(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'allocation'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<AllocationDto>(`/api/portfolios/${portfolioId}/allocation`, { token: await getToken() })
  });
}

export function useCapitalGains(portfolioId: string | undefined, year?: number) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'capital-gains', year ?? 'current'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<CapitalGainsDto>(`/api/portfolios/${portfolioId}/capital-gains`, { query: { year }, token: await getToken() })
  });
}

export function useWashSales(portfolioId: string | undefined, year?: number) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'wash-sales', year ?? 'current'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<WashSaleReportDto>(`/api/portfolios/${portfolioId}/wash-sales`, { query: { year }, token: await getToken() })
  });
}

export function useRebalance(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'rebalance'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<RebalanceReportDto>(`/api/portfolios/${portfolioId}/rebalance`, { token: await getToken() })
  });
}

export function useRebalanceTargets(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'rebalance', 'targets'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<RebalanceTargetDto[]>(`/api/portfolios/${portfolioId}/rebalance/targets`, { token: await getToken() })
  });
}

export function useSaveRebalanceTargets(portfolioId: string) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (targets: RebalanceTargetDto[]) =>
      request<RebalanceTargetDto[]>(`/api/portfolios/${portfolioId}/rebalance/targets`, {
        method: 'PUT', body: targets, token: await getToken()
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'rebalance'] });
    }
  });
}

export function useScreenerFacets() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['screener', 'facets'] as const,
    queryFn: async () => request<ScreenerFacetsDto>('/api/securities/screener/facets', { token: await getToken() }),
    staleTime: 5 * 60 * 1000
  });
}

export function useScreener(query: ScreenerQuery, enabled = true) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['screener', query] as const,
    enabled,
    queryFn: async () => request<ScreenerResultDto>('/api/securities/screener', {
      query: query as Record<string, string | number | undefined>,
      token: await getToken()
    })
  });
}

export function useAlerts(status?: string) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['alerts', status ?? 'all'] as const,
    queryFn: async () => request<AlertDto[]>('/api/alerts', { query: { status }, token: await getToken() })
  });
}

export function useCreateAlert() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: CreateAlertRequest) =>
      request<AlertDto>('/api/alerts', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useUpdateAlert() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: UpdateAlertRequest }) =>
      request<AlertDto>(`/api/alerts/${id}`, { method: 'PUT', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useDeleteAlert() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/alerts/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

// M10 — Advanced Alerts
import type { AlertEventDto, SnoozeAlertRequest, InsiderEventDto, InsiderClusterDto } from './types';

export function useAlertHistory(take = 200) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['alerts', 'history', take] as const,
    queryFn: async () => request<AlertEventDto[]>('/api/alerts/history', { query: { take }, token: await getToken() })
  });
}

export function useSnoozeAlert() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: SnoozeAlertRequest }) =>
      request<AlertDto>(`/api/alerts/${id}/snooze`, { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useReactivateAlert() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<AlertDto>(`/api/alerts/${id}/reactivate`, { method: 'POST', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useInsiderEvents(symbol: string, days = 30) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['insider-events', symbol, days] as const,
    enabled: !!symbol,
    queryFn: async () => request<InsiderEventDto[]>(`/api/insider-events/${encodeURIComponent(symbol)}`, { query: { days }, token: await getToken() })
  });
}

export function useInsiderCluster(symbol: string, days = 30) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['insider-events', 'cluster', symbol, days] as const,
    enabled: !!symbol,
    queryFn: async () => request<InsiderClusterDto>(`/api/insider-events/${encodeURIComponent(symbol)}/cluster`, { query: { days }, token: await getToken() })
  });
}

export function useNews(symbols?: string[], limit = 20) {
  const getToken = useApiToken();
  const key = symbols && symbols.length ? symbols.slice().sort().join(',') : '';
  return useQuery({
    queryKey: ['news', key, limit] as const,
    queryFn: async () => request<NewsItemDto[]>('/api/news', {
      query: { symbols: symbols?.join(',') || undefined, limit },
      token: await getToken()
    })
  });
}

export function useEarnings(from?: string, to?: string) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['earnings', from ?? '', to ?? ''] as const,
    queryFn: async () => request<EarningsEventDto[]>('/api/earnings', { query: { from, to }, token: await getToken() })
  });
}

export function useSettings() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['settings'] as const,
    queryFn: async () => request<UserSettingsDto>('/api/settings', { token: await getToken() })
  });
}

export function useUpdateSettings() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: UserSettingsDto) =>
      request<UserSettingsDto>('/api/settings', { method: 'PUT', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] })
  });
}

// M8 — Data Providers & Real-Time hooks
import type {
  OrderBookDto, ExtendedQuoteDto, FilingDto, InsiderTradeDto,
  ShortInterestDto, EconomicEventDto, OptionsFlowDto
} from './types';

export function useOrderBook(symbol: string | undefined, depth = 10) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['orderbook', symbol, depth] as const,
    refetchInterval: 2000,
    queryFn: async () => request<OrderBookDto>(`/api/quotes/${symbol}/book`, { query: { depth }, token: await getToken() })
  });
}

export function useExtendedQuote(symbol: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['extended-quote', symbol] as const,
    refetchInterval: 15000,
    queryFn: async () => request<ExtendedQuoteDto>(`/api/quotes/${symbol}/extended`, { token: await getToken() })
  });
}

export function useFilings(symbols?: string[], limit = 25) {
  const getToken = useApiToken();
  const key = symbols && symbols.length ? symbols.slice().sort().join(',') : '';
  return useQuery({
    queryKey: ['filings', key, limit] as const,
    queryFn: async () => request<FilingDto[]>('/api/filings', {
      query: { symbols: symbols?.join(',') || undefined, limit },
      token: await getToken()
    })
  });
}

export function useInsiderTrades(symbol: string | undefined, limit = 25) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['insider-trades', symbol, limit] as const,
    queryFn: async () => request<InsiderTradeDto[]>('/api/insider-trades', { query: { symbol, limit }, token: await getToken() })
  });
}

export function useShortInterest(symbol: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['short-interest', symbol] as const,
    queryFn: async () => request<ShortInterestDto>(`/api/short-interest/${symbol}`, { token: await getToken() })
  });
}

export function useEconomicCalendar(from?: string, to?: string) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['economic-calendar', from ?? '', to ?? ''] as const,
    queryFn: async () => request<EconomicEventDto[]>('/api/calendar/economic', { query: { from, to }, token: await getToken() })
  });
}

export function useOptionsFlow(symbol: string | undefined, limit = 25) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['options-flow', symbol, limit] as const,
    queryFn: async () => request<OptionsFlowDto>('/api/options-flow', { query: { symbol, limit }, token: await getToken() })
  });
}

// ─────────────────────────────────────────────────────────────────────────
// M9 — Advanced Analytics & Charts
// ─────────────────────────────────────────────────────────────────────────
import type {
  OhlcBarDto, AnalystRatingDto, RiskMetricsDto, BacktestRequest, BacktestDto,
  EarningsSurprisePointDto, BenchmarkComparisonDto, BenchmarkConfigDto,
  GoalDto, GoalCreateDto
} from './types';

export function useBars(symbol: string | undefined, days = 180) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['bars', symbol, days] as const,
    queryFn: async () => request<OhlcBarDto[]>(`/api/quotes/${symbol}/bars`, { query: { days }, token: await getToken() })
  });
}

export function useAnalystRating(symbol: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['analyst-rating', symbol] as const,
    queryFn: async () => request<AnalystRatingDto>(`/api/analyst-ratings/${symbol}`, { token: await getToken() })
  });
}

export function useRiskMetrics(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!portfolioId,
    queryKey: ['risk-metrics', portfolioId] as const,
    queryFn: async () => request<RiskMetricsDto>(`/api/portfolios/${portfolioId}/risk`, { token: await getToken() })
  });
}

export function useBenchmarkComparison(portfolioId: string | undefined, symbol?: string) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!portfolioId,
    queryKey: ['benchmark', portfolioId, symbol ?? ''] as const,
    queryFn: async () => request<BenchmarkComparisonDto>(`/api/portfolios/${portfolioId}/benchmark`, { query: { symbol }, token: await getToken() })
  });
}

export function useSetPortfolioBenchmark() {
  const getToken = useApiToken();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ portfolioId, symbol }: { portfolioId: string; symbol: string | null }) => {
      await request(`/api/portfolios/${portfolioId}/benchmark/symbol`, {
        method: 'PUT',
        body: { symbol, blend: null } satisfies BenchmarkConfigDto,
        token: await getToken()
      });
    },
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: ['benchmark', vars.portfolioId] });
      qc.invalidateQueries({ queryKey: ['risk-metrics', vars.portfolioId] });
      qc.invalidateQueries({ queryKey: ['portfolios'] });
    }
  });
}

export function useRunBacktest() {
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (req: BacktestRequest) =>
      request<BacktestDto>(`/api/portfolios/${req.portfolioId}/backtest`, {
        method: 'POST',
        body: req,
        token: await getToken()
      })
  });
}

export function useGoals() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['goals'] as const,
    queryFn: async () => request<GoalDto[]>('/api/goals', { token: await getToken() })
  });
}

export function useGoal(id: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!id,
    queryKey: ['goal', id] as const,
    queryFn: async () => request<GoalDto>(`/api/goals/${id}`, { token: await getToken() })
  });
}

export function useCreateGoal() {
  const getToken = useApiToken();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: GoalCreateDto) =>
      request<GoalDto>('/api/goals', { method: 'POST', body: dto, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['goals'] })
  });
}

export function useUpdateGoal() {
  const getToken = useApiToken();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: GoalCreateDto }) =>
      request<GoalDto>(`/api/goals/${id}`, { method: 'PUT', body: dto, token: await getToken() }),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: ['goals'] });
      qc.invalidateQueries({ queryKey: ['goal', vars.id] });
    }
  });
}

export function useDeleteGoal() {
  const getToken = useApiToken();
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/goals/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['goals'] })
  });
}

export function useEarningsSurprises(symbol: string | undefined, quarters = 8) {
  const getToken = useApiToken();
  return useQuery({
    enabled: !!symbol,
    queryKey: ['earnings-surprises', symbol, quarters] as const,
    queryFn: async () => request<EarningsSurprisePointDto[]>(`/api/earnings/${symbol}/surprises`, { query: { quarters }, token: await getToken() })
  });
}

export function useEarningsCalendar(params: { from?: string; to?: string; scope?: 'holdings' | 'watchlist' | 'all'; watchlistId?: string }) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['earnings-calendar', params.from ?? '', params.to ?? '', params.scope ?? 'holdings', params.watchlistId ?? ''] as const,
    queryFn: async () => request<EarningsEventDto[]>('/api/calendar/earnings', { query: params, token: await getToken() })
  });
}

// ---------------- M11 Reporting & Sharing ----------------
import type {
  ShareTokenDto,
  CreateShareTokenRequest,
  SharedPortfolioDto,
  ReportScheduleDto,
  CreateReportScheduleRequest,
  UpdateReportScheduleRequest,
  ReportDeliveryDto
} from './types';

export function useShareTokens() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['share-tokens'] as const,
    queryFn: async () => request<ShareTokenDto[]>('/api/share-tokens', { token: await getToken() })
  });
}

export function useCreateShareToken() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: CreateShareTokenRequest) =>
      request<ShareTokenDto>('/api/share-tokens', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['share-tokens'] })
  });
}

export function useRevokeShareToken() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/share-tokens/${id}/revoke`, { method: 'POST', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['share-tokens'] })
  });
}

export function useDeleteShareToken() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/share-tokens/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['share-tokens'] })
  });
}

export function usePublicShare(token: string | undefined) {
  return useQuery({
    queryKey: ['public-share', token] as const,
    enabled: !!token,
    queryFn: async () => request<SharedPortfolioDto>(`/api/public/share/${token}`)
  });
}

export function useReportSchedules() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['report-schedules'] as const,
    queryFn: async () => request<ReportScheduleDto[]>('/api/report-schedules', { token: await getToken() })
  });
}

export function useCreateReportSchedule() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: CreateReportScheduleRequest) =>
      request<ReportScheduleDto>('/api/report-schedules', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['report-schedules'] });
    }
  });
}

export function useUpdateReportSchedule() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: UpdateReportScheduleRequest }) =>
      request<ReportScheduleDto>(`/api/report-schedules/${id}`, { method: 'PATCH', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['report-schedules'] })
  });
}

export function useDeleteReportSchedule() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/report-schedules/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['report-schedules'] })
  });
}

export function useRunReportSchedule() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<ReportDeliveryDto>(`/api/report-schedules/${id}/run`, { method: 'POST', token: await getToken() }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['report-deliveries'] });
      qc.invalidateQueries({ queryKey: ['report-schedules'] });
    }
  });
}

export function useReportDeliveries(filter?: { portfolioId?: string; scheduleId?: string }) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['report-deliveries', filter?.portfolioId ?? '', filter?.scheduleId ?? ''] as const,
    queryFn: async () => request<ReportDeliveryDto[]>('/api/report-deliveries', { query: filter, token: await getToken() })
  });
}

export function useDownloadReportDelivery() {
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (d: ReportDeliveryDto) => {
      const token = await getToken();
      const headers = new Headers();
      if (token) headers.set('Authorization', `Bearer ${token}`);
      const res = await fetch(`${config.apiBaseUrl}/api/report-deliveries/${d.id}/download`, { headers });
      if (!res.ok) throw new Error(`Download failed: ${res.status}`);
      const blob = await res.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = d.fileName;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    }
  });
}

export function useGenerateOnDemandReport() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (params: { portfolioId: string; type: string; format: string }) =>
      request<ReportDeliveryDto>('/api/report-deliveries/ondemand', { method: 'POST', query: params, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['report-deliveries'] })
  });
}

// ---------------- M14 Platform & Admin ----------------
export function useCashTransactions(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['cash-transactions', portfolioId] as const,
    enabled: !!portfolioId,
    queryFn: async () =>
      request<CashTransactionDto[]>(`/api/cash/transactions?portfolioId=${portfolioId}`, { token: await getToken() })
  });
}

export function useCashBalances(portfolioId: string | undefined) {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['cash-balances', portfolioId] as const,
    enabled: !!portfolioId,
    queryFn: async () =>
      request<CashBalanceDto[]>(`/api/cash/balances?portfolioId=${portfolioId}`, { token: await getToken() })
  });
}

export function useCreateCashTransaction() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: CreateCashTransactionRequest) =>
      request<CashTransactionDto>('/api/cash/transactions', { method: 'POST', body, token: await getToken() }),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: ['cash-transactions', vars.portfolioId] });
      qc.invalidateQueries({ queryKey: ['cash-balances', vars.portfolioId] });
    }
  });
}

export function useDeleteCashTransaction(portfolioId: string | undefined) {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/cash/transactions/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['cash-transactions', portfolioId] });
      qc.invalidateQueries({ queryKey: ['cash-balances', portfolioId] });
    }
  });
}

export function usePositionNotes(params: { symbol?: string; portfolioId?: string } = {}) {
  const getToken = useApiToken();
  const qs = new URLSearchParams();
  if (params.symbol) qs.set('symbol', params.symbol);
  if (params.portfolioId) qs.set('portfolioId', params.portfolioId);
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  return useQuery({
    queryKey: ['position-notes', params.symbol ?? null, params.portfolioId ?? null] as const,
    queryFn: async () => request<PositionNoteDto[]>(`/api/position-notes${suffix}`, { token: await getToken() })
  });
}

export function useCreatePositionNote() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: CreatePositionNoteRequest) =>
      request<PositionNoteDto>('/api/position-notes', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['position-notes'] })
  });
}

export function useUpdatePositionNote() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (vars: { id: string; body: UpdatePositionNoteRequest }) =>
      request<PositionNoteDto>(`/api/position-notes/${vars.id}`, { method: 'PATCH', body: vars.body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['position-notes'] })
  });
}

export function useDeletePositionNote() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/position-notes/${id}`, { method: 'DELETE', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['position-notes'] })
  });
}

export function useAuditLog(params: { take?: number; resource?: string } = {}) {
  const getToken = useApiToken();
  const qs = new URLSearchParams();
  if (params.take) qs.set('take', String(params.take));
  if (params.resource) qs.set('resource', params.resource);
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  return useQuery({
    queryKey: ['audit-log', params.take ?? 200, params.resource ?? null] as const,
    queryFn: async () => request<AuditEntryDto[]>(`/api/audit${suffix}`, { token: await getToken() })
  });
}

export function useModelTemplates() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['model-templates'] as const,
    queryFn: async () => request<ModelPortfolioTemplateDto[]>('/api/model-templates', { token: await getToken() })
  });
}

export function useApplyModelTemplate() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: ApplyTemplateRequest) =>
      request<PortfolioDto>('/api/model-templates/apply', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['portfolios'] })
  });
}

export function useExportAccount() {
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async () => {
      const token = await getToken();
      const res = await fetch(`${config.apiBaseUrl}/api/account/export`, {
        headers: token ? { Authorization: `Bearer ${token}` } : {}
      });
      if (!res.ok) throw new Error(`Export failed: ${res.status}`);
      return res.json();
    }
  });
}

export function useDeleteAccount() {
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async () =>
      request<void>('/api/account?confirm=delete', { method: 'DELETE', token: await getToken() })
  });
}
// M14 #91 — API keys
export function useApiKeys() {
  const getToken = useApiToken();
  return useQuery({
    queryKey: ['api-keys'] as const,
    queryFn: async () => request<import('./types').ApiKeyDto[]>('/api/api-keys', { token: await getToken() })
  });
}

export function useCreateApiKey() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (body: import('./types').CreateApiKeyRequest) =>
      request<import('./types').CreatedApiKeyDto>('/api/api-keys', { method: 'POST', body, token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['api-keys'] })
  });
}

export function useRevokeApiKey() {
  const qc = useQueryClient();
  const getToken = useApiToken();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/api-keys/${id}/revoke`, { method: 'POST', token: await getToken() }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['api-keys'] })
  });
}
