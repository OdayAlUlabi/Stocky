import { createContext, useCallback, useContext, useEffect, useReducer, useState, type ReactNode } from 'react';
import { GoogleOAuthProvider, googleLogout, useGoogleOneTapLogin } from '@react-oauth/google';
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
    const stored = localStorage.getItem('google_credential');
    if (stored && !isExpired(stored)) return stored;
    localStorage.removeItem('google_credential');
    return null;
  });

  const user = extractUser(credential);
  // When auth is not configured treat as authenticated so the UI is browsable without login.
  const isAuthenticated = !isAuthConfigured || Boolean(credential);

  const setCredential = useCallback((cred: string | null) => {
    if (cred) {
      localStorage.setItem('google_credential', cred);
    } else {
      localStorage.removeItem('google_credential');
    }
    setCredentialState(cred);
  }, []);

  const signOut = useCallback(() => {
    googleLogout();
    setCredential(null);
  }, [setCredential]);

  // At token expiry, trigger a re-render so SilentOneTap (disabled while token is
  // valid) re-enables and silently obtains a fresh credential via auto_select.
  // We do NOT clear credential here — that immediately shows the login page.
  // The credential is only cleared when the API explicitly rejects it (hadToken=true 401).
  const [, forceRefresh] = useReducer((n: number) => n + 1, 0);
  useEffect(() => {
    if (!credential) return;
    try {
      const base64 = credential.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(base64)) as { exp?: number };
      if (typeof payload.exp !== 'number') return;
      const expiresAt = payload.exp * 1000;
      const now = Date.now();
      if (now >= expiresAt) { forceRefresh(); return; }
      const t = setTimeout(forceRefresh, expiresAt - now);
      return () => clearTimeout(t);
    } catch { /* ignore malformed token */ }
  }, [credential]);

  // Auto sign-out when any API call receives a 401 with a token that was rejected.
  // Ignore 401s from requests that had no token (e.g. race condition on login).
  useEffect(() => {
    if (!isAuthConfigured) return;
    const handler = (e: Event) => {
      const { hadToken } = (e as CustomEvent<{ hadToken: boolean }>).detail;
      if (hadToken) setCredential(null);
    };
    window.addEventListener('stocky:unauthorized', handler);
    return () => window.removeEventListener('stocky:unauthorized', handler);
  }, [setCredential]);

  return (
    <GoogleOAuthProvider clientId={config.google.clientId || 'unconfigured'}>
      <GoogleAuthContext.Provider value={{ credential, user, isAuthenticated, setCredential, signOut }}>
        <SilentOneTap credential={credential} setCredential={setCredential} />
        {children}
      </GoogleAuthContext.Provider>
    </GoogleOAuthProvider>
  );
}

/**
 * Silently fires Google One Tap when there is no credential (after expiry or
 * first load). We intentionally do NOT pre-warm while a valid token exists —
 * showing the One Tap dialog during an active session causes the user to dismiss
 * it, which triggers Google's exponential cooldown and prevents the auto-select
 * from working at the next expiry.
 */
function SilentOneTap({
  credential,
  setCredential,
}: {
  credential: string | null;
  setCredential: (c: string | null) => void;
}) {
  useGoogleOneTapLogin({
    onSuccess: (res) => {
      if (res.credential) setCredential(res.credential);
    },
    onError: () => {},
    // Fire when there is no credential, or when the existing credential has expired.
    // Do NOT fire while the credential is still valid — showing One Tap during an active
    // session causes dismissals that trigger Google's exponential cooldown.
    disabled: !isAuthConfigured || (Boolean(credential) && !isExpired(credential!)),
    auto_select: true,
  });
  return null;
}

export function useGoogleAuth(): GoogleAuthState {
  const ctx = useContext(GoogleAuthContext);
  if (!ctx) throw new Error('useGoogleAuth must be inside GoogleAuthProvider');
  return ctx;
}
