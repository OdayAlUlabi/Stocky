import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query';
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
  return useQuery({
    queryKey: ['portfolios'] as const,
    queryFn: async () => request<PortfolioDto[]>('/api/portfolios', {}),
    ...opts
  });
}

export function useCreatePortfolio() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { name: string; baseCurrency?: string }) =>
      request<PortfolioDto>('/api/portfolios', { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['portfolios'] })
  });
}

export function useDeletePortfolio() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/portfolios/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['portfolios'] })
  });
}

export function useUpdatePortfolio() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: { name: string; baseCurrency: string; costBasisMethod?: string } }) =>
      request<PortfolioDto>(`/api/portfolios/${id}`, { method: 'PUT', body }),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['portfolios'] });
      qc.invalidateQueries({ queryKey: ['portfolios', vars.id, 'capital-gains'] });
      qc.invalidateQueries({ queryKey: ['portfolios', vars.id, 'reports'] });
    }
  });
}

export function useHoldings(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'holdings'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<HoldingDto[]>(`/api/portfolios/${portfolioId}/holdings`, {})
  });
}

export function useTransactions(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'transactions'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<TransactionDto[]>(`/api/portfolios/${portfolioId}/transactions`, {})
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
  return useMutation({
    mutationFn: async (body: CreateTransactionRequest) =>
      request<TransactionDto>(`/api/portfolios/${portfolioId}/transactions`, { method: 'POST', body }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useUpdateTransaction(portfolioId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: CreateTransactionRequest }) =>
      request<TransactionDto>(`/api/portfolios/${portfolioId}/transactions/${id}`, { method: 'PUT', body }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useDeleteTransaction(portfolioId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/portfolios/${portfolioId}/transactions/${id}`, { method: 'DELETE' }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useImportTransactions(portfolioId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (csv: string) =>
      request<ImportResultDto>(`/api/portfolios/${portfolioId}/transactions/import`, {
        method: 'POST',
        body: { csv }
      }),
    onSuccess: () => invalidatePortfolio(qc, portfolioId)
  });
}

export function useDashboard(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['dashboard', portfolioId ?? 'all'] as const,
    queryFn: async () => request<DashboardDto>('/api/dashboard', { query: { portfolioId } })
  });
}

export function useWatchlists() {
  return useQuery({
    queryKey: ['watchlists'] as const,
    queryFn: async () => request<WatchlistDto[]>('/api/watchlists', {})
  });
}

export function useCreateWatchlist() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { name: string }) =>
      request<WatchlistDto>('/api/watchlists', { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['watchlists'] })
  });
}

export function useAddWatchlistItem(watchlistId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: { symbol: string }) =>
      request(`/api/watchlists/${watchlistId}/items`, { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['watchlists'] })
  });
}

export function useRemoveWatchlistItem(watchlistId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (itemId: string) =>
      request<void>(`/api/watchlists/${watchlistId}/items/${itemId}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['watchlists'] })
  });
}

export function useQuotes(symbols: string[]) {
  return useQuery({
    queryKey: ['quotes', [...symbols].sort().join(',')] as const,
    enabled: symbols.length > 0,
    queryFn: async () => request<QuoteDto[]>('/api/quotes', { query: { symbols: symbols.join(',') } })
  });
}

export function useSecuritySearch(q: string) {
  return useQuery({
    queryKey: ['securities-search', q] as const,
    enabled: q.length >= 1,
    queryFn: async () => request<InstrumentDto[]>('/api/securities/search', { query: { q } })
  });
}

export function usePositionDetail(portfolioId: string | undefined, symbol: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'position', symbol] as const,
    enabled: Boolean(portfolioId && symbol),
    queryFn: async () => request<PositionDetailDto>(`/api/portfolios/${portfolioId}/positions/${symbol}`, {})
  });
}

export function useReportSummary(portfolioId: string | undefined, from?: string, to?: string) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'reports', 'summary', from ?? '', to ?? ''] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<ReportSummaryDto>(`/api/portfolios/${portfolioId}/reports/summary`, { query: { from, to } })
  });
}

export function useDividends(portfolioId: string | undefined, year?: number) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'reports', 'dividends', year ?? 'all'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<DividendRowDto[]>(`/api/portfolios/${portfolioId}/reports/dividends`, { query: { year } })
  });
}

export function usePerformance(portfolioId: string | undefined, days = 90) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'performance', days] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PerformanceDto>(`/api/portfolios/${portfolioId}/performance-series`, { query: { days } })
  });
}

