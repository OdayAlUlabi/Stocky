import { PublicClientApplication, type Configuration, type RedirectRequest } from '@azure/msal-browser';
import { config } from '../config';

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

export const msalInstance = new PublicClientApplication(msalConfig);

export const loginRequest: RedirectRequest = {
  scopes: [config.entra.apiScope, 'openid', 'profile', 'offline_access']
};

export const apiTokenRequest = { scopes: [config.entra.apiScope] };

/** True when MSAL has been configured with real values via env vars. */
export const isAuthConfigured = Boolean(config.entra.tenantId && config.entra.spaClientId);
