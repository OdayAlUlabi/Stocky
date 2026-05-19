import { useCallback } from 'react';
import { useGoogleAuth, isAuthConfigured } from './googleAuth';

/**
 * Returns a function that resolves to a Google ID token for API Bearer auth.
 * When auth is not configured (local dev without Google OAuth), returns undefined.
 * Clears the stored credential and triggers re-auth if the token has expired.
 */
export function useApiToken() {
  const { credential, setCredential } = useGoogleAuth();

  return useCallback(async (): Promise<string | undefined> => {
    if (!isAuthConfigured) return undefined;
    if (!credential) return undefined;
    // Clear and force re-login if the token has expired.
    try {
      const base64 = credential.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const payload = JSON.parse(atob(base64));
      if (typeof payload.exp === 'number' && Date.now() >= payload.exp * 1000) {
        setCredential(null);
        return undefined;
      }
    } catch {
      setCredential(null);
      return undefined;
    }
    return credential;
  }, [credential, setCredential]);
}
