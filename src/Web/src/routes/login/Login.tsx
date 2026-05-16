import { Button, Card, Center, Stack, Text, Title } from '@mantine/core';
import { IconBrandWindows } from '@tabler/icons-react';
import { useMsal, useIsAuthenticated } from '@azure/msal-react';
import { Navigate } from 'react-router-dom';
import { loginRequest, isAuthConfigured } from '../../auth/msal';

export function Login() {
  const { instance } = useMsal();
  const isAuthed = useIsAuthenticated();

  if (isAuthed) return <Navigate to="/" replace />;

  return (
    <Center mih="100vh" p="md">
      <Card withBorder radius="md" padding="xl" maw={400} w="100%">
        <Stack align="center" gap="md">
          <Title order={2}>Sign in to Stocky</Title>
          <Text c="dimmed" ta="center">Track positions, trades, and watchlists across your accounts.</Text>
          <Button
            fullWidth
            size="md"
            leftSection={<IconBrandWindows size={18} />}
            onClick={() => instance.loginRedirect(loginRequest)}
            disabled={!isAuthConfigured}
          >
            Continue with Microsoft
          </Button>
          {!isAuthConfigured && (
            <Text size="xs" c="dimmed" ta="center">
              Entra is not configured. Set <code>VITE_ENTRA_TENANT_ID</code> and <code>VITE_ENTRA_SPA_CLIENT_ID</code>.
            </Text>
          )}
        </Stack>
      </Card>
    </Center>
  );
}
