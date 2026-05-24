import { useState } from 'react';
import { Badge, Card, Group, Loader, SegmentedControl, Stack, Table, Text, Title, Anchor, NumberInput } from '@mantine/core';
import { Link } from 'react-router-dom';
import { useEarningsCalendar, useEarningsSurprises } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';
import { ApiErrorAlert } from '../../components/ApiErrorAlert';

/** M9 #95 — Scoped earnings calendar with EPS surprise drilldown. */
function isoDate(d: Date) { return d.toISOString().slice(0, 10); }

function SurpriseDrilldown({ symbol }: { symbol: string }) {
  const { data, isLoading } = useEarningsSurprises(symbol, 8);
  if (isLoading) return <Loader size="xs" />;
  if (!data || data.length === 0) return <Text c="dimmed" size="xs">No surprise history.</Text>;
  return (
    <Table fz="xs" withRowBorders={false}>
      <Table.Thead>
        <Table.Tr><Table.Th>Q-end</Table.Th><Table.Th>Est.</Table.Th><Table.Th>Actual</Table.Th><Table.Th>Surprise</Table.Th></Table.Tr>
      </Table.Thead>
      <Table.Tbody>
        {data.map(s => (
          <Table.Tr key={s.date}>
            <Table.Td>{s.date}</Table.Td>
            <Table.Td>{s.epsEstimate?.toFixed(2) ?? '—'}</Table.Td>
            <Table.Td>{s.epsActual?.toFixed(2) ?? '—'}</Table.Td>
            <Table.Td c={(s.surprisePercent ?? 0) >= 0 ? 'teal' : 'red'}>
              {s.surprisePercent == null ? '—' : `${s.surprisePercent >= 0 ? '+' : ''}${s.surprisePercent.toFixed(1)}%`}
            </Table.Td>
          </Table.Tr>
        ))}
      </Table.Tbody>
    </Table>
  );
}

export function EarningsCalendar() {
  const today = new Date();
  const [scope, setScope] = useState<'holdings' | 'watchlist' | 'all'>('holdings');
  const [days, setDays] = useState<number>(30);
  const [expanded, setExpanded] = useState<string | null>(null);
  const from = isoDate(today);
  const to = isoDate(new Date(today.getTime() + days * 86400000));
  const { data, isLoading, error } = useEarningsCalendar({ from, to, scope });

  if (error) return <ApiErrorAlert error={error} title="Could not load earnings calendar" />;

  const grouped = (data ?? []).reduce<Record<string, NonNullable<typeof data>>>((acc, ev) => {
    (acc[ev.date] ||= []).push(ev); return acc;
  }, {});

  return (
    <Stack>
      <Group justify="space-between" align="flex-end">
        <Title order={3}>Earnings Calendar</Title>
        <Group>
          <SegmentedControl value={scope} onChange={(v) => setScope(v as typeof scope)}
            data={[
              { label: 'My holdings', value: 'holdings' },
              { label: 'Watchlist', value: 'watchlist' },
              { label: 'All', value: 'all' },
            ]} />
          <NumberInput value={days} onChange={(v) => setDays(typeof v === 'number' ? v : 30)} min={7} max={365} step={7} w={120} label="Days" />
        </Group>
      </Group>

      {isLoading ? <Loader /> : !data || data.length === 0 ? (
        <EmptyState title="No upcoming earnings"
          description={scope === 'holdings' ? 'Add positions to your portfolios to populate this view.' : 'Try widening the date range or changing scope.'} />
      ) : Object.entries(grouped).map(([date, events]) => (
        <Card withBorder key={date}>
          <Title order={5} mb="xs">{date}</Title>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Symbol</Table.Th>
                <Table.Th>Company</Table.Th>
                <Table.Th>Period</Table.Th>
                <Table.Th>Hour</Table.Th>
                <Table.Th>EPS est.</Table.Th>
                <Table.Th>Rev est.</Table.Th>
                <Table.Th>Surprises</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {events.map(ev => (
                <>
                  <Table.Tr key={ev.symbol + ev.date}>
                    <Table.Td><Anchor component={Link} to={`/positions/${ev.symbol}`} fw={600}>{ev.symbol}</Anchor></Table.Td>
                    <Table.Td>—</Table.Td>
                    <Table.Td><Badge variant="light">{ev.time ?? '—'}</Badge></Table.Td>
                    <Table.Td>{ev.time ?? '—'}</Table.Td>
                    <Table.Td>{ev.epsEstimate?.toFixed(2) ?? '—'}</Table.Td>
                    <Table.Td>{ev.revenueEstimate ? `$${(ev.revenueEstimate / 1e9).toFixed(2)}B` : '—'}</Table.Td>
                    <Table.Td>
                      <Anchor component="button" type="button" onClick={() => setExpanded(expanded === ev.symbol ? null : ev.symbol)}>
                        {expanded === ev.symbol ? 'Hide' : 'Show'}
                      </Anchor>
                    </Table.Td>
                  </Table.Tr>
                  {expanded === ev.symbol && (
                    <Table.Tr key={ev.symbol + '-surprises'}>
                      <Table.Td colSpan={7}><SurpriseDrilldown symbol={ev.symbol} /></Table.Td>
                    </Table.Tr>
                  )}
                </>
              ))}
            </Table.Tbody>
          </Table>
        </Card>
      ))}
    </Stack>
  );
}
