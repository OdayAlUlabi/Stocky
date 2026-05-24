import { ActionIcon, Alert, Anchor, Badge, Button, Card, FileButton, Group, Modal, ScrollArea, Select, Stack, Table, Tabs, Text, Textarea, Title, Tooltip } from '@mantine/core';
import { IconArrowLeft, IconDownload, IconEdit, IconPlus, IconTrash, IconUpload } from '@tabler/icons-react';
import { Link, useParams } from 'react-router-dom';
import { useState } from 'react';
import { notifications } from '@mantine/notifications';
import dayjs from 'dayjs';
import {
  useDeleteTransaction,
  useHoldings,
  useImportTransactions,
  usePortfolios,
  useTransactions,
  useUpdatePortfolio
} from '../../api/hooks';
import { formatApiError } from '../../api/client';
import { EmptyState } from '../../components/EmptyState';
import { TradeDrawer } from './TradeDrawer';
import type { CostBasisMethod, TransactionDto } from '../../api/types';

const COST_BASIS_OPTIONS: { value: CostBasisMethod; label: string }[] = [
  { value: 'Fifo', label: 'FIFO — first in, first out' },
  { value: 'Lifo', label: 'LIFO — last in, first out' },
  { value: 'HighestCost', label: 'Highest cost (HIFO)' },
  { value: 'LowestCost', label: 'Lowest cost' },
];

function txTypeColor(type: string): string {
  switch (type) {
    case 'Buy': return 'teal';
    case 'Sell': return 'red';
    case 'Dividend': return 'green';
    case 'Deposit': return 'blue';
    case 'Withdrawal': return 'orange';
    case 'Fee': return 'gray';
    case 'Split': return 'violet';
    case 'SpinOff': return 'grape';
    default: return 'gray';
  }
}

