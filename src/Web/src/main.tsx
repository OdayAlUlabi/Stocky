import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import { MantineProvider, createTheme, localStorageColorSchemeManager } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { GoogleAuthProvider } from './auth/googleAuth';
import { router } from './routes/router';
import { ApiError } from './api/client';

import '@mantine/core/styles.css';
import '@mantine/notifications/styles.css';
import '@mantine/dates/styles.css';
import './index.css';

const theme = createTheme({
  primaryColor: 'blue',
  fontFamily: 'Inter, system-ui, -apple-system, Segoe UI, Roboto, sans-serif'
});

const colorSchemeManager = localStorageColorSchemeManager({ key: 'stocky-color-scheme' });

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        if (error instanceof ApiError && error.status === 401) return false;
        return failureCount < 1;
      }
    },
    mutations: {
      retry: (failureCount, error) => {
        if (error instanceof ApiError && error.status === 401) return false;
        return failureCount < 1;
      }
    }
  }
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <GoogleAuthProvider>
      <QueryClientProvider client={queryClient}>
        <MantineProvider theme={theme} defaultColorScheme="auto" colorSchemeManager={colorSchemeManager}>
          <Notifications position="top-right" />
          <RouterProvider router={router} />
        </MantineProvider>
      </QueryClientProvider>
    </GoogleAuthProvider>
  </StrictMode>
);
