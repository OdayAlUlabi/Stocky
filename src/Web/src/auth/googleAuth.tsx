import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
import { GoogleOAuthProvider, googleLogout } from '@react-oauth/google';
import { config } from '../config';

/** True when Google OAuth has been configured via VITE_GOOGLE_CLIENT_ID. */
export const isAuthConfigured = Boolean(config.google.clientId);

export interface GoogleUser {
  sub: string;
  name: string;
  email: string;
  picture?: string;
}

interface GoogleAuthState {
  credential: string | null;
  user: GoogleUser | null;
  isAuthenticated: boolean;
  setCredential: (credential: string | null) => void;
  signOut: () => void;
}

const GoogleAuthContext = createContext<GoogleAuthState | null>(null);

function decodePayload(token: string): Record<string, unknown> {
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    return JSON.parse(atob(base64));
  } catch {
    return {};
  }
}

function isExpired(token: string): boolean {
  const exp = decodePayload(token)['exp'];
  return typeof exp !== 'number' || Date.now() >= exp * 1000;
}

function extractUser(token: string | null): GoogleUser | null {
  if (!token) return null;
  const p = decodePayload(token);
  const sub = typeof p['sub'] === 'string' ? p['sub'] : '';
  if (!sub) return null;
  return {
    sub,
    name: String(p['name'] ?? p['email'] ?? 'User'),
    email: String(p['email'] ?? ''),
    picture: typeof p['picture'] === 'string' ? p['picture'] : undefined,
  };
}

export function GoogleAuthProvider({ children }: { children: ReactNode }) {
  const [credential, setCredentialState] = useState<string | null>(() => {
    const stored = sessionStorage.getItem('google_credential');
    if (stored && !isExpired(stored)) return stored;
    sessionStorage.removeItem('google_credential');
    return null;
  });

  const user = extractUser(credential);
  // When auth is not configured treat as authenticated so the UI is browsable without login.
  const isAuthenticated = !isAuthConfigured || Boolean(credential);

  const setCredential = useCallback((cred: string | null) => {
    if (cred) {
      sessionStorage.setItem('google_credential', cred);
    } else {
      sessionStorage.removeItem('google_credential');
    }
    setCredentialState(cred);
  }, []);

  const signOut = useCallback(() => {
    googleLogout();
    setCredential(null);
  }, [setCredential]);

  // Auto sign-out when any API call receives a 401 (expired or missing token).
  useEffect(() => {
    if (!isAuthConfigured) return;
    const handler = () => setCredential(null);
    window.addEventListener('stocky:unauthorized', handler);
    return () => window.removeEventListener('stocky:unauthorized', handler);
  }, [setCredential]);

  return (
    <GoogleOAuthProvider clientId={config.google.clientId || 'unconfigured'}>
      <GoogleAuthContext.Provider value={{ credential, user, isAuthenticated, setCredential, signOut }}>
        {children}
      </GoogleAuthContext.Provider>
    </GoogleOAuthProvider>
  );
}

export function useGoogleAuth(): GoogleAuthState {
  const ctx = useContext(GoogleAuthContext);
  if (!ctx) throw new Error('useGoogleAuth must be inside GoogleAuthProvider');
  return ctx;
}