export function PortfolioDetail() {
  const { id = '' } = useParams<{ id: string }>();
  const portfolios = usePortfolios();
  const portfolio = portfolios.data?.find((p) => p.id === id);
  const holdings = useHoldings(id);
  const transactions = useTransactions(id);
  const delTx = useDeleteTransaction(id);
  const importTx = useImportTransactions(id);
  const updatePortfolio = useUpdatePortfolio();

  const [drawerOpen, setDrawerOpen] = useState(false);
  const [editing, setEditing] = useState<TransactionDto | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  const [importCsv, setImportCsv] = useState('');

  const currency = portfolio?.baseCurrency ?? 'USD';

  const openNew = () => { setEditing(null); setDrawerOpen(true); };
  const openEdit = (tx: TransactionDto) => { setEditing(tx); setDrawerOpen(true); };

  const remove = async (tx: TransactionDto) => {
    if (!confirm(`Delete ${tx.type} ${tx.symbol ?? ''}?`)) return;
    try {
      await delTx.mutateAsync(tx.id);
      notifications.show({ message: 'Transaction deleted', color: 'teal' });
    } catch (e) {
      notifications.show({ message: formatApiError(e), color: 'red' });
    }
  };

  const fmt = (n: number | null | undefined) =>
    n == null ? '—' : new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(n);

  const totalValue = (holdings.data ?? []).reduce(
    (s, h) => s + (h.marketValue ?? h.quantity * h.averageCost),
    0
  );

  const exportCsv = () => {
    const rows = holdings.data ?? [];
    if (rows.length === 0) return;
    const header = ['Symbol', 'Quantity', 'AvgCost', 'LastPrice', 'MarketValue', 'CostBasis', 'UnrealizedPnL', 'WeightPct'];
    const lines = [header.join(',')];
    rows.forEach((h) => {
      const cost = h.quantity * h.averageCost;
      const value = h.marketValue ?? cost;
      const pnl = value - cost;
      const weight = totalValue > 0 ? (value / totalValue) * 100 : 0;
      lines.push([
        h.symbol,
        h.quantity,
        h.averageCost,
        h.latestPrice ?? '',
        value,
        cost,
        pnl,
        weight.toFixed(2)
      ].join(','));
    });
    const blob = new Blob([lines.join('\n')], { type: 'text/csv' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${(portfolio?.name ?? 'portfolio').replace(/\s+/g, '_')}_positions.csv`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <Stack gap="lg">
      <Group justify="space-between" wrap="wrap">
        <Group>
          <ActionIcon component={Link} to="/portfolios" variant="subtle" aria-label="Back">
            <IconArrowLeft size={18} />
          </ActionIcon>
          <Stack gap={0}>
            <Title order={2}>{portfolio?.name ?? 'Portfolio'}</Title>
            <Text c="dimmed" size="sm">Base currency {currency}</Text>
          </Stack>
        </Group>
        <Group gap="xs">
          <Tooltip label="Lot-selection method used when realising gains on Sell trades.">
            <Select
              size="xs"
              w={240}
              value={portfolio?.costBasisMethod ?? 'Fifo'}
              data={COST_BASIS_OPTIONS}
              onChange={async (val) => {
                if (!val || !portfolio || val === portfolio.costBasisMethod) return;
                try {
                  await updatePortfolio.mutateAsync({
                    id,
                    body: {
                      name: portfolio.name,
                      baseCurrency: portfolio.baseCurrency,
                      costBasisMethod: val,
                    },
                  });
                  notifications.show({ message: `Cost basis: ${val} — gains recomputed`, color: 'teal' });
                } catch (e) {
                  notifications.show({ message: formatApiError(e), color: 'red' });
                }
              }}
              disabled={!portfolio || updatePortfolio.isPending}
              aria-label="Cost basis method"
            />
          </Tooltip>
          <Button variant="default" component={Link} to={`/portfolios/${id}/history`}>History</Button>
          <Button variant="default" component={Link} to={`/portfolios/${id}/capital-flow`}>Capital flow</Button>
          <Button variant="default" component={Link} to={`/portfolios/${id}/analytics`}>Analytics</Button>
          <Button variant="default" leftSection={<IconDownload size={16} />} onClick={exportCsv} disabled={!holdings.data || holdings.data.length === 0}>Export CSV</Button>
          <Button variant="default" leftSection={<IconUpload size={16} />} onClick={() => { setImportCsv(''); setImportOpen(true); }}>Import CSV</Button>
          <Button leftSection={<IconPlus size={16} />} onClick={openNew}>Add trade</Button>
        </Group>
      </Group>

      {portfolio && (
        <Group gap="md" wrap="wrap">
          <Card withBorder radius="md" padding="md" miw={180}>
            <Text size="xs" c="dimmed" tt="uppercase">Market value</Text>
            <Text fw={600} size="xl">{fmt(totalValue)}</Text>
          </Card>
          <Card withBorder radius="md" padding="md" miw={180}>
            <Text size="xs" c="dimmed" tt="uppercase">Cash</Text>
            <Text fw={600} size="xl" c={portfolio.cashBalance < 0 ? 'red' : undefined}>{fmt(portfolio.cashBalance)}</Text>
          </Card>
          <Card withBorder radius="md" padding="md" miw={180}>
            <Text size="xs" c="dimmed" tt="uppercase">Total equity</Text>
            <Text fw={600} size="xl">{fmt(totalValue + portfolio.cashBalance)}</Text>
          </Card>
        </Group>
      )}

      <Tabs defaultValue="positions">
        <Tabs.List>
          <Tabs.Tab value="positions">Positions</Tabs.Tab>
          <Tabs.Tab value="transactions">Transactions</Tabs.Tab>
        </Tabs.List>

        <Tabs.Panel value="positions" pt="md">
          {holdings.isLoading ? (
            <Text c="dimmed">Loading...</Text>
          ) : !holdings.data || holdings.data.length === 0 ? (
            <EmptyState title="No positions" description="Log a Buy trade to open your first position." actionLabel="Add trade" onAction={openNew} />
          ) : (
            <Card withBorder radius="md" padding="0">
              <ScrollArea>
                <Table striped highlightOnHover>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Symbol</Table.Th>
                      <Table.Th ta="right">Quantity</Table.Th>
                      <Table.Th ta="right">Avg cost</Table.Th>
                      <Table.Th ta="right">Cost basis</Table.Th>
                      <Table.Th ta="right">Last</Table.Th>
                      <Table.Th ta="right">Market value</Table.Th>
                      <Table.Th ta="right">Weight %</Table.Th>
                      <Table.Th ta="right">Unrealized</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {holdings.data.map((h) => {
                      const cost = h.quantity * h.averageCost;
                      const value = h.marketValue ?? null;
                      const pnl = value == null ? null : value - cost;
                      const weight = value != null && totalValue > 0 ? (value / totalValue) * 100 : null;
                      return (
                        <Table.Tr key={h.id}>
                          <Table.Td>
                            <Anchor component={Link} to={`/portfolios/${id}/positions/${encodeURIComponent(h.symbol)}`} fw={500}>
                              {h.symbol}
                            </Anchor>
                          </Table.Td>
                          <Table.Td ta="right">{h.quantity}</Table.Td>
                          <Table.Td ta="right">{fmt(h.averageCost)}</Table.Td>
                          <Table.Td ta="right">{fmt(cost)}</Table.Td>
                          <Table.Td ta="right">{fmt(h.latestPrice)}</Table.Td>
                          <Table.Td ta="right">{fmt(value)}</Table.Td>
                          <Table.Td ta="right">{weight == null ? '—' : `${weight.toFixed(2)}%`}</Table.Td>
                          <Table.Td ta="right" c={pnl == null ? undefined : pnl >= 0 ? 'teal' : 'red'}>{fmt(pnl)}</Table.Td>
                        </Table.Tr>
                      );
                    })}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            </Card>
          )}
        </Tabs.Panel>

        <Tabs.Panel value="transactions" pt="md">
          {transactions.isLoading ? (
            <Text c="dimmed">Loading...</Text>
          ) : !transactions.data || transactions.data.length === 0 ? (
            <EmptyState title="No transactions" description="Add a trade to start building history." actionLabel="Add trade" onAction={openNew} />
          ) : (
            <Card withBorder radius="md" padding="0">
              <ScrollArea>
                <Table striped highlightOnHover>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Date</Table.Th>
                      <Table.Th>Type</Table.Th>
                      <Table.Th>Symbol</Table.Th>
                      <Table.Th ta="right">Qty</Table.Th>
                      <Table.Th ta="right">Price</Table.Th>
                      <Table.Th ta="right">Fee</Table.Th>
                      <Table.Th ta="right">Total</Table.Th>
                      <Table.Th ta="right" />
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {transactions.data.map((t) => (
                      <Table.Tr key={t.id}>
                        <Table.Td>{dayjs(t.executedAt).format('MMM D, YYYY')}</Table.Td>
                        <Table.Td><Badge color={txTypeColor(t.type)} variant="light">{t.type}</Badge></Table.Td>
                        <Table.Td>{t.symbol ?? '—'}</Table.Td>
                        <Table.Td ta="right">{t.quantity}</Table.Td>
                        <Table.Td ta="right">{fmt(t.price)}</Table.Td>
                        <Table.Td ta="right">{fmt(t.fee)}</Table.Td>
                        <Table.Td ta="right">{fmt(t.quantity * t.price + t.fee)}</Table.Td>
                        <Table.Td ta="right">
                          <Group gap={4} justify="flex-end" wrap="nowrap">
                            <Tooltip label="Edit"><ActionIcon variant="subtle" onClick={() => openEdit(t)}><IconEdit size={16} /></ActionIcon></Tooltip>
                            <Tooltip label="Delete"><ActionIcon variant="subtle" color="red" onClick={() => remove(t)}><IconTrash size={16} /></ActionIcon></Tooltip>
                          </Group>
                        </Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </ScrollArea>
            </Card>
          )}
        </Tabs.Panel>
      </Tabs>

      <TradeDrawer
        portfolioId={id}
        opened={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        editing={editing}
        defaultCurrency={currency}
      />

      <Modal opened={importOpen} onClose={() => setImportOpen(false)} title="Import transactions from CSV" size="lg">
        <Stack gap="sm">
          <Text size="sm" c="dimmed">
            Expected columns: <code>type,symbol,quantity,price,fee,currency,executedAt,notes</code>.
            Header row optional. Supported types: Buy, Sell, Deposit, Withdrawal, Dividend, Fee, Split, SpinOff.
          </Text>
          <Group gap="xs">
            <FileButton
              accept="text/csv,.csv"
              onChange={async (file) => {
                if (!file) return;
                const text = await file.text();
                setImportCsv(text);
              }}
            >
              {(props) => <Button {...props} variant="default" leftSection={<IconUpload size={14} />}>Load file…</Button>}
            </FileButton>
            <Anchor
              size="sm"
              onClick={() => setImportCsv('type,symbol,quantity,price,fee,currency,executedAt,notes\nDeposit,,1000,1,0,USD,2024-01-02,Initial funding\nBuy,AAPL,10,180.50,0,USD,2024-01-05,\nSell,AAPL,4,195.00,0,USD,2024-03-15,Partial trim')}
            >
              Insert sample
            </Anchor>
          </Group>
          <Textarea
            value={importCsv}
            onChange={(e) => setImportCsv(e.currentTarget.value)}
            placeholder="Paste CSV here or load a file…"
            minRows={10}
            autosize
            maxRows={20}
            styles={{ input: { fontFamily: 'monospace', fontSize: 12 } }}
          />
          {importTx.data && (
            <Alert color={importTx.data.skipped > 0 ? 'yellow' : 'teal'}>
              Imported {importTx.data.imported}, skipped {importTx.data.skipped}.
              {importTx.data.errors.length > 0 && (
                <ScrollArea.Autosize mah={160} mt="xs">
                  <Stack gap={2}>
                    {importTx.data.errors.map((e, i) => (
                      <Text key={i} size="xs" c="red">Row {e.row}: {e.message}</Text>
                    ))}
                  </Stack>
                </ScrollArea.Autosize>
              )}
            </Alert>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setImportOpen(false)}>Close</Button>
            <Button
              loading={importTx.isPending}
              disabled={!importCsv.trim()}
              onClick={async () => {
                try {
                  const result = await importTx.mutateAsync(importCsv);
                  notifications.show({
                    message: `Imported ${result.imported} transaction${result.imported === 1 ? '' : 's'}` + (result.skipped > 0 ? `, ${result.skipped} skipped` : ''),
                    color: result.skipped > 0 ? 'yellow' : 'teal'
                  });
                } catch (e) {
                  notifications.show({ message: formatApiError(e), color: 'red' });
                }
              }}
            >
              Import
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
