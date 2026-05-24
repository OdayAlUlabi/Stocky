import { createBrowserRouter } from 'react-router-dom';
import { Shell } from './shell/Shell';
import { Dashboard } from './dashboard/Dashboard';
import { PortfolioList } from './portfolios/PortfolioList';
import { PortfolioDetail } from './portfolios/PortfolioDetail';
import { PortfolioHistory } from './portfolios/PortfolioHistory';
import { CapitalFlow } from './portfolios/CapitalFlow';
import { PortfolioAnalytics } from './portfolios/PortfolioAnalytics';
import { WatchlistView } from './watchlist/Watchlist';
import { PositionDetail } from './positions/PositionDetail';
import { TransactionsBrowser } from './transactions/TransactionsBrowser';
import { Reports } from './reports/Reports';
import { Performance } from './performance/Performance';
import { Allocation } from './allocation/Allocation';
import { Alerts } from './alerts/Alerts';
import { AlertHistory } from './alerts/AlertHistory';
import { CapitalGains } from './capgains/CapitalGains';
import { News } from './news/News';
import { Earnings } from './earnings/Earnings';
import { EconomicCalendar } from './calendar/EconomicCalendar';
import { EarningsCalendar } from './calendar/EarningsCalendar';
import { Goals } from './goals/Goals';
import { Settings } from './settings/Settings';
import { Screener } from './screener/Screener';
import { ReportSchedules } from './reports/ReportSchedules';
import { ShareLinks } from './reports/ShareLinks';
import { PublicPortfolioView } from './share/PublicPortfolioView';
import { Cash } from './cash/Cash';
import { PositionNotes } from './notes/PositionNotes';
import { AuditLog } from './admin/AuditLog';
import { ModelTemplates } from './templates/ModelTemplates';
import { AccountSettings } from './account/AccountSettings';
import { ApiKeys } from './account/ApiKeys';

export const router = createBrowserRouter([
  { path: '/share/:token', element: <PublicPortfolioView /> },
  {
    path: '/',
    element: <Shell />,
    children: [
      { index: true, element: <Dashboard /> },
      { path: 'portfolios', element: <PortfolioList /> },
      { path: 'portfolios/:id', element: <PortfolioDetail /> },
      { path: 'portfolios/:id/positions/:symbol', element: <PositionDetail /> },
      { path: 'portfolios/:id/performance', element: <Performance /> },
      { path: 'portfolios/:id/history', element: <PortfolioHistory /> },
      { path: 'portfolios/:id/capital-flow', element: <CapitalFlow /> },
      { path: 'portfolios/:id/analytics', element: <PortfolioAnalytics /> },
      { path: 'portfolios/:id/allocation', element: <Allocation /> },
      { path: 'portfolios/:id/reports', element: <Reports /> },
      { path: 'portfolios/:id/capital-gains', element: <CapitalGains /> },
      { path: 'reports/schedules', element: <ReportSchedules /> },
      { path: 'reports/share', element: <ShareLinks /> },
      { path: 'transactions', element: <TransactionsBrowser /> },
      { path: 'watchlist', element: <WatchlistView /> },
      { path: 'screener', element: <Screener /> },
      { path: 'alerts', element: <Alerts /> },
      { path: 'alerts/history', element: <AlertHistory /> },
      { path: 'news', element: <News /> },
      { path: 'earnings', element: <Earnings /> },
      { path: 'calendar/economic', element: <EconomicCalendar /> },
      { path: 'calendar/earnings', element: <EarningsCalendar /> },
      { path: 'goals', element: <Goals /> },
      { path: 'cash', element: <Cash /> },
      { path: 'notes', element: <PositionNotes /> },
      { path: 'templates', element: <ModelTemplates /> },
      { path: 'admin/audit', element: <AuditLog /> },
      { path: 'account', element: <AccountSettings /> },
      { path: 'account/api-keys', element: <ApiKeys /> },
      { path: 'settings', element: <Settings /> }
    ]
  }
]);
