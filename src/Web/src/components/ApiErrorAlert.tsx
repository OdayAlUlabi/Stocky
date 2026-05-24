import { Alert, Badge, Box, Code, List, Stack, Text } from '@mantine/core';
import { IconAlertCircle } from '@tabler/icons-react';
import { getApiErrorDetails } from '../api/client';

interface ApiErrorAlertProps {
  error: unknown;
  title?: string;
}

/**
 * Renders a full API error with status badge, detail text, and any
 * validation field errors returned by the server.
 */
export function ApiErrorAlert({ error, title = 'Request failed' }: ApiErrorAlertProps) {
  const details = getApiErrorDetails(error);

  return (
    <Alert
      color="red"
      icon={<IconAlertCircle size={16} />}
      title={
        <Box style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span>{title}</span>
          {details.status !== undefined && (
            <Badge color="red" variant="light" size="sm">{details.status}</Badge>
          )}
        </Box>
      }
    >
      <Stack gap={4}>
        <Text size="sm">{details.message}</Text>

        {details.detail && details.detail !== details.message && (
          <Text size="sm" c="dimmed">{details.detail}</Text>
        )}

        {details.fieldErrors && (
          <List size="sm" mt={4}>
            {Object.entries(details.fieldErrors).map(([field, msgs]) => (
              <List.Item key={field}>
                <Text span fw={600}>{field}: </Text>
                {msgs.join(', ')}
              </List.Item>
            ))}
          </List>
        )}

        {/* Show raw body for unexpected non-Problem-Details errors */}
        {!details.fieldErrors && !details.detail && error instanceof Error && error.message !== details.message && (
          <Code block mt={4} style={{ fontSize: 11, maxHeight: 120, overflow: 'auto' }}>
            {error.message}
          </Code>
        )}
      </Stack>
    </Alert>
  );
}
