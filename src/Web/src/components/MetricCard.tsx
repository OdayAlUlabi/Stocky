import { Card, Group, Stack, Text } from '@mantine/core';
import type { ReactNode } from 'react';

interface MetricCardProps {
  label: string;
  value: string;
  hint?: string;
  trend?: 'up' | 'down' | 'flat';
  icon?: ReactNode;
}

const trendColor: Record<NonNullable<MetricCardProps['trend']>, string> = {
  up: 'teal',
  down: 'red',
  flat: 'gray'
};

export function MetricCard({ label, value, hint, trend, icon }: MetricCardProps) {
  return (
    <Card withBorder radius="md" padding="md">
      <Stack gap={4}>
        <Group justify="space-between">
          <Text size="sm" c="dimmed">{label}</Text>
          {icon}
        </Group>
        <Text size="xl" fw={700}>{value}</Text>
        {hint && (
          <Text size="xs" c={trend ? trendColor[trend] : 'dimmed'}>{hint}</Text>
        )}
      </Stack>
    </Card>
  );
}
