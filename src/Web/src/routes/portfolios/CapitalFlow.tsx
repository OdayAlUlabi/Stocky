import {
  ActionIcon,
  Badge,
  Box,
  Card,
  Group,
  Loader,
  Popover,
  ScrollArea,
  Stack,
  Table,
  Text,
  Title,
  UnstyledButton
} from '@mantine/core';
import { IconArrowLeft, IconArrowRight } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { usePortfolioHistory } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';
import type { PortfolioHistoryEventDto } from '../../api/types';

type FlowKind = 'deposit' | 'sell-profit' | 'big-gain' | 'loss';

interface FlowNode {
  id: string;
  date: string;
  label: string;       // e.g. "Jul'23"
  title: string;       // e.g. "$500" or "RGTI ×3"
  subtitle: string;    // e.g. "Deposit" or "+737% $8 pos"
  kind: FlowKind;
  amount: number;      // cash amount in / realized proceeds
  realizedPnL?: number;
  realizedPct?: number;
  symbol?: string;
  events: PortfolioHistoryEventDto[]; // source events that built this node
  reinvested: Array<{ symbol: string; quantity: number; amount: number; date: string }>;
}

const KIND_COLOR: Record<FlowKind, string> = {
  'deposit': '#0ca678',          // teal
  'sell-profit': '#2f9e44',      // green
  'big-gain': '#f59f00',         // orange
  'loss': '#e03131'              // red
};

const KIND_LABEL: Record<FlowKind, string> = {
  'deposit': 'Cash deposit',
  'sell-profit': 'Sell profit → reinvest',
  'big-gain': 'Big gain → reinvest',
  'loss': 'Loss → reinvest'
};

function fmtCurrency(n: number, currency: string) {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 0 }).format(n);
}

function fmtMonth(iso: string): string {
  const d = new Date(iso);
  const month = d.toLocaleString('en-US', { month: 'short' });
  const yr = String(d.getFullYear()).slice(2);
  return `${month}'${yr}`;
}

// Walk events in order, track avg cost basis per symbol from Buys,
// emit a FlowNode per Deposit-cluster or per realized Sell, and bucket
// subsequent Buys (until the next inflow node) as "reinvested".
function buildFlow(events: PortfolioHistoryEventDto[]): FlowNode[] {
  if (!events?.length) return [];
  const sorted = [...events].sort((a, b) => a.date.localeCompare(b.date));
  const costBasis = new Map<string, { qty: number; cost: number }>(); // running avg-cost ledger
  const nodes: FlowNode[] = [];

  // First pass: emit nodes for deposits (clustered by 21-day gaps) and sells.
  let depositCluster: { events: PortfolioHistoryEventDto[]; amount: number } | null = null;
  const flushDeposit = () => {
    if (!depositCluster || depositCluster.events.length === 0) return;
    const first = depositCluster.events[0];
    nodes.push({
      id: `dep-${first.date}-${nodes.length}`,
      date: first.date,
      label: fmtMonth(first.date),
      title: fmtCurrency(depositCluster.amount, 'USD'),
      subtitle: 'Deposit',
      kind: 'deposit',
      amount: depositCluster.amount,
      events: depositCluster.events,
      reinvested: []
    });
    depositCluster = null;
  };

  for (const ev of sorted) {
    if (ev.type === 'Deposit') {
      if (depositCluster) {
        const prev = depositCluster.events[depositCluster.events.length - 1];
        const gapDays = (new Date(ev.date).getTime() - new Date(prev.date).getTime()) / 86_400_000;
        if (gapDays <= 21) {
          depositCluster.events.push(ev);
          depositCluster.amount += ev.amount;
          continue;
        }
        flushDeposit();
      }
      depositCluster = { events: [ev], amount: ev.amount };
      continue;
    }
    // any non-deposit closes a deposit cluster
    flushDeposit();

    if (ev.type === 'Buy' && ev.symbol) {
      const lot = costBasis.get(ev.symbol) ?? { qty: 0, cost: 0 };
      // amount is negative for buys; cost added = -amount
      lot.qty += ev.quantity;
      lot.cost += -ev.amount;
      costBasis.set(ev.symbol, lot);
      continue;
    }
    if (ev.type === 'Sell' && ev.symbol && ev.quantity > 0) {
      const lot = costBasis.get(ev.symbol) ?? { qty: 0, cost: 0 };
      const avg = lot.qty > 0 ? lot.cost / lot.qty : 0;
      const costRemoved = avg * ev.quantity;
      lot.qty = Math.max(0, lot.qty - ev.quantity);
      lot.cost = Math.max(0, lot.cost - costRemoved);
      costBasis.set(ev.symbol, lot);
      const proceeds = ev.amount; // positive
      const pnl = proceeds - costRemoved;
      const pct = costRemoved > 0 ? (pnl / costRemoved) * 100 : 0;
      const kind: FlowKind = pnl < 0 ? 'loss' : pct >= 200 ? 'big-gain' : 'sell-profit';
      const titleMain = kind === 'big-gain' ? `${ev.symbol} ×${(1 + pct / 100).toFixed(1).replace(/\.0$/, '')}` : `${ev.symbol} ${pnl >= 0 ? '+' : ''}${pct.toFixed(0)}%`;
      nodes.push({
        id: `sell-${ev.date}-${ev.symbol}-${nodes.length}`,
        date: ev.date,
        label: fmtMonth(ev.date),
        title: titleMain,
        subtitle: kind === 'loss' ? `${pct.toFixed(0)}%` : `${fmtCurrency(proceeds, 'USD')} proceeds`,
        kind,
        amount: proceeds,
        realizedPnL: pnl,
        realizedPct: pct,
        symbol: ev.symbol,
        events: [ev],
        reinvested: []
      });
      continue;
    }
    // Dividend / Fee / Split / SpinOff / Withdrawal: ignored as flow source.
  }
  flushDeposit();

  // Second pass: bucket Buys between consecutive flow nodes as "reinvested into".
  if (nodes.length === 0) return nodes;
  const buys = sorted.filter((e) => e.type === 'Buy' && e.symbol);
  for (let i = 0; i < nodes.length; i++) {
    const start = nodes[i].date;
    const end = i + 1 < nodes.length ? nodes[i + 1].date : '9999-12-31';
    for (const b of buys) {
      if (b.date >= start && b.date < end) {
        nodes[i].reinvested.push({
          symbol: b.symbol!,
          quantity: b.quantity,
          amount: -b.amount,
          date: b.date
        });
      }
    }
  }
  return nodes;
}