export function usePortfolioHistory(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'history'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PortfolioHistoryDto>(`/api/portfolios/${portfolioId}/history`, {})
  });
}

export function useAnalytics(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'analytics'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PortfolioAnalyticsDto>(`/api/portfolios/${portfolioId}/analytics`, {})
  });
}

export function useCorrelation(portfolioId: string | undefined, days = 90) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'correlation', days] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<CorrelationDto>(`/api/portfolios/${portfolioId}/correlation`, { query: { days } })
  });
}

export function usePortfolioAnalytics(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'analytics'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<PortfolioAnalyticsDto>(`/api/portfolios/${portfolioId}/analytics`, {})
  });
}

export function useAllocation(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'allocation'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<AllocationDto>(`/api/portfolios/${portfolioId}/allocation`, {})
  });
}

export function useCapitalGains(portfolioId: string | undefined, year?: number) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'capital-gains', year ?? 'current'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<CapitalGainsDto>(`/api/portfolios/${portfolioId}/capital-gains`, { query: { year } })
  });
}

export function useWashSales(portfolioId: string | undefined, year?: number) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'wash-sales', year ?? 'current'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<WashSaleReportDto>(`/api/portfolios/${portfolioId}/wash-sales`, { query: { year } })
  });
}

export function useRebalance(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'rebalance'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<RebalanceReportDto>(`/api/portfolios/${portfolioId}/rebalance`, {})
  });
}

export function useRebalanceTargets(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['portfolios', portfolioId, 'rebalance', 'targets'] as const,
    enabled: Boolean(portfolioId),
    queryFn: async () => request<RebalanceTargetDto[]>(`/api/portfolios/${portfolioId}/rebalance/targets`, {})
  });
}

export function useSaveRebalanceTargets(portfolioId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (targets: RebalanceTargetDto[]) =>
      request<RebalanceTargetDto[]>(`/api/portfolios/${portfolioId}/rebalance/targets`, {
        method: 'PUT', body: targets
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['portfolios', portfolioId, 'rebalance'] });
    }
  });
}

export function useScreenerFacets() {
  return useQuery({
    queryKey: ['screener', 'facets'] as const,
    queryFn: async () => request<ScreenerFacetsDto>('/api/securities/screener/facets', {}),
    staleTime: 5 * 60 * 1000
  });
}

export function useScreener(query: ScreenerQuery, enabled = true) {
  return useQuery({
    queryKey: ['screener', query] as const,
    enabled,
    queryFn: async () => request<ScreenerResultDto>('/api/securities/screener', {
      query: query as Record<string, string | number | undefined>
    })
  });
}

export function useAlerts(status?: string) {
  return useQuery({
    queryKey: ['alerts', status ?? 'all'] as const,
    queryFn: async () => request<AlertDto[]>('/api/alerts', { query: { status } })
  });
}

export function useCreateAlert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateAlertRequest) =>
      request<AlertDto>('/api/alerts', { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useUpdateAlert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: UpdateAlertRequest }) =>
      request<AlertDto>(`/api/alerts/${id}`, { method: 'PUT', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useDeleteAlert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/alerts/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

// M10 — Advanced Alerts
import type { AlertEventDto, SnoozeAlertRequest, InsiderEventDto, InsiderClusterDto } from './types';

export function useAlertHistory(take = 200) {
  return useQuery({
    queryKey: ['alerts', 'history', take] as const,
    queryFn: async () => request<AlertEventDto[]>('/api/alerts/history', { query: { take } })
  });
}

export function useSnoozeAlert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: SnoozeAlertRequest }) =>
      request<AlertDto>(`/api/alerts/${id}/snooze`, { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useReactivateAlert() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<AlertDto>(`/api/alerts/${id}/reactivate`, { method: 'POST' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['alerts'] })
  });
}

export function useInsiderEvents(symbol: string, days = 30) {
  return useQuery({
    queryKey: ['insider-events', symbol, days] as const,
    enabled: !!symbol,
    queryFn: async () => request<InsiderEventDto[]>(`/api/insider-events/${encodeURIComponent(symbol)}`, { query: { days } })
  });
}

export function useInsiderCluster(symbol: string, days = 30) {
  return useQuery({
    queryKey: ['insider-events', 'cluster', symbol, days] as const,
    enabled: !!symbol,
    queryFn: async () => request<InsiderClusterDto>(`/api/insider-events/${encodeURIComponent(symbol)}/cluster`, { query: { days } })
  });
}

