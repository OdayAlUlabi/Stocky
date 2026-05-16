import { Button, Center, Stack, Text, Title } from '@mantine/core';
import type { ReactNode } from 'react';

interface EmptyStateProps {
  title: string;
  description?: string;
  icon?: ReactNode;
  actionLabel?: string;
  onAction?: () => void;
}

export function EmptyState({ title, description, icon, actionLabel, onAction }: EmptyStateProps) {
  return (
    <Center py={48}>
      <Stack align="center" gap="xs" maw={420} ta="center">
        {icon}
        <Title order={4}>{title}</Title>
        {description && <Text c="dimmed">{description}</Text>}
        {actionLabel && onAction && (
          <Button mt="sm" onClick={onAction}>{actionLabel}</Button>
        )}
      </Stack>
    </Center>
  );
}
