import { Card, Group, Stack, Text, Title, Tooltip } from '@mantine/core';
import type { CorrelationDto } from '../api/types';

interface Props {
  data: CorrelationDto;
}

/**
 * Heatmap-style table rendering a symbol-vs-symbol Pearson correlation matrix.
 * Colour scale runs red (-1) → white (0) → green (+1), matching the convention
 * traders expect (red = moves opposite, green = moves together = concentration).
 */
export function CorrelationMatrix({ data }: Props) {
  if (data.symbols.length < 2 || data.matrix.length === 0) {
    return (
      <Card withBorder>
        <Title order={5} mb="xs">Symbol correlation</Title>
        <Text c="dimmed" size="sm">Need at least two held symbols with overlapping price history to build a matrix.</Text>
      </Card>
    );
  }

  const cellPx = 56;
  return (
    <Card withBorder>
      <Group justify="space-between" mb="xs">
        <Title order={5}>Symbol correlation</Title>
        <Text size="xs" c="dimmed">{data.from} → {data.to} · Pearson of daily log returns</Text>
      </Group>
      <Stack gap={0} style={{ overflowX: 'auto' }}>
        <Group gap={0} wrap="nowrap" style={{ paddingLeft: cellPx }}>
          {data.symbols.map((s) => (
            <Text key={`h-${s}`} size="xs" fw={600} ta="center" style={{ width: cellPx }}>{s}</Text>
          ))}
        </Group>
        {data.symbols.map((row, i) => (
          <Group key={`r-${row}`} gap={0} wrap="nowrap" align="center">
            <Text size="xs" fw={600} style={{ width: cellPx }}>{row}</Text>
            {data.symbols.map((col, j) => {
              const v = data.matrix[i]?.[j] ?? 0;
              const bg = correlationColor(v);
              return (
                <Tooltip key={`c-${row}-${col}`} label={`${row} vs ${col}: ${v.toFixed(2)}`} withArrow>
                  <div
                    style={{
                      width: cellPx,
                      height: cellPx,
                      background: bg,
                      color: Math.abs(v) > 0.5 ? '#fff' : '#222',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      fontSize: 12,
                      fontWeight: 500,
                      border: '1px solid var(--mantine-color-default-border)',
                    }}
                  >
                    {v.toFixed(2)}
                  </div>
                </Tooltip>
              );
            })}
          </Group>
        ))}
      </Stack>
    </Card>
  );
}

function correlationColor(v: number): string {
  const clamped = Math.max(-1, Math.min(1, v));
  if (clamped >= 0) {
    // white (0) → green (+1)
    const alpha = clamped;
    return `rgba(34, 139, 230, ${alpha.toFixed(2)})`; // mantine blue.6 scaled
  }
  // white (0) → red (-1)
  const alpha = -clamped;
  return `rgba(250, 82, 82, ${alpha.toFixed(2)})`; // mantine red.6 scaled
}
