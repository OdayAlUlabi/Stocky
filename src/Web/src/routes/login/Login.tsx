import { Card, Center, Stack, Text, Title } from '@mantine/core';
import { GoogleLogin } from '@react-oauth/google';
import { Navigate } from 'react-router-dom';
import { useGoogleAuth, isAuthConfigured } from '../../auth/googleAuth';

export function Login() {
  const { isAuthenticated, setCredential } = useGoogleAuth();

  if (isAuthenticated) return <Navigate to="/" replace />;

  return (
    <Center mih="100vh" p="md">
      <Card withBorder radius="md" padding="xl" maw={400} w="100%">
        <Stack align="center" gap="md">
          <Title order={2}>Sign in to Stocky</Title>
          <Text c="dimmed" ta="center">Track positions, trades, and watchlists across your accounts.</Text>
          {isAuthConfigured ? (
            <GoogleLogin
              onSuccess={(response) => {
                if (response.credential) setCredential(response.credential);
              }}
              onError={() => {}}
              useOneTap
              auto_select
            />
          ) : (
            <Text size="xs" c="dimmed" ta="center">
              Google OAuth is not configured. Set <code>VITE_GOOGLE_CLIENT_ID</code>.
            </Text>
          )}
        </Stack>
      </Card>
    </Center>
  );
}
