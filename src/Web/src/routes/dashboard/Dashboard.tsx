import { Card, Grid, Group, ScrollArea, SegmentedControl, Skeleton, Stack, Table, Text, Title } from '@mantine/core';
import { IconArrowDownRight, IconArrowUpRight, IconBriefcase, IconCoin, IconWallet } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { CartesianGrid, Cell, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import dayjs from 'dayjs';
import { MetricCard } from '../../components/MetricCard';
import { EmptyState } from '../../components/EmptyState';
import { useDashboard, usePortfolios } from '../../api/hooks';

const PIE_COLORS = ['#228be6', '#15aabf', '#40c057', '#fab005', '#fa5252', '#7950f2', '#e64980'];

function fmtCurrency(v: number, ccy: string) {
  try { return new Intl.NumberFormat(undefined, { style: 'currency', currency: ccy }).format(v); }
  catch { return `${ccy} ${v.toFixed(2)}`; }
}
function fmtPct(v: number) { return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`; }

export function Dashboard() {
  const portfolios = usePortfolios();
  const [scope, setScope] = useState<string>('all');
  const portfolioId = scope === 'all' ? undefined : scope;
  const { data, isLoading } = useDashboard(portfolioId);

  const segments = useMemo(() => {
    const opts = [{ label: 'All', value: 'all' }];
    for (const p of portfolios.data ?? []) opts.push({ label: p.name, value: p.id });
    return opts;
  }, [portfolios.data]);

  const currency = data?.currency ?? 'USD';

  return (
    <Stack gap="lg">
      <Group justify="space-between" align="flex-end" wrap="wrap">
        <Stack gap={4}>
          <Title order={2}>Dashboard</Title>
          {data && <Text c="dimmed" size="sm">As of {dayjs(data.asOf).format('MMM D, YYYY HH:mm')}</Text>}
        </Stack>
        {segments.length > 1 && (
          <SegmentedControl data={segments} value={scope} onChange={setScope} />
        )}
      </Group>

      {isLoading ? (
        <Grid>
          {[0, 1, 2, 3].map((i) => (
            <Grid.Col span={{ base: 12, sm: 6, md: 3 }} key={i}><Skeleton h={96} radius="md" /></Grid.Col>
          ))}
        </Grid>
      ) : !data || (data.totalValue === 0 && data.cashBalance === 0) ? (
        <EmptyState
          icon={<IconBriefcase size={48} stroke={1.2} />}
          title="No holdings yet"
          description="Create a portfolio and log your first trade to see KPIs, allocations, and movers."
        />
      ) : (
        <>
          <Grid>
            <Grid.Col span={{ base: 12, sm: 6, md: 3 }}>
              <MetricCard label="Total equity" value={fmtCurrency(data.totalEquity, currency)} icon={<IconWallet size={18} />} hint={`Market ${fmtCurrency(data.totalValue, currency)} · Cash ${fmtCurrency(data.cashBalance, currency)}`} />
            </Grid.Col>
            <Grid.Col span={{ base: 12, sm: 6, md: 3 }}>
              <MetricCard
                label="Day P&L"
                value={fmtCurrency(data.dayPnL, currency)}
                hint={fmtPct(data.dayPnLPercent)}
                trend={data.dayPnL >= 0 ? 'up' : 'down'}
                icon={data.dayPnL >= 0 ? <IconArrowUpRight size={18} /> : <IconArrowDownRight size={18} />}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, sm: 6, md: 3 }}>
              <MetricCard
                label="Total return"
                value={fmtCurrency(data.totalReturn, currency)}
                hint={fmtPct(data.totalReturnPercent)}
                trend={data.totalReturn >= 0 ? 'up' : 'down'}
                icon={<IconCoin size={18} />}
              />
            </Grid.Col>
            <Grid.Col span={{ base: 12, sm: 6, md: 3 }}>
              <MetricCard label="Positions" value={String(data.sectorAllocation.length || data.assetClassAllocation.length)} hint="Distinct allocations" />
            </Grid.Col>
          </Grid>

          <Grid>
            <Grid.Col span={{ base: 12, md: 8 }}>
              <Card withBorder radius="md" padding="md" h={320}>
                <Stack gap="xs" h="100%">
                  <Title order={5}>Portfolio value (90d)</Title>
                  {data.valueHistory.length === 0 ? (
                    <EmptyState title="No history yet" description="History will populate as quotes are captured daily." />
                  ) : (
                    <ResponsiveContainer width="100%" height="100%">
                      <LineChart data={data.valueHistory.map((p) => ({ date: dayjs(p.date).format('MMM D'), value: p.value }))}>
                        <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
                        <XAxis dataKey="date" tick={{ fontSize: 11 }} />
                        <YAxis tick={{ fontSize: 11 }} width={64} />
                        <Tooltip formatter={(v: number) => fmtCurrency(v, currency)} />
                        <Line type="monotone" dataKey="value" stroke="#228be6" strokeWidth={2} dot={false} />
                      </LineChart>
                    </ResponsiveContainer>
                  )}
                </Stack>
              </Card>
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 4 }}>
              <Card withBorder radius="md" padding="md" h={320}>
                <Stack gap="xs" h="100%">
                  <Title order={5}>Allocation</Title>
                  {data.assetClassAllocation.length === 0 ? (
                    <EmptyState title="No allocation" />
                  ) : (
                    <ResponsiveContainer width="100%" height="100%">
                      <PieChart>
                        <Pie data={data.assetClassAllocation} dataKey="value" nameKey="label" outerRadius={80} label={(e: { label: string }) => e.label}>
                          {data.assetClassAllocation.map((_, i) => <Cell key={i} fill={PIE_COLORS[i % PIE_COLORS.length]} />)}
                        </Pie>
                        <Tooltip formatter={(v: number) => fmtCurrency(v, currency)} />
                      </PieChart>
                    </ResponsiveContainer>
                  )}
                </Stack>
              </Card>
            </Grid.Col>
          </Grid>

          <Grid>
            <Grid.Col span={{ base: 12, md: 6 }}>
              <MoversCard title="Top gainers" rows={data.topGainers} currency={currency} positive />
            </Grid.Col>
            <Grid.Col span={{ base: 12, md: 6 }}>
              <MoversCard title="Top losers" rows={data.topLosers} currency={currency} positive={false} />
            </Grid.Col>
          </Grid>
        </>
      )}
    </Stack>
  );
}

function MoversCard({ title, rows, currency, positive }: { title: string; rows: { symbol: string; marketValue: number; dayChangePercent: number }[]; currency: string; positive: boolean }) {
  return (
    <Card withBorder radius="md" padding="md">
      <Stack gap="xs">
        <Title order={5}>{title}</Title>
        {rows.length === 0 ? <Text c="dimmed" size="sm">No data.</Text> : (
          <ScrollArea>
            <Table striped highlightOnHover withRowBorders={false}>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Symbol</Table.Th>
                  <Table.Th ta="right">Value</Table.Th>
                  <Table.Th ta="right">% Day</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {rows.map((r) => (
                  <Table.Tr key={r.symbol}>
                    <Table.Td><Text fw={500}>{r.symbol}</Text></Table.Td>
                    <Table.Td ta="right">{fmtCurrency(r.marketValue, currency)}</Table.Td>
                    <Table.Td ta="right" c={positive ? 'teal' : 'red'}>{fmtPct(r.dayChangePercent)}</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </ScrollArea>
        )}
      </Stack>
    </Card>
  );
}
