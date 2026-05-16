import { createBrowserRouter } from 'react-router-dom';
import { Shell } from './shell/Shell';
import { RequireAuth } from '../auth/RequireAuth';
import { Login } from './login/Login';
import { Dashboard } from './dashboard/Dashboard';
import { PortfolioList } from './portfolios/PortfolioList';
import { PortfolioDetail } from './portfolios/PortfolioDetail';
import { PortfolioHistory } from './portfolios/PortfolioHistory';
import { CapitalFlow } from './portfolios/CapitalFlow';
import { WatchlistView } from './watchlist/Watchlist';
import { PositionDetail } from './positions/PositionDetail';
import { TransactionsBrowser } from './transactions/TransactionsBrowser';
import { Reports } from './reports/Reports';
import { Performance } from './performance/Performance';
import { Allocation } from './allocation/Allocation';
import { Alerts } from './alerts/Alerts';
import { CapitalGains } from './capgains/CapitalGains';
import { News } from './news/News';
import { Earnings } from './earnings/Earnings';
import { Settings } from './settings/Settings';

export const router = createBrowserRouter([
  { path: '/login', element: <Login /> },
  {
    path: '/',
    element: <RequireAuth><Shell /></RequireAuth>,
    children: [
      { index: true, element: <Dashboard /> },
      { path: 'portfolios', element: <PortfolioList /> },
      { path: 'portfolios/:id', element: <PortfolioDetail /> },
      { path: 'portfolios/:id/positions/:symbol', element: <PositionDetail /> },
      { path: 'portfolios/:id/performance', element: <Performance /> },
      { path: 'portfolios/:id/history', element: <PortfolioHistory /> },
      { path: 'portfolios/:id/capital-flow', element: <CapitalFlow /> },
      { path: 'portfolios/:id/allocation', element: <Allocation /> },
      { path: 'portfolios/:id/reports', element: <Reports /> },
      { path: 'portfolios/:id/capital-gains', element: <CapitalGains /> },
      { path: 'transactions', element: <TransactionsBrowser /> },
      { path: 'watchlist', element: <WatchlistView /> },
      { path: 'alerts', element: <Alerts /> },
      { path: 'news', element: <News /> },
      { path: 'earnings', element: <Earnings /> },
      { path: 'settings', element: <Settings /> }
    ]
  }
]);
