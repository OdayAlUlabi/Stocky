import { ActionIcon, Badge, Card, Group, Loader, ScrollArea, SegmentedControl, SimpleGrid, Stack, Switch, Table, Text, Title } from '@mantine/core';
import { IconArrowLeft } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  Area,
  CartesianGrid,
  ComposedChart,
  Line,
  ResponsiveContainer,
  Scatter,
  Tooltip,
  XAxis,
  YAxis
} from 'recharts';
import { usePortfolioHistory } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';
import type { PortfolioHistoryEventDto } from '../../api/types';

const EVENT_COLOR: Record<string, string> = {
  Deposit: '#228be6',
  Withdrawal: '#fd7e14',
  Buy: '#12b886',
  Sell: '#fa5252',
  Dividend: '#40c057',
  Fee: '#868e96',
  Split: '#7950f2',
  SpinOff: '#e64980'
};

const ALL_EVENT_TYPES: ReadonlyArray<keyof typeof EVENT_COLOR> = [
  'Deposit',
  'Withdrawal',
  'Buy',
  'Sell',
  'Dividend',
  'Fee',
  'Split',
  'SpinOff'
];

function fmtCurrency(n: number, currency: string) {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(n);
}

export function PortfolioHistory() {
  const { id = '' } = useParams<{ id: string }>();
  const { data, isLoading } = usePortfolioHistory(id);

  const [range, setRange] = useState<'all' | '1y' | '6m' | '3m' | '1m'>('all');
  const [enabled, setEnabled] = useState<Record<string, boolean>>(
    () => Object.fromEntries(ALL_EVENT_TYPES.map((t) => [t, true]))
  );

  const filtered = useMemo(() => {
    if (!data) return null;
    const today = new Date();
    const cutoff = new Date(today);
    if (range === '1m') cutoff.setMonth(cutoff.getMonth() - 1);
    else if (range === '3m') cutoff.setMonth(cutoff.getMonth() - 3);
    else if (range === '6m') cutoff.setMonth(cutoff.getMonth() - 6);
    else if (range === '1y') cutoff.setFullYear(cutoff.getFullYear() - 1);
    else cutoff.setFullYear(1970);
    const cutoffIso = cutoff.toISOString().slice(0, 10);
    const series = data.series.filter((p) => p.date >= cutoffIso);
    const events = data.events.filter((e) => e.date >= cutoffIso && enabled[e.type] !== false);
    return { ...data, series, events };
  }, [data, range, enabled]);

  // Merge events onto the closest day on the series so Scatter can plot at the
  // right Y. Events are joined by exact date string match; if a weekend event
  // falls outside the series we drop it (rare for real-world ledgers).
  const chartRows = useMemo(() => {
    if (!filtered) return [];
    type Row = {
      date: string;
      totalEquity: number;
      netContributions: number;
      [eventKey: `evt_${string}`]: number | undefined;
    };
    const byDate = new Map<string, Row>();
    for (const p of filtered.series) {
      byDate.set(p.date, {
        date: p.date,
        totalEquity: p.totalEquity,
        netContributions: p.netContributions
      });
    }
    for (const e of filtered.events) {
      const row = byDate.get(e.date);
      if (!row) continue;
      row[`evt_${e.type}` as const] = row.totalEquity;
    }
    return Array.from(byDate.values());
  }, [filtered]);

  const currency = data?.currency ?? 'USD';

  return (
    <Stack>
      <Group justify="space-between" wrap="wrap">
        <Group>
          <ActionIcon component={Link} to={`/portfolios/${id}`} variant="subtle" aria-label="Back">
            <IconArrowLeft size={18} />
          </ActionIcon>
          <Title order={3}>Portfolio history</Title>
        </Group>
        <SegmentedControl
          value={range}
          onChange={(v) => setRange(v as typeof range)}
          data={[
            { value: '1m', label: '1M' },
            { value: '3m', label: '3M' },
            { value: '6m', label: '6M' },
            { value: '1y', label: '1Y' },
            { value: 'all', label: 'All' }
          ]}
        />
      </Group>

      {isLoading ? (
        <Loader />
      ) : !data || data.series.length === 0 ? (
        <EmptyState title="No history yet" description="Log a deposit or trade to start the timeline." />
      ) : (
        <>
          <SimpleGrid cols={{ base: 2, md: 4 }}>
            <Card withBorder>
              <Text size="xs" c="dimmed" tt="uppercase">Net contributions</Text>
              <Text fw={600} size="lg">{fmtCurrency(data.netContributions, currency)}</Text>
            </Card>
            <Card withBorder>
              <Text size="xs" c="dimmed" tt="uppercase">Total equity</Text>
              <Text fw={600} size="lg">{fmtCurrency(data.totalEquity, currency)}</Text>
            </Card>
            <Card withBorder>
              <Text size="xs" c="dimmed" tt="uppercase">Total return</Text>
              <Text fw={600} size="lg" c={data.totalReturn >= 0 ? 'teal' : 'red'}>
                {fmtCurrency(data.totalReturn, currency)}
              </Text>
            </Card>
            <Card withBorder>
              <Text size="xs" c="dimmed" tt="uppercase">Return %</Text>
              <Text fw={600} size="lg" c={data.totalReturnPercent >= 0 ? 'teal' : 'red'}>
                {data.totalReturnPercent.toFixed(2)}%
              </Text>
            </Card>
          </SimpleGrid>

          <Card withBorder>
            <Group justify="space-between" mb="xs" wrap="wrap">
              <Title order={5}>Equity vs net contributions</Title>
              <Group gap="xs">
                {ALL_EVENT_TYPES.map((t) => (
                  <Switch
                    key={t}
                    label={
                      <Badge color={EVENT_COLOR[t]} variant="filled" size="sm">
                        {t}
                      </Badge>
                    }
                    checked={enabled[t] !== false}
                    onChange={(e) => setEnabled((s) => ({ ...s, [t]: e.currentTarget.checked }))}
                    size="xs"
                  />
                ))}
              </Group>
            </Group>
            <ResponsiveContainer width="100%" height={380}>
              <ComposedChart data={chartRows}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" minTickGap={40} />
                <YAxis tickFormatter={(v) => `${Math.round(Number(v)).toLocaleString()}`} />
                <Tooltip
                  formatter={(v: number | string) =>
                    typeof v === 'number' ? fmtCurrency(v, currency) : v
                  }
                />
                <Area
                  type="monotone"
                  dataKey="totalEquity"
                  name="Total equity"
                  stroke="#228be6"
                  fill="#228be6"
                  fillOpacity={0.15}
                  isAnimationActive={false}
                />
                <Line
                  type="monotone"
                  dataKey="netContributions"
                  name="Net contributions"
                  stroke="#fab005"
                  dot={false}
                  isAnimationActive={false}
                />
                {ALL_EVENT_TYPES.map((t) =>
                  enabled[t] === false ? null : (
                    <Scatter
                      key={t}
                      dataKey={`evt_${t}`}
                      name={t}
                      fill={EVENT_COLOR[t]}
                      shape="circle"
                      isAnimationActive={false}
                    />
                  )
                )}
              </ComposedChart>
            </ResponsiveContainer>
          </Card>

          <Card withBorder>
            <Title order={5} mb="xs">Event timeline</Title>
            <ScrollArea h={320}>
              <Table striped highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Date</Table.Th>
                    <Table.Th>Type</Table.Th>
                    <Table.Th>Symbol</Table.Th>
                    <Table.Th ta="right">Quantity</Table.Th>
                    <Table.Th ta="right">Cash impact</Table.Th>
                    <Table.Th>Notes</Table.Th>
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {(filtered?.events ?? []).map((e: PortfolioHistoryEventDto, i) => (
                    <Table.Tr key={`${e.date}-${i}`}>
                      <Table.Td>{e.date}</Table.Td>
                      <Table.Td>
                        <Badge color={EVENT_COLOR[e.type] ?? 'gray'} variant="light">{e.type}</Badge>
                      </Table.Td>
                      <Table.Td>{e.symbol ?? '—'}</Table.Td>
                      <Table.Td ta="right">{e.quantity || '—'}</Table.Td>
                      <Table.Td ta="right" c={e.amount > 0 ? 'teal' : e.amount < 0 ? 'red' : undefined}>
                        {e.amount === 0 ? '—' : fmtCurrency(e.amount, currency)}
                      </Table.Td>
                      <Table.Td>{e.notes ?? ''}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </ScrollArea>
          </Card>
        </>
      )}
    </Stack>
  );
}
