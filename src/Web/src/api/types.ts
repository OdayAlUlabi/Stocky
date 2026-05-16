export interface PortfolioDto {
  id: string;
  name: string;
  baseCurrency: string;
  createdAt: string;
  cashBalance: number;
}

export interface HoldingDto {
  id: string;
  symbol: string;
  quantity: number;
  averageCost: number;
  latestPrice: number | null;
  marketValue: number | null;
}

export type TransactionType = 'Buy' | 'Sell' | 'Dividend' | 'Deposit' | 'Withdrawal' | 'Split';

export interface TransactionDto {
  id: string;
  symbol: string | null;
  type: TransactionType;
  quantity: number;
  price: number;
  fee: number;
  currency: string;
  executedAt: string;
  notes: string | null;
}

export interface CreateTransactionRequest {
  symbol: string | null;
  type: TransactionType;
  quantity: number;
  price: number;
  fee: number;
  currency: string;
  executedAt: string;
  notes: string | null;
}

export interface WatchlistItemDto {
  id: string;
  symbol: string;
  latestPrice: number | null;
  changePercent: number | null;
}

export interface WatchlistDto {
  id: string;
  name: string;
  items: WatchlistItemDto[];
}

export interface QuoteDto {
  symbol: string;
  price: number;
  change: number | null;
  changePercent: number | null;
  asOf: string;
}

export interface InstrumentDto {
  symbol: string;
  name: string;
  exchange: string;
  currency: string;
  assetClass: string;
}

export interface AllocationSliceDto {
  label: string;
  value: number;
  percent: number;
}

export interface MoverDto {
  symbol: string;
  marketValue: number;
  dayChangePercent: number;
}

export interface ValuePointDto {
  date: string;
  value: number;
}

export interface DashboardDto {
  portfolioId: string | null;
  portfolioName: string;
  currency: string;
  totalValue: number;
  dayPnL: number;
  dayPnLPercent: number;
  totalReturn: number;
  totalReturnPercent: number;
  sectorAllocation: AllocationSliceDto[];
  assetClassAllocation: AllocationSliceDto[];
  topGainers: MoverDto[];
  topLosers: MoverDto[];
  valueHistory: ValuePointDto[];
  asOf: string;
  cashBalance: number;
  totalEquity: number;
}

export interface TaxLotDto {
  id: string;
  openedAt: string;
  quantity: number;
  remainingQuantity: number;
  costPerShare: number;
  costBasis: number;
}

export interface PositionDetailDto {
  symbol: string;
  name: string;
  assetClass: string;
  sector: string | null;
  currency: string;
  quantity: number;
  averageCost: number;
  latestPrice: number | null;
  marketValue: number | null;
  unrealizedPnL: number;
  unrealizedPnLPercent: number;
  realizedPnL: number;
  dividendsReceived: number;
  lots: TaxLotDto[];
  transactions: TransactionDto[];
  priceHistory: ValuePointDto[];
}

export interface ReportSummaryDto {
  totalDeposits: number;
  totalWithdrawals: number;
  netContributions: number;
  marketValue: number;
  realizedPnL: number;
  unrealizedPnL: number;
  dividends: number;
  fees: number;
  from: string;
  to: string;
  currency: string;
}

export interface DividendRowDto {
  symbol: string;
  date: string;
  amount: number;
  currency: string;
}

export interface PerformancePointDto {
  date: string;
  value: number;
  costBasis: number;
  twrPercent: number;
}

export interface PerformanceDto {
  portfolioId: string | null;
  currency: string;
  twrPercent: number;
  mwrPercent: number;
  best1Day: number;
  worst1Day: number;
  series: PerformancePointDto[];
}

export interface PortfolioHistoryPointDto {
  date: string;
  cash: number;
  marketValue: number;
  totalEquity: number;
  netContributions: number;
}

export interface PortfolioHistoryEventDto {
  date: string;
  type: string;
  symbol: string | null;
  quantity: number;
  amount: number;
  notes: string | null;
}

export interface PortfolioHistoryDto {
  portfolioId: string;
  currency: string;
  from: string;
  to: string;
  netContributions: number;
  totalEquity: number;
  totalReturn: number;
  totalReturnPercent: number;
  series: PortfolioHistoryPointDto[];
  events: PortfolioHistoryEventDto[];
}

export interface DrawdownPointDto {
  date: string;
  drawdownPercent: number;
}

export interface DailyReturnPointDto {
  date: string;
  returnPercent: number;
}

export interface PortfolioAnalyticsDto {
  portfolioId: string;
  currency: string;
  from: string;
  to: string;
  totalReturnPercent: number;
  twrr: number;
  twrrAnnualised: number;
  mwrr: number;
  volatility: number;
  sharpe: number;
  maxDrawdown: number;
  maxDrawdownDate: string;
  peakEquity: number;
  bestDay: number;
  bestDayDate: string;
  worstDay: number;
  worstDayDate: string;
  totalDividends: number;
  ttmDividends: number;
  dividendYield: number;
  drawdownSeries: DrawdownPointDto[];
  dailyReturnSeries: DailyReturnPointDto[];
}

export interface CorrelationDto {
  portfolioId: string;
  from: string;
  to: string;
  symbols: string[];
  matrix: number[][];
}

export interface AllocationDto {
  byAsset: AllocationSliceDto[];
  bySector: AllocationSliceDto[];
  byCurrency: AllocationSliceDto[];
  bySymbol: AllocationSliceDto[];
  totalValue: number;
  currency: string;
}

export type AlertCondition = 'PriceAbove' | 'PriceBelow' | 'DayChangePercentAbove' | 'DayChangePercentBelow';
export type AlertStatus = 'Active' | 'Triggered' | 'Disabled';

export interface AlertDto {
  id: string;
  symbol: string;
  condition: AlertCondition;
  threshold: number;
  status: AlertStatus;
  createdAt: string;
  triggeredAt: string | null;
  triggeredValue: number | null;
  note: string | null;
}

export interface CreateAlertRequest {
  symbol: string;
  condition: AlertCondition;
  threshold: number;
  note: string | null;
}

export interface UpdateAlertRequest {
  threshold: number;
  status: AlertStatus;
  note: string | null;
}

export interface RealizedGainDto {
  id: string;
  symbol: string;
  acquiredAt: string;
  soldAt: string;
  quantity: number;
  costBasis: number;
  proceeds: number;
  gain: number;
  isLongTerm: boolean;
}

export interface CapitalGainsDto {
  year: number;
  shortTermGain: number;
  longTermGain: number;
  totalGain: number;
  lots: RealizedGainDto[];
}

export interface NewsItemDto {
  id: number;
  headline: string;
  summary: string | null;
  source: string;
  url: string | null;
  symbol: string | null;
  publishedAt: string;
  category: string;
}

export interface EarningsEventDto {
  id: number;
  symbol: string;
  date: string;
  time: string | null;
  epsEstimate: number | null;
  epsActual: number | null;
  revenueEstimate: number | null;
  revenueActual: number | null;
}

export interface UserSettingsDto {
  displayCurrency: string;
  theme: string;
  locale: string;
  emailAlerts: boolean;
  weeklyDigest: boolean;
}

export interface ImportResultRowError {
  row: number;
  message: string;
}

export interface ImportResultDto {
  imported: number;
  skipped: number;
  errors: ImportResultRowError[];
}
