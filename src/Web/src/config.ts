export const config = {
  // Default to '' so the SPA talks same-origin to the API via the App Gateway path-route
  // (/api/*, /hubs/*, /health). Override with VITE_API_BASE_URL at build time only for
  // local dev (e.g. http://localhost:5170). Setting it in a deployed build defeats the
  // same-origin guarantee that protects the API behind the gateway.
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL ?? '',
  google: {
    clientId: import.meta.env.VITE_GOOGLE_CLIENT_ID ?? ''
  }
} as const;
