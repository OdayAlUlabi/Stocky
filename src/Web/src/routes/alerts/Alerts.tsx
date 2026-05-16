import { ActionIcon, Badge, Button, Card, Group, Loader, Modal, NumberInput, Select, Stack, Table, TextInput, Title, Text } from '@mantine/core';
import { useState } from 'react';
import { IconTrash, IconPlus } from '@tabler/icons-react';
import { useAlerts, useCreateAlert, useDeleteAlert, useUpdateAlert } from '../../api/hooks';
import type { AlertCondition } from '../../api/types';

const conditions: { value: AlertCondition; label: string }[] = [
  { value: 'PriceAbove', label: 'Price above' },
  { value: 'PriceBelow', label: 'Price below' },
  { value: 'DayChangePercentAbove', label: 'Day change % above' },
  { value: 'DayChangePercentBelow', label: 'Day change % below' }
];

export function Alerts() {
  const { data, isLoading } = useAlerts();
  const create = useCreateAlert();
  const del = useDeleteAlert();
  const update = useUpdateAlert();
  const [open, setOpen] = useState(false);
  const [symbol, setSymbol] = useState('');
  const [condition, setCondition] = useState<AlertCondition>('PriceAbove');
  const [threshold, setThreshold] = useState<number | string>(0);
  const [note, setNote] = useState('');

  const submit = async () => {
    if (!symbol) return;
    await create.mutateAsync({ symbol, condition, threshold: Number(threshold), note: note || null });
    setOpen(false); setSymbol(''); setThreshold(0); setNote('');
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Alerts</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={() => setOpen(true)}>New alert</Button>
      </Group>
      <Card withBorder>
        {isLoading ? <Loader /> : !data || data.length === 0 ? <Text c="dimmed">No alerts yet.</Text> : (
          <Table striped>
            <Table.Thead><Table.Tr>
              <Table.Th>Symbol</Table.Th><Table.Th>Condition</Table.Th><Table.Th>Threshold</Table.Th>
              <Table.Th>Status</Table.Th><Table.Th>Triggered</Table.Th><Table.Th></Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {data.map(a => (
                <Table.Tr key={a.id}>
                  <Table.Td>{a.symbol}</Table.Td>
                  <Table.Td>{a.condition}</Table.Td>
                  <Table.Td>{a.threshold}</Table.Td>
                  <Table.Td>
                    <Badge color={a.status === 'Triggered' ? 'red' : a.status === 'Active' ? 'teal' : 'gray'}>
                      {a.status}
                    </Badge>
                  </Table.Td>
                  <Table.Td>{a.triggeredAt ? `${a.triggeredAt.slice(0, 10)} @ ${a.triggeredValue}` : '—'}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      {a.status !== 'Active' && (
                        <Button size="xs" variant="light" onClick={() => update.mutate({ id: a.id, body: { threshold: a.threshold, status: 'Active', note: a.note } })}>Reactivate</Button>
                      )}
                      <ActionIcon color="red" variant="subtle" onClick={() => del.mutate(a.id)}><IconTrash size={16} /></ActionIcon>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal opened={open} onClose={() => setOpen(false)} title="Create alert">
        <Stack>
          <TextInput label="Symbol" value={symbol} onChange={(e) => setSymbol(e.currentTarget.value.toUpperCase())} placeholder="AAPL" />
          <Select label="Condition" value={condition} onChange={(v) => setCondition((v as AlertCondition) ?? 'PriceAbove')} data={conditions} />
          <NumberInput label="Threshold" value={threshold} onChange={setThreshold} decimalScale={4} />
          <TextInput label="Note (optional)" value={note} onChange={(e) => setNote(e.currentTarget.value)} />
          <Group justify="flex-end"><Button onClick={submit} loading={create.isPending}>Create</Button></Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
