import { useMsal } from '@azure/msal-react';
import { useCallback } from 'react';
import { apiTokenRequest, isAuthConfigured } from './msal';
import { InteractionRequiredAuthError } from '@azure/msal-browser';

/**
 * Returns a function that resolves to an access token for the API scope.
 * When auth is not configured (local dev without Entra), returns undefined so
 * the API can be called anonymously while standing up the UI.
 */
export function useApiToken() {
  const { instance, accounts } = useMsal();

  return useCallback(async (): Promise<string | undefined> => {
    if (!isAuthConfigured) return undefined;
    const account = accounts[0];
    if (!account) return undefined;
    try {
      const result = await instance.acquireTokenSilent({ ...apiTokenRequest, account });
      return result.accessToken;
    } catch (err) {
      if (err instanceof InteractionRequiredAuthError) {
        await instance.acquireTokenRedirect({ ...apiTokenRequest, account });
        return undefined;
      }
      throw err;
    }
  }, [instance, accounts]);
}