export function useNews(symbols?: string[], limit = 20) {
  const key = symbols && symbols.length ? symbols.slice().sort().join(',') : '';
  return useQuery({
    queryKey: ['news', key, limit] as const,
    queryFn: async () => request<NewsItemDto[]>('/api/news', {
      query: { symbols: symbols?.join(',') || undefined, limit }
    })
  });
}

export function useEarnings(from?: string, to?: string) {
  return useQuery({
    queryKey: ['earnings', from ?? '', to ?? ''] as const,
    queryFn: async () => request<EarningsEventDto[]>('/api/earnings', { query: { from, to } })
  });
}

export function useSettings() {
  return useQuery({
    queryKey: ['settings'] as const,
    queryFn: async () => request<UserSettingsDto>('/api/settings', {})
  });
}

export function useUpdateSettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: UserSettingsDto) =>
      request<UserSettingsDto>('/api/settings', { method: 'PUT', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['settings'] })
  });
}

// M8 — Data Providers & Real-Time hooks
import type {
  OrderBookDto, ExtendedQuoteDto, FilingDto, InsiderTradeDto,
  ShortInterestDto, EconomicEventDto, OptionsFlowDto
} from './types';

export function useOrderBook(symbol: string | undefined, depth = 10) {
  return useQuery({
    enabled: !!symbol,
    queryKey: ['orderbook', symbol, depth] as const,
    refetchInterval: 2000,
    queryFn: async () => request<OrderBookDto>(`/api/quotes/${symbol}/book`, { query: { depth } })
  });
}

export function useExtendedQuote(symbol: string | undefined) {
  return useQuery({
    enabled: !!symbol,
    queryKey: ['extended-quote', symbol] as const,
    refetchInterval: 15000,
    queryFn: async () => request<ExtendedQuoteDto>(`/api/quotes/${symbol}/extended`, {})
  });
}

export function useFilings(symbols?: string[], limit = 25) {
  const key = symbols && symbols.length ? symbols.slice().sort().join(',') : '';
  return useQuery({
    queryKey: ['filings', key, limit] as const,
    queryFn: async () => request<FilingDto[]>('/api/filings', {
      query: { symbols: symbols?.join(',') || undefined, limit }
    })
  });
}

export function useInsiderTrades(symbol: string | undefined, limit = 25) {
  return useQuery({
    enabled: !!symbol,
    queryKey: ['insider-trades', symbol, limit] as const,
    queryFn: async () => request<InsiderTradeDto[]>('/api/insider-trades', { query: { symbol, limit } })
  });
}

export function useShortInterest(symbol: string | undefined) {
  return useQuery({
    enabled: !!symbol,
    queryKey: ['short-interest', symbol] as const,
    queryFn: async () => request<ShortInterestDto>(`/api/short-interest/${symbol}`, {})
  });
}

export function useEconomicCalendar(from?: string, to?: string) {
  return useQuery({
    queryKey: ['economic-calendar', from ?? '', to ?? ''] as const,
    queryFn: async () => request<EconomicEventDto[]>('/api/calendar/economic', { query: { from, to } })
  });
}

export function useOptionsFlow(symbol: string | undefined, limit = 25) {
  return useQuery({
    enabled: !!symbol,
    queryKey: ['options-flow', symbol, limit] as const,
    queryFn: async () => request<OptionsFlowDto>('/api/options-flow', { query: { symbol, limit } })
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
  return useQuery({
    enabled: !!symbol,
    queryKey: ['bars', symbol, days] as const,
    queryFn: async () => request<OhlcBarDto[]>(`/api/quotes/${symbol}/bars`, { query: { days } })
  });
}

export function useAnalystRating(symbol: string | undefined) {
  return useQuery({
    enabled: !!symbol,
    queryKey: ['analyst-rating', symbol] as const,
    queryFn: async () => request<AnalystRatingDto>(`/api/analyst-ratings/${symbol}`, {})
  });
}

export function useRiskMetrics(portfolioId: string | undefined) {
  return useQuery({
    enabled: !!portfolioId,
    queryKey: ['risk-metrics', portfolioId] as const,
    queryFn: async () => request<RiskMetricsDto>(`/api/portfolios/${portfolioId}/risk`, {})
  });
}

export function useBenchmarkComparison(portfolioId: string | undefined, symbol?: string) {
  return useQuery({
    enabled: !!portfolioId,
    queryKey: ['benchmark', portfolioId, symbol ?? ''] as const,
    queryFn: async () => request<BenchmarkComparisonDto>(`/api/portfolios/${portfolioId}/benchmark`, { query: { symbol } })
  });
}

export function useSetPortfolioBenchmark() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ portfolioId, symbol }: { portfolioId: string; symbol: string | null }) => {
      await request(`/api/portfolios/${portfolioId}/benchmark/symbol`, {
        method: 'PUT',
        body: { symbol, blend: null } satisfies BenchmarkConfigDto
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
  return useMutation({
    mutationFn: async (req: BacktestRequest) =>
      request<BacktestDto>(`/api/portfolios/${req.portfolioId}/backtest`, {
        method: 'POST',
        body: req
      })
  });
}

export function useGoals() {
  return useQuery({
    queryKey: ['goals'] as const,
    queryFn: async () => request<GoalDto[]>('/api/goals', {})
  });
}

export function useGoal(id: string | undefined) {
  return useQuery({
    enabled: !!id,
    queryKey: ['goal', id] as const,
    queryFn: async () => request<GoalDto>(`/api/goals/${id}`, {})
  });
}

export function useCreateGoal() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (dto: GoalCreateDto) =>
      request<GoalDto>('/api/goals', { method: 'POST', body: dto }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['goals'] })
  });
}

