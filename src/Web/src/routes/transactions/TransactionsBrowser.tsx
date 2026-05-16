import { Alert, Button, Card, FileButton, Group, Loader, NumberFormatter, Select, Stack, Table, Text, Title } from '@mantine/core';
import { useState } from 'react';
import { useImportTransactions, usePortfolios, useTransactions } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';

export function TransactionsBrowser() {
  const { data: portfolios } = usePortfolios();
  const [portfolioId, setPortfolioId] = useState<string | null>(null);
  const effectiveId = portfolioId ?? portfolios?.[0]?.id;
  const { data: txs, isLoading } = useTransactions(effectiveId);
  const importMut = useImportTransactions(effectiveId ?? '');
  const [importResult, setImportResult] = useState<string | null>(null);

  return (
    <Stack>
      <Title order={3}>Transactions</Title>
      <Group>
        <Select
          placeholder="Portfolio"
          value={effectiveId}
          onChange={setPortfolioId}
          data={portfolios?.map(p => ({ value: p.id, label: p.name })) ?? []}
          searchable
        />
        <FileButton
          accept=".csv,text/csv"
          disabled={!effectiveId || importMut.isPending}
          onChange={async (file) => {
            if (!file || !effectiveId) return;
            setImportResult(null);
            try {
              const r = await importMut.mutateAsync(file);
              setImportResult(`Imported ${r.imported}, skipped ${r.skipped}.${r.errors.length ? ' First error: ' + r.errors[0] : ''}`);
            } catch (e) {
              setImportResult(`Import failed: ${(e as Error).message}`);
            }
          }}
        >
          {(props) => <Button {...props} variant="light">Import CSV</Button>}
        </FileButton>
      </Group>
      {importResult && <Alert color={importResult.startsWith('Import failed') ? 'red' : 'teal'}>{importResult}</Alert>}
      <Card withBorder>
        {isLoading ? <Loader /> : !txs || txs.length === 0 ? (
          <EmptyState title="No transactions" description="Buy or sell something to populate this view, or import a CSV file with headers: symbol,type,quantity,price,fee,currency,executedAt,notes" />
        ) : (
          <Table striped>
            <Table.Thead><Table.Tr>
              <Table.Th>Date</Table.Th><Table.Th>Symbol</Table.Th><Table.Th>Type</Table.Th>
              <Table.Th>Qty</Table.Th><Table.Th>Price</Table.Th><Table.Th>Fee</Table.Th><Table.Th>Currency</Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {txs.map(t => (
                <Table.Tr key={t.id}>
                  <Table.Td>{t.executedAt.slice(0, 10)}</Table.Td>
                  <Table.Td>{t.symbol ?? '—'}</Table.Td>
                  <Table.Td>{t.type}</Table.Td>
                  <Table.Td>{t.quantity}</Table.Td>
                  <Table.Td><NumberFormatter value={t.price} thousandSeparator decimalScale={2} /></Table.Td>
                  <Table.Td><NumberFormatter value={t.fee} thousandSeparator decimalScale={2} /></Table.Td>
                  <Table.Td>{t.currency}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>
      <Text size="xs" c="dimmed">CSV columns: <code>symbol,type,quantity,price,fee,currency,executedAt,notes</code>. Type ∈ Buy, Sell, Dividend, Deposit, Withdrawal, Split.</Text>
    </Stack>
  );
}
