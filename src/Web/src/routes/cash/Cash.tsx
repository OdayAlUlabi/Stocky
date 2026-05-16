import { useMemo, useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Modal, NumberInput, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { IconCash, IconPlus, IconTrash } from '@tabler/icons-react';
import { usePortfolios, useCashTransactions, useCashBalances, useCreateCashTransaction, useDeleteCashTransaction } from '../../api/hooks';

export function Cash() {
  const { data: portfolios } = usePortfolios();
  const [portfolioId, setPortfolioId] = useState<string | null>(null);
  const effectiveId = portfolioId ?? portfolios?.[0]?.id;

  const { data: txs } = useCashTransactions(effectiveId);
  const { data: balances } = useCashBalances(effectiveId);
  const create = useCreateCashTransaction();
  const del = useDeleteCashTransaction(effectiveId);

  const [modalOpen, setModalOpen] = useState(false);
  const [type, setType] = useState<string | null>('Deposit');
  const [amount, setAmount] = useState<number | ''>('');
  const [currency, setCurrency] = useState('USD');
  const [notes, setNotes] = useState('');

  const portfolioOptions = useMemo(
    () => (portfolios ?? []).map((p) => ({ value: p.id, label: p.name })),
    [portfolios]
  );

  const submit = async () => {
    if (!effectiveId || typeof amount !== 'number' || amount <= 0 || !type) return;
    await create.mutateAsync({ portfolioId: effectiveId, type, amount, currency, notes: notes || null });
    setModalOpen(false);
    setAmount('');
    setNotes('');
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}><IconCash size={20} /> Cash management</Title>
        <Group>
          <Select
            data={portfolioOptions}
            value={effectiveId ?? null}
            onChange={setPortfolioId}
            placeholder="Portfolio"
            w={240}
          />
          <Button leftSection={<IconPlus size={16} />} onClick={() => setModalOpen(true)} disabled={!effectiveId}>
            Add cash transaction
          </Button>
        </Group>
      </Group>

      <Group>
        {(balances ?? []).map((b) => (
          <Card key={b.currency} withBorder padding="md">
            <Text size="xs" c="dimmed">{b.currency} balance</Text>
            <Text size="xl" fw={600}>{b.balance.toLocaleString(undefined, { style: 'currency', currency: b.currency })}</Text>
            <Text size="xs" c="dimmed">{b.count} transaction{b.count === 1 ? '' : 's'}</Text>
          </Card>
        ))}
        {balances && balances.length === 0 && <Text c="dimmed">No cash activity yet.</Text>}
      </Group>

      <Card withBorder padding="md">
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Date</Table.Th>
              <Table.Th>Type</Table.Th>
              <Table.Th ta="right">Amount</Table.Th>
              <Table.Th>Currency</Table.Th>
              <Table.Th>Notes</Table.Th>
              <Table.Th />
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(txs ?? []).map((t) => (
              <Table.Tr key={t.id}>
                <Table.Td>{new Date(t.executedAt).toLocaleDateString()}</Table.Td>
                <Table.Td><Badge variant="light">{t.type}</Badge></Table.Td>
                <Table.Td ta="right" c={t.amount < 0 ? 'red' : 'teal'}>
                  {t.amount.toLocaleString(undefined, { style: 'currency', currency: t.currency })}
                </Table.Td>
                <Table.Td>{t.currency}</Table.Td>
                <Table.Td>{t.notes}</Table.Td>
                <Table.Td>
                  <ActionIcon variant="subtle" color="red" onClick={() => del.mutate(t.id)}>
                    <IconTrash size={16} />
                  </ActionIcon>
                </Table.Td>
              </Table.Tr>
            ))}
            {txs && txs.length === 0 && (
              <Table.Tr><Table.Td colSpan={6}><Text ta="center" c="dimmed">No cash transactions.</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Card>

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="Add cash transaction">
        <Stack>
          <Select
            label="Type"
            data={['Deposit', 'Withdrawal', 'Fee', 'Dividend']}
            value={type}
            onChange={setType}
          />
          <NumberInput
            label="Amount"
            min={0}
            decimalScale={2}
            value={amount}
            onChange={(v) => setAmount(typeof v === 'number' ? v : '')}
          />
          <TextInput label="Currency" value={currency} onChange={(e) => setCurrency(e.currentTarget.value.toUpperCase())} maxLength={3} />
          <TextInput label="Notes" value={notes} onChange={(e) => setNotes(e.currentTarget.value)} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button onClick={submit} loading={create.isPending}>Save</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