export function useUpdateGoal() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, dto }: { id: string; dto: GoalCreateDto }) =>
      request<GoalDto>(`/api/goals/${id}`, { method: 'PUT', body: dto }),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: ['goals'] });
      qc.invalidateQueries({ queryKey: ['goal', vars.id] });
    }
  });
}

export function useDeleteGoal() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/goals/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['goals'] })
  });
}

export function useEarningsSurprises(symbol: string | undefined, quarters = 8) {
  return useQuery({
    enabled: !!symbol,
    queryKey: ['earnings-surprises', symbol, quarters] as const,
    queryFn: async () => request<EarningsSurprisePointDto[]>(`/api/earnings/${symbol}/surprises`, { query: { quarters } })
  });
}

export function useEarningsCalendar(params: { from?: string; to?: string; scope?: 'holdings' | 'watchlist' | 'all'; watchlistId?: string }) {
  return useQuery({
    queryKey: ['earnings-calendar', params.from ?? '', params.to ?? '', params.scope ?? 'holdings', params.watchlistId ?? ''] as const,
    queryFn: async () => request<EarningsEventDto[]>('/api/calendar/earnings', { query: params })
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
  return useQuery({
    queryKey: ['share-tokens'] as const,
    queryFn: async () => request<ShareTokenDto[]>('/api/share-tokens', {})
  });
}

export function useCreateShareToken() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateShareTokenRequest) =>
      request<ShareTokenDto>('/api/share-tokens', { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['share-tokens'] })
  });
}

export function useRevokeShareToken() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/share-tokens/${id}/revoke`, { method: 'POST' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['share-tokens'] })
  });
}

export function useDeleteShareToken() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/share-tokens/${id}`, { method: 'DELETE' }),
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
  return useQuery({
    queryKey: ['report-schedules'] as const,
    queryFn: async () => request<ReportScheduleDto[]>('/api/report-schedules', {})
  });
}

export function useCreateReportSchedule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateReportScheduleRequest) =>
      request<ReportScheduleDto>('/api/report-schedules', { method: 'POST', body }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['report-schedules'] });
    }
  });
}

export function useUpdateReportSchedule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string; body: UpdateReportScheduleRequest }) =>
      request<ReportScheduleDto>(`/api/report-schedules/${id}`, { method: 'PATCH', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['report-schedules'] })
  });
}

export function useDeleteReportSchedule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/report-schedules/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['report-schedules'] })
  });
}

export function useRunReportSchedule() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<ReportDeliveryDto>(`/api/report-schedules/${id}/run`, { method: 'POST' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['report-deliveries'] });
      qc.invalidateQueries({ queryKey: ['report-schedules'] });
    }
  });
}

export function useReportDeliveries(filter?: { portfolioId?: string; scheduleId?: string }) {
  return useQuery({
    queryKey: ['report-deliveries', filter?.portfolioId ?? '', filter?.scheduleId ?? ''] as const,
    queryFn: async () => request<ReportDeliveryDto[]>('/api/report-deliveries', { query: filter })
  });
}

