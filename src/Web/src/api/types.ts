export interface PortfolioDto {
  id: string;
  name: string;
  baseCurrency: string;
  createdAt: string;
  cashBalance: number;
  costBasisMethod: CostBasisMethod;
}

export type CostBasisMethod = 'Fifo' | 'Lifo' | 'HighestCost' | 'LowestCost';

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
  beta: number;
  benchmarkSymbol: string;
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

export interface WashSaleReplacementDto {
  buyTransactionId: string;
  buyAt: string;
  shares: number;
}

export interface WashSaleAdjustmentDto {
  lotId: string;
  symbol: string;
  soldAt: string;
  lotQuantity: number;
  lotLoss: number;
  replacementShares: number;
  disallowedLoss: number;
  allowedLoss: number;
  replacements: WashSaleReplacementDto[];
}

export interface WashSaleReportDto {
  year: number;
  totalLoss: number;
  disallowedLoss: number;
  adjustments: WashSaleAdjustmentDto[];
}

export interface AllocationDto {
  byAsset: AllocationSliceDto[];
  bySector: AllocationSliceDto[];
  byCurrency: AllocationSliceDto[];
  bySymbol: AllocationSliceDto[];
  totalValue: number;
  currency: string;
}

export interface RebalanceTargetDto {
  symbol: string;
  targetWeightPercent: number;
}

export interface RebalanceSuggestionDto {
  symbol: string;
  currentValue: number;
  currentWeightPercent: number;
  targetWeightPercent: number;
  driftPercent: number;
  tradeValue: number;
  action: 'Buy' | 'Sell' | 'Hold';
}

export interface RebalanceReportDto {
  portfolioId: string;
  currency: string;
  totalValue: number;
  targetWeightSumPercent: number;
  suggestions: RebalanceSuggestionDto[];
}

export interface ScreenerRowDto {
  symbol: string;
  name: string;
  assetClass: string;
  sector: string | null;
  industry: string | null;
  country: string | null;
  marketCap: number | null;
  beta: number | null;
  dividendYield: number | null;
  latestPrice: number | null;
}

export interface ScreenerResultDto {
  total: number;
  rows: ScreenerRowDto[];
}

export interface ScreenerFacetsDto {
  assetClasses: string[];
  sectors: string[];
  countries: string[];
}

export interface ScreenerQuery {
  q?: string;
  assetClass?: string;
  sector?: string;
  country?: string;
  minMarketCap?: number;
  maxMarketCap?: number;
  minDividendYield?: number;
  maxBeta?: number;
  sort?: 'marketcap-desc' | 'marketcap-asc' | 'divyield-desc' | 'beta-asc' | 'symbol';
  limit?: number;
}

export type AlertCondition =
  | 'PriceAbove' | 'PriceBelow' | 'DayChangePercentAbove' | 'DayChangePercentBelow'
  | 'SmaCrossAbove' | 'SmaCrossBelow' | 'RsiAbove' | 'RsiBelow'
  | 'EarningsWithinDays' | 'NewsKeyword' | 'DriftAbovePercent'
  | 'InsiderClusterBuy' | 'InsiderClusterSell';
export type AlertStatus = 'Active' | 'Triggered' | 'Disabled';
export type AlertType = 'Price' | 'Technical' | 'Earnings' | 'News' | 'Drift' | 'Insider';

export interface AlertDto {
  id: string;
  symbol: string;
  type: AlertType;
  condition: AlertCondition;
  threshold: number;
  status: AlertStatus;
  createdAt: string;
  triggeredAt: string | null;
  triggeredValue: number | null;
  note: string | null;
  indicatorPeriod: number | null;
  keywordFilter: string | null;
  minSentiment: number | null;
  daysBeforeEarnings: number | null;
  portfolioId: string | null;
  channels: string;
  webhookUrl: string | null;
  snoozedUntil: string | null;
}

export interface CreateAlertRequest {
  symbol: string;
  condition: AlertCondition;
  threshold: number;
  note: string | null;
  type?: AlertType;
  indicatorPeriod?: number | null;
  keywordFilter?: string | null;
  minSentiment?: number | null;
  daysBeforeEarnings?: number | null;
  portfolioId?: string | null;
  channels?: string;
  webhookUrl?: string | null;
}

export interface UpdateAlertRequest {
  threshold: number;
  status: AlertStatus;
  note: string | null;
  channels?: string;
  webhookUrl?: string | null;
  indicatorPeriod?: number | null;
  keywordFilter?: string | null;
  minSentiment?: number | null;
  daysBeforeEarnings?: number | null;
}

export interface AlertEventDto {
  id: string;
  alertId: string;
  symbol: string;
  type: AlertType;
  condition: AlertCondition;
  triggeredAt: string;
  triggeredValue: number | null;
  message: string;
  channels: string;
  context: string | null;
}

export interface SnoozeAlertRequest { untilUtc: string; }

