export const config = {
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5170',
  entra: {
    tenantId: import.meta.env.VITE_ENTRA_TENANT_ID ?? '',
    spaClientId: import.meta.env.VITE_ENTRA_SPA_CLIENT_ID ?? '',
    apiScope: import.meta.env.VITE_ENTRA_API_SCOPE ?? 'api://stocky/access'
  }
} as const;
