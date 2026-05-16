import { useMutation, useQuery, useQueryClient, type UseQueryOptions } from '@tanstack/react-query';
import { useApiToken } from '../auth/useApiToken';
import { request } from './client';
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
  RebalanceTargetDto
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