export interface InsiderEventDto {
  id: string;
  symbol: string;
  insiderName: string;
  relation: string;
  transactionType: string;
  shares: number;
  price: number;
  filedAt: string;
}

export interface InsiderClusterDto {
  symbol: string;
  buyCount: number;
  sellCount: number;
  netShares: number;
  windowStart: string;
  windowEnd: string;
  trades: InsiderEventDto[];
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

// M8 — Data Providers & Real-Time
export interface OrderBookLevelDto { price: number; size: number; }
export interface OrderBookDto { symbol: string; bids: OrderBookLevelDto[]; asks: OrderBookLevelDto[]; asOf: string; }

export interface ExtendedQuoteDto {
  symbol: string;
  regularPrice: number;
  extendedPrice: number;
  extendedChange: number;
  extendedChangePercent: number;
  session: 'PreMarket' | 'Regular' | 'AfterHours' | 'Closed';
  asOf: string;
}

export interface FilingDto { id: number; symbol: string; form: string; title: string; filedAt: string; url: string; accessionNumber: string; }

export interface InsiderTradeDto {
  id: number; symbol: string; insider: string; role: string; side: 'Buy' | 'Sell';
  quantity: number; price: number; value: number; filedAt: string;
}

export interface ShortInterestPointDto { reportDate: string; shortInterest: number; percentOfFloat: number; daysToCover: number; }
export interface ShortInterestDto {
  symbol: string; reportDate: string; shortInterest: number; floatShares: number;
  percentOfFloat: number; daysToCover: number; history: ShortInterestPointDto[];
}

export interface EconomicEventDto {
  id: number; date: string; time: string; country: string; indicator: string;
  importance: 'High' | 'Medium' | 'Low';
  actual: number | null; forecast: number | null; previous: number | null; unit: string;
}

export interface OptionsFlowRowDto {
  symbol: string; side: 'Call' | 'Put'; strike: number; expiry: string;
  volume: number; openInterest: number; volumeOverOpenInterest: number;
  premium: number; notionalValue: number;
}
export interface OptionsFlowDto { symbol: string; rows: OptionsFlowRowDto[]; asOf: string; }

export interface PriceTickDto { symbol: string; price: number; change: number | null; changePercent: number | null; asOf: string; }

// ─────────────────────────────────────────────────────────────────────────
// M9 — Advanced Analytics & Charts
// ─────────────────────────────────────────────────────────────────────────

export interface OhlcBarDto {
  date: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface AnalystRatingDistributionDto {
  strongBuy: number;
  buy: number;
  hold: number;
  sell: number;
  strongSell: number;
}

export interface AnalystRatingDto {
  symbol: string;
  consensusScore: number;
  consensusLabel: string;
  priceTargetLow: number;
  priceTargetMean: number;
  priceTargetHigh: number;
  priceTargetMedian: number;
  analystCount: number;
  asOf: string;
  distribution: AnalystRatingDistributionDto;
}

export interface RiskMetricsDto {
  portfolioId: string;
  from: string;
  to: string;
  sharpe: number;
  sortino: number;
  maxDrawdown: number;
  maxDrawdownDate: string;
  var95: number;
  var99: number;
  cvar95: number;
  annualisedVolatility: number;
  downsideVolatility: number;
  beta: number;
  alpha: number;
  benchmarkSymbol: string;
}

export interface BacktestRequest {
  portfolioId: string;
  from: string;
  to: string;
  initialCash: number;
  monthlyContribution: number;
  frequency: 'Monthly' | 'Quarterly' | 'Yearly';
  targets: { symbol: string; targetWeightPercent: number }[];
}

export interface BacktestPointDto {
  date: string;
  equity: number;
  contributions: number;
  benchmarkEquity: number;
}

export interface BacktestDto {
  portfolioId: string;
  benchmarkSymbol: string;
  finalEquity: number;
  totalContributions: number;
  totalReturnPercent: number;
  cagr: number;
  maxDrawdown: number;
  benchmarkFinalEquity: number;
  benchmarkTotalReturnPercent: number;
  benchmarkCagr: number;
  series: BacktestPointDto[];
}

export interface EarningsSurprisePointDto {
  date: string;
  epsEstimate: number | null;
  epsActual: number | null;
  surprisePercent: number | null;
}

export interface BenchmarkComponentDto {
  symbol: string;
  weight: number;
}

export interface BenchmarkConfigDto {
  symbol?: string | null;
  blend?: BenchmarkComponentDto[] | null;
}

export interface BenchmarkPointDto {
  date: string;
  portfolioEquity: number;
  benchmarkEquity: number;
  outperformanceBps: number;
}

export interface BenchmarkComparisonDto {
  portfolioId: string;
  benchmarkLabel: string;
  from: string;
  to: string;
  portfolioReturnPercent: number;
  benchmarkReturnPercent: number;
  outperformanceBps: number;
  alpha: number;
  beta: number;
  series: BenchmarkPointDto[];
}

export interface GoalCreateDto {
  portfolioId?: string | null;
  name: string;
  targetValue: number;
  targetDate: string;
  monthlyContribution: number;
  expectedReturn: number;
}

export interface GoalProjectionPointDto {
  date: string;
  projectedValue: number;
  targetTrajectory: number;
}

export interface GoalDto {
  id: string;
  portfolioId: string | null;
  name: string;
  targetValue: number;
  targetDate: string;
  monthlyContribution: number;
  expectedReturn: number;
  currentValue: number;
  progressPercent: number;
  projectedHitDate: string | null;
  onTrack: boolean;
  projectedFinalValue: number;
  projection: GoalProjectionPointDto[];
}

// ---------------- M11 Reporting & Sharing ----------------
export interface ShareTokenDto {
  id: string;
  token: string;
  portfolioId: string;
  label: string | null;
  createdAt: string;
  expiresAt: string | null;
  revokedAt: string | null;
  viewCount: number;
  lastViewedAt: string | null;
  includeTransactions: boolean;
  includeCostBasis: boolean;
  isActive: boolean;
  shareUrl: string;
}

export interface CreateShareTokenRequest {
  portfolioId: string;
  label?: string | null;
  expiresAt?: string | null;
  includeTransactions?: boolean;
  includeCostBasis?: boolean;
}

export interface SharedHoldingRowDto {
  symbol: string;
  quantity: number;
  latestPrice: number | null;
  marketValue: number | null;
  averageCost: number | null;
  unrealizedPnL: number | null;
}

export interface SharedTransactionRowDto {
  executedAt: string;
  type: string;
  symbol: string | null;
  quantity: number;
  price: number;
}

export interface SharedPortfolioDto {
  portfolioName: string;
  baseCurrency: string;
  generatedAt: string;
  totalMarketValue: number;
  totalUnrealizedPnL: number | null;
  includesCostBasis: boolean;
  includesTransactions: boolean;
  holdings: SharedHoldingRowDto[];
  transactions: SharedTransactionRowDto[] | null;
}

export type ReportTypeName = 'CapitalGains' | 'WashSales' | 'Dividends';
export type ReportFormatName = 'Csv' | 'Pdf';
export type ReportCadenceName = 'OnDemand' | 'Weekly' | 'Monthly' | 'Quarterly';

export interface ReportScheduleDto {
  id: string;
  portfolioId: string;
  type: ReportTypeName;
  format: ReportFormatName;
  cadence: ReportCadenceName;
  email: string | null;
  enabled: boolean;
  createdAt: string;
  nextRunUtc: string;
  lastRunUtc: string | null;
}

export interface CreateReportScheduleRequest {
  portfolioId: string;
  type: ReportTypeName;
  format: ReportFormatName;
  cadence: ReportCadenceName;
  email?: string | null;
  enabled?: boolean;
}

export interface UpdateReportScheduleRequest {
  type?: ReportTypeName;
  format?: ReportFormatName;
  cadence?: ReportCadenceName;
  email?: string | null;
  enabled?: boolean;
}

export interface ReportDeliveryDto {
  id: string;
  scheduleId: string | null;
  portfolioId: string;
  type: ReportTypeName;
  format: ReportFormatName;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  generatedAt: string;
  trigger: string | null;
  channel: string | null;
}

// ---------------- M14 Platform & Admin ----------------
export interface CashTransactionDto {
  id: string;
  portfolioId: string;
  type: string;
  amount: number;
  currency: string;
  executedAt: string;
  notes: string | null;
}
export interface CreateCashTransactionRequest {
  portfolioId: string;
  type: string;
  amount: number;
  currency: string;
  executedAt?: string;
  notes?: string | null;
}
export interface CashBalanceDto {
  portfolioId: string;
  currency: string;
  balance: number;
  count: number;
}
export interface PositionNoteDto {
  id: string;
  portfolioId: string | null;
  symbol: string;
  body: string;
  createdAt: string;
  updatedAt: string;
}
export interface CreatePositionNoteRequest {
  symbol: string;
  body: string;
  portfolioId?: string | null;
}
export interface UpdatePositionNoteRequest {
  body: string;
}
export interface AuditEntryDto {
  id: string;
  timestamp: string;
  action: string;
  resource: string;
  resourceId: string | null;
  method: string | null;
  path: string | null;
  statusCode: number | null;
  clientIp: string | null;
  details: string | null;
}
export interface ModelTemplateAllocationDto {
  symbol: string;
  name: string;
  assetClass: string;
  weightPercent: number;
}
export interface ModelPortfolioTemplateDto {
  slug: string;
  name: string;
  description: string;
  risk: string;
  allocations: ModelTemplateAllocationDto[];
}
export interface ApplyTemplateRequest {
  slug: string;
  portfolioName: string;
  baseCurrency?: string;
  initialCashDeposit?: number | null;
}