function FlowCard({ node, currency, above }: { node: FlowNode; currency: string; above: boolean }) {
  const [opened, setOpened] = useState(false);
  const color = KIND_COLOR[node.kind];
  return (
    <Popover opened={opened} onChange={setOpened} position={above ? 'top' : 'bottom'} withArrow shadow="md" width={360}>
      <Popover.Target>
        <UnstyledButton
          onClick={() => setOpened((o) => !o)}
          style={{
            background: color,
            color: 'white',
            borderRadius: 6,
            padding: '10px 12px',
            minWidth: 120,
            maxWidth: 160,
            textAlign: 'center',
            boxShadow: '0 2px 6px rgba(0,0,0,0.15)'
          }}
        >
          <Text size="xs" fw={600} opacity={0.85}>{node.label}</Text>
          <Text size="sm" fw={700} mt={2}>{node.title}</Text>
          <Text size="xs" mt={2} opacity={0.9}>{node.subtitle}</Text>
          {node.reinvested.length > 0 && (
            <Text size="xs" mt={4} opacity={0.85}>
              → {node.reinvested.slice(0, 2).map((r) => r.symbol).join('+')}
              {node.reinvested.length > 2 ? `+${node.reinvested.length - 2}` : ''}
            </Text>
          )}
        </UnstyledButton>
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap={6}>
          <Group justify="space-between" wrap="nowrap">
            <Text fw={700}>{node.title}</Text>
            <Badge color={node.kind === 'deposit' ? 'teal' : node.kind === 'big-gain' ? 'orange' : node.kind === 'loss' ? 'red' : 'green'}>
              {KIND_LABEL[node.kind]}
            </Badge>
          </Group>
          <Text size="xs" c="dimmed">{node.date}</Text>

          {node.kind === 'deposit' ? (
            <Stack gap={2}>
              <Text size="sm">Total deposited: <b>{fmtCurrency(node.amount, currency)}</b></Text>
              {node.events.length > 1 && (
                <Text size="xs" c="dimmed">{node.events.length} deposits clustered</Text>
              )}
              <Table withRowBorders={false} highlightOnHover mt={4}>
                <Table.Tbody>
                  {node.events.map((e, i) => (
                    <Table.Tr key={i}>
                      <Table.Td><Text size="xs">{e.date}</Text></Table.Td>
                      <Table.Td ta="right"><Text size="xs">{fmtCurrency(e.amount, currency)}</Text></Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Stack>
          ) : (
            <Stack gap={2}>
              <Text size="sm">Symbol: <b>{node.symbol}</b></Text>
              <Text size="sm">Proceeds: <b>{fmtCurrency(node.amount, currency)}</b></Text>
              {node.realizedPnL !== undefined && (
                <Text size="sm" c={node.realizedPnL >= 0 ? 'teal' : 'red'}>
                  Realized P&L: <b>{fmtCurrency(node.realizedPnL, currency)}</b>
                  {node.realizedPct !== undefined && <> ({node.realizedPct >= 0 ? '+' : ''}{node.realizedPct.toFixed(1)}%)</>}
                </Text>
              )}
            </Stack>
          )}

          {node.reinvested.length > 0 && (
            <>
              <Text size="xs" c="dimmed" mt={4}>Reinvested into ({node.reinvested.length}):</Text>
              <ScrollArea.Autosize mah={180}>
                <Table withRowBorders={false}>
                  <Table.Tbody>
                    {node.reinvested.map((r, i) => (
                      <Table.Tr key={i}>
                        <Table.Td><Text size="xs">{r.date}</Text></Table.Td>
                        <Table.Td><Text size="xs" fw={600}>{r.symbol}</Text></Table.Td>
                        <Table.Td ta="right"><Text size="xs">{r.quantity.toFixed(2)} sh</Text></Table.Td>
                        <Table.Td ta="right"><Text size="xs">{fmtCurrency(r.amount, currency)}</Text></Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </ScrollArea.Autosize>
            </>
          )}
        </Stack>
      </Popover.Dropdown>
    </Popover>
  );
}

function PositionChip({
  symbol, qty, amount, currency
}: { symbol: string; qty: number; amount: number; currency: string }) {
  const [opened, setOpened] = useState(false);
  return (
    <Popover opened={opened} onChange={setOpened} position="top" withArrow shadow="md" width={240}>
      <Popover.Target>
        <UnstyledButton
          onClick={() => setOpened((o) => !o)}
          style={{
            border: '1px solid var(--mantine-color-default-border)',
            borderRadius: 4,
            padding: '8px 14px',
            minWidth: 96,
            textAlign: 'center',
            background: 'var(--mantine-color-body)'
          }}
        >
          <Text size="sm" fw={700}>{symbol}</Text>
        </UnstyledButton>
      </Popover.Target>
      <Popover.Dropdown>
        <Stack gap={2}>
          <Text fw={700}>{symbol}</Text>
          <Text size="xs">Shares bought: <b>{qty.toFixed(2)}</b></Text>
          <Text size="xs">Cash deployed: <b>{fmtCurrency(amount, currency)}</b></Text>
        </Stack>
      </Popover.Dropdown>
    </Popover>
  );
}

export function CapitalFlow() {
  const { id = '' } = useParams<{ id: string }>();
  const { data, isLoading } = usePortfolioHistory(id);

  const nodes = useMemo(() => buildFlow(data?.events ?? []), [data]);

  if (isLoading) return <Loader />;
  if (!data) return <EmptyState title="No portfolio" description="Could not load history." />;
  if (nodes.length === 0) {
    return (
      <Stack gap="md">
        <Group>
          <ActionIcon variant="default" component={Link} to={`/portfolios/${id}`}><IconArrowLeft size={16} /></ActionIcon>
          <Title order={2}>Capital flow</Title>
        </Group>
        <EmptyState title="No flow yet" description="Make a deposit or trade to see capital flow." />
      </Stack>
    );
  }

  return (
    <Stack gap="lg">
      <Group justify="space-between">
        <Group>
          <ActionIcon variant="default" component={Link} to={`/portfolios/${id}`}><IconArrowLeft size={16} /></ActionIcon>
          <Title order={2}>Capital flow timeline</Title>
        </Group>
        <Text size="sm" c="dimmed">Every deposit & reinvestment · click any card for details</Text>
      </Group>

      {/* Timeline */}
      <Card withBorder>
        <ScrollArea type="auto" offsetScrollbars>
          <Box style={{ minWidth: Math.max(900, nodes.length * 160), padding: '8px 8px 16px' }}>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: `repeat(${nodes.length}, 1fr)`,
                rowGap: 0,
                alignItems: 'stretch'
              }}
            >
              {/* Top row: even-indexed nodes */}
              {nodes.map((n, i) => (
                <div key={`top-${n.id}`} style={{ display: 'flex', justifyContent: 'center', minHeight: 100, alignItems: 'flex-end' }}>
                  {i % 2 === 0 ? <FlowCard node={n} currency={data.currency} above /> : null}
                </div>
              ))}
              {/* Connectors above */}
              {nodes.map((n, i) => (
                <div key={`stemTop-${n.id}`} style={{ display: 'flex', justifyContent: 'center', height: 18 }}>
                  {i % 2 === 0 ? <div style={{ width: 2, background: KIND_COLOR[n.kind] }} /> : null}
                </div>
              ))}
              {/* Axis with dots */}
              <div style={{ gridColumn: `1 / span ${nodes.length}`, position: 'relative', height: 16 }}>
                <div style={{
                  position: 'absolute', top: 7, left: 0, right: 0, height: 2,
                  background: 'var(--mantine-color-default-border)'
                }} />
                <div style={{
                  position: 'absolute', top: 0, left: 0, right: 0, bottom: 0,
                  display: 'grid', gridTemplateColumns: `repeat(${nodes.length}, 1fr)`
                }}>
                  {nodes.map((n) => (
                    <div key={`dot-${n.id}`} style={{ display: 'flex', justifyContent: 'center', alignItems: 'center' }}>
                      <div style={{ width: 14, height: 14, borderRadius: '50%', background: KIND_COLOR[n.kind], border: '2px solid white', boxShadow: '0 0 0 1px ' + KIND_COLOR[n.kind] }} />
                    </div>
                  ))}
                </div>
              </div>
              {/* Connectors below */}
              {nodes.map((n, i) => (
                <div key={`stemBot-${n.id}`} style={{ display: 'flex', justifyContent: 'center', height: 18 }}>
                  {i % 2 === 1 ? <div style={{ width: 2, background: KIND_COLOR[n.kind] }} /> : null}
                </div>
              ))}
              {/* Bottom row: odd-indexed nodes */}
              {nodes.map((n, i) => (
                <div key={`bot-${n.id}`} style={{ display: 'flex', justifyContent: 'center', minHeight: 100, alignItems: 'flex-start' }}>
                  {i % 2 === 1 ? <FlowCard node={n} currency={data.currency} above={false} /> : null}
                </div>
              ))}
            </div>
          </Box>
        </ScrollArea>

        <Group mt="md" gap="lg">
          {(Object.keys(KIND_LABEL) as FlowKind[]).map((k) => (
            <Group key={k} gap={6}>
              <div style={{ width: 14, height: 14, background: KIND_COLOR[k], borderRadius: 2 }} />
              <Text size="xs">{KIND_LABEL[k]}</Text>
            </Group>
          ))}
        </Group>
      </Card>

      {/* Where each dollar went */}
      <Title order={3}>Where each dollar went · source → positions opened</Title>
      <Card withBorder>
        <Stack gap={0}>
          {nodes.filter((n) => n.reinvested.length > 0).map((n, idx) => (
            <Group
              key={`row-${n.id}`}
              wrap="nowrap"
              align="center"
              gap="md"
              style={{
                padding: '12px 8px',
                borderTop: idx === 0 ? 'none' : '1px solid var(--mantine-color-default-border)'
              }}
            >
              <div
                style={{
                  background: KIND_COLOR[n.kind],
                  color: 'white',
                  borderRadius: 6,
                  padding: '8px 10px',
                  minWidth: 140,
                  textAlign: 'center'
                }}
              >
                <Text size="xs" fw={600} opacity={0.85}>{n.label}</Text>
                <Text size="sm" fw={700}>{n.kind === 'deposit' ? 'Deposit' : n.title}</Text>
                <Text size="xs" opacity={0.9}>{fmtCurrency(n.amount, data.currency)}</Text>
              </div>
              <IconArrowRight size={18} color={KIND_COLOR[n.kind]} />
              <ScrollArea style={{ flex: 1 }} type="auto" offsetScrollbars>
                <Group wrap="nowrap" gap="sm">
                  {n.reinvested.map((r, i) => (
                    <PositionChip key={i} symbol={r.symbol} qty={r.quantity} amount={r.amount} currency={data.currency} />
                  ))}
                </Group>
              </ScrollArea>
            </Group>
          ))}
        </Stack>
      </Card>
    </Stack>
  );
}
