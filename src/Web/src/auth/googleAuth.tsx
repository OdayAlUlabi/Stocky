import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';
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

  // Clear the credential exactly when the token hard-expires so that
  // SilentOneTap (which is already pre-warming a new token) can swap it in.
  useEffect(() => {
    if (!credential) return;
    try {
      const base64 = credential.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(base64)) as { exp?: number };
      if (typeof payload.exp !== 'number') return;
      const expiresAt = payload.exp * 1000;
      const now = Date.now();
      if (now >= expiresAt) { setCredential(null); return; }
      const t = setTimeout(() => setCredential(null), expiresAt - now);
      return () => clearTimeout(t);
    } catch { /* ignore malformed token */ }
  }, [credential, setCredential]);

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
        <SilentOneTap credential={credential} setCredential={setCredential} />
        {children}
      </GoogleAuthContext.Provider>
    </GoogleOAuthProvider>
  );
}

const PREWARM_MS = 5 * 60 * 1000; // start One Tap 5 min before expiry

function getExpiry(token: string): number | null {
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const p = JSON.parse(atob(base64)) as { exp?: number };
    return typeof p.exp === 'number' ? p.exp * 1000 : null;
  } catch { return null; }
}

/**
 * Fires Google One Tap:
 *  - immediately when there is no credential (after expiry or first load)
 *  - proactively 5 min before the current token expires, so a fresh token
 *    is ready before the old one runs out — the user never sees the login page.
 */
function SilentOneTap({
  credential,
  setCredential,
}: {
  credential: string | null;
  setCredential: (c: string | null) => void;
}) {
  const [shouldPrompt, setShouldPrompt] = useState(() => {
    if (!credential) return true;
    const exp = getExpiry(credential);
    return exp === null || Date.now() >= exp - PREWARM_MS;
  });

  useEffect(() => {
    if (!credential) { setShouldPrompt(true); return; }
    const exp = getExpiry(credential);
    if (exp === null) return;
    const now = Date.now();
    if (now >= exp - PREWARM_MS) { setShouldPrompt(true); return; }
    const t = setTimeout(() => setShouldPrompt(true), exp - now - PREWARM_MS);
    return () => clearTimeout(t);
  }, [credential]);

  useGoogleOneTapLogin({
    onSuccess: (res) => {
      if (res.credential) {
        setCredential(res.credential);
        setShouldPrompt(false);
      }
    },
    onError: () => {},
    disabled: !isAuthConfigured || !shouldPrompt,
    auto_select: true,
  });
  return null;
}

export function useGoogleAuth(): GoogleAuthState {
  const ctx = useContext(GoogleAuthContext);
  if (!ctx) throw new Error('useGoogleAuth must be inside GoogleAuthProvider');
  return ctx;
}
