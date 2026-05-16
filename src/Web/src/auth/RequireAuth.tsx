import { Navigate, useLocation } from 'react-router-dom';
import { useIsAuthenticated } from '@azure/msal-react';
import { isAuthConfigured } from './msal';
import type { ReactNode } from 'react';

export function RequireAuth({ children }: { children: ReactNode }) {
  const isAuthed = useIsAuthenticated();
  const location = useLocation();
  // If auth isn't configured (local dev without Entra) treat as authed so the UI is browsable.
  if (!isAuthConfigured) return <>{children}</>;
  if (!isAuthed) return <Navigate to="/login" replace state={{ from: location }} />;
  return <>{children}</>;
}
