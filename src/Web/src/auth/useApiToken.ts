import { useCallback } from 'react';
import { useGoogleAuth, isAuthConfigured } from './googleAuth';

// Google ID tokens are ~1 h. Refresh pre-emptively when we're within this window so
// in-flight calls don't hit the API with a token that's about to expire.
const REFRESH_WINDOW_MS = 5 * 60 * 1000;

/**
 * Returns a function that resolves to a Google ID token for API Bearer auth.
 * When auth is not configured (local dev without Google OAuth), returns undefined.
 * Clears the stored credential and triggers re-auth if the token has expired.
 * Best-effort fires a silent re-prompt when the token is close to expiry.
 */
export function useApiToken() {
  const { credential, setCredential } = useGoogleAuth();

  return useCallback(async (): Promise<string | undefined> => {
    if (!isAuthConfigured) return undefined;
    if (!credential) return undefined;
    try {
      const base64 = credential.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(base64));
      if (typeof payload.exp === 'number') {
        const expiresAt = payload.exp * 1000;
        if (Date.now() >= expiresAt) {
          setCredential(null);
          return undefined;
        }
        // Pre-emptive refresh: ask Google to re-prompt (best effort, non-blocking).
        // Returns the existing credential immediately so the in-flight request still goes.
        if (expiresAt - Date.now() <= REFRESH_WINDOW_MS) {
          try {
            const g = (window as unknown as {
              google?: { accounts?: { id?: { prompt?: () => void } } };
            }).google;
            g?.accounts?.id?.prompt?.();
          } catch {
            // Ignore — refresh is best effort. Expiry will trigger a hard re-login.
          }
        }
      }
    } catch {
      setCredential(null);
      return undefined;
    }
    return credential;
  }, [credential, setCredential]);
}