export function useDownloadReportDelivery() {
  return useMutation({
    mutationFn: async (d: ReportDeliveryDto) => {
      const res = await fetch(`${config.apiBaseUrl}/api/report-deliveries/${d.id}/download`);
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
  return useMutation({
    mutationFn: async (params: { portfolioId: string; type: string; format: string }) =>
      request<ReportDeliveryDto>('/api/report-deliveries/ondemand', { method: 'POST', query: params }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['report-deliveries'] })
  });
}

// ---------------- M14 Platform & Admin ----------------
export function useCashTransactions(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['cash-transactions', portfolioId] as const,
    enabled: !!portfolioId,
    queryFn: async () =>
      request<CashTransactionDto[]>(`/api/cash/transactions?portfolioId=${portfolioId}`, {})
  });
}

export function useCashBalances(portfolioId: string | undefined) {
  return useQuery({
    queryKey: ['cash-balances', portfolioId] as const,
    enabled: !!portfolioId,
    queryFn: async () =>
      request<CashBalanceDto[]>(`/api/cash/balances?portfolioId=${portfolioId}`, {})
  });
}

export function useCreateCashTransaction() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreateCashTransactionRequest) =>
      request<CashTransactionDto>('/api/cash/transactions', { method: 'POST', body }),
    onSuccess: (_d, vars) => {
      qc.invalidateQueries({ queryKey: ['cash-transactions', vars.portfolioId] });
      qc.invalidateQueries({ queryKey: ['cash-balances', vars.portfolioId] });
    }
  });
}

export function useDeleteCashTransaction(portfolioId: string | undefined) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/cash/transactions/${id}`, { method: 'DELETE' }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['cash-transactions', portfolioId] });
      qc.invalidateQueries({ queryKey: ['cash-balances', portfolioId] });
    }
  });
}

export function usePositionNotes(params: { symbol?: string; portfolioId?: string } = {}) {
  const qs = new URLSearchParams();
  if (params.symbol) qs.set('symbol', params.symbol);
  if (params.portfolioId) qs.set('portfolioId', params.portfolioId);
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  return useQuery({
    queryKey: ['position-notes', params.symbol ?? null, params.portfolioId ?? null] as const,
    queryFn: async () => request<PositionNoteDto[]>(`/api/position-notes${suffix}`, {})
  });
}

export function useCreatePositionNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: CreatePositionNoteRequest) =>
      request<PositionNoteDto>('/api/position-notes', { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['position-notes'] })
  });
}

export function useUpdatePositionNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (vars: { id: string; body: UpdatePositionNoteRequest }) =>
      request<PositionNoteDto>(`/api/position-notes/${vars.id}`, { method: 'PATCH', body: vars.body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['position-notes'] })
  });
}

export function useDeletePositionNote() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/position-notes/${id}`, { method: 'DELETE' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['position-notes'] })
  });
}

export function useAuditLog(params: { take?: number; resource?: string } = {}) {
  const qs = new URLSearchParams();
  if (params.take) qs.set('take', String(params.take));
  if (params.resource) qs.set('resource', params.resource);
  const suffix = qs.toString() ? `?${qs.toString()}` : '';
  return useQuery({
    queryKey: ['audit-log', params.take ?? 200, params.resource ?? null] as const,
    queryFn: async () => request<AuditEntryDto[]>(`/api/audit${suffix}`, {})
  });
}

export function useModelTemplates() {
  return useQuery({
    queryKey: ['model-templates'] as const,
    queryFn: async () => request<ModelPortfolioTemplateDto[]>('/api/model-templates', {})
  });
}

export function useApplyModelTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: ApplyTemplateRequest) =>
      request<PortfolioDto>('/api/model-templates/apply', { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['portfolios'] })
  });
}

export function useExportAccount() {
  return useMutation({
    mutationFn: async () => {
      const res = await fetch(`${config.apiBaseUrl}/api/account/export`);
      if (!res.ok) throw new Error(`Export failed: ${res.status}`);
      return res.json();
    }
  });
}

export function useDeleteAccount() {
  return useMutation({
    mutationFn: async () =>
      request<void>('/api/account?confirm=delete', { method: 'DELETE' })
  });
}
// M14 #91 — API keys
export function useApiKeys() {
  return useQuery({
    queryKey: ['api-keys'] as const,
    queryFn: async () => request<import('./types').ApiKeyDto[]>('/api/api-keys', {})
  });
}

export function useCreateApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: import('./types').CreateApiKeyRequest) =>
      request<import('./types').CreatedApiKeyDto>('/api/api-keys', { method: 'POST', body }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['api-keys'] })
  });
}

export function useRevokeApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      request<void>(`/api/api-keys/${id}/revoke`, { method: 'POST' }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['api-keys'] })
  });
}
