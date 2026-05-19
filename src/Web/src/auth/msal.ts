import { Logger, LogLevel, PublicClientApplication, type Configuration, type RedirectRequest } from '@azure/msal-browser';
import { config } from '../config';

/** True when MSAL has been configured with real values via env vars. */
export const isAuthConfigured = Boolean(config.entra.tenantId && config.entra.spaClientId);

const authority = config.entra.tenantId
  ? `https://login.microsoftonline.com/${config.entra.tenantId}`
  : 'https://login.microsoftonline.com/common';

const msalConfig: Configuration = {
  auth: {
    clientId: config.entra.spaClientId || '00000000-0000-0000-0000-000000000000',
    authority,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
    navigateToLoginRequestUrl: true
  },
  cache: { cacheLocation: 'sessionStorage', storeAuthStateInCookie: false }
};

/**
 * Create the MSAL PublicClientApplication instance.
 *
 * MSAL 4.x requires `window.crypto` (Web Crypto API) which is only available in
 * secure contexts (HTTPS / localhost). When the app is served over plain HTTP the
 * constructor throws `BrowserAuthError: crypto_nonexistent` before React even
 * mounts, producing a blank page.
 *
 * When Entra is not configured (isAuthConfigured = false) we never call any real
 * auth operations, so we return a no-op stub that satisfies MsalProvider's
 * interface requirements without requiring crypto.
 */
function createMsalInstance(): PublicClientApplication {
  if (isAuthConfigured) {
    // Real instance — crypto must be available (i.e. HTTPS).
    return new PublicClientApplication(msalConfig);
  }

  // No-op stub for unauthenticated / HTTP deployments.
  const noOpLogger = new Logger({ loggerCallback: () => {}, logLevel: LogLevel.Error });
  return {
    getAllAccounts: () => [],
    getActiveAccount: () => null,
    setActiveAccount: () => {},
    addEventCallback: () => null,
    removeEventCallback: () => {},
    handleRedirectPromise: () => Promise.resolve(null),
    initialize: () => Promise.resolve(),
    loginRedirect: () => Promise.resolve(),
    logoutRedirect: () => Promise.resolve(),
    acquireTokenSilent: () => Promise.reject(new Error('Auth not configured')),
    acquireTokenRedirect: () => Promise.resolve(),
    getLogger: () => noOpLogger,
    setLogger: () => {},
    initializeWrapperLibrary: () => {},
    setNavigationClient: () => {},
    getConfiguration: () => ({}) as never,
    enableAccountStorageEvents: () => {},
    disableAccountStorageEvents: () => {},
    addPerformanceCallback: () => '',
    removePerformanceCallback: () => false,
    clearCache: () => Promise.resolve(),
    getTokenCache: () => ({}) as never,
  } as unknown as PublicClientApplication;
}

export const msalInstance = createMsalInstance();

export const loginRequest: RedirectRequest = {
  scopes: [config.entra.apiScope, 'openid', 'profile', 'offline_access']
};

export const apiTokenRequest = { scopes: [config.entra.apiScope] };
