import { ActionIcon, Button, Card, Group, Modal, Select, Stack, Table, Text, TextInput, Title, Tooltip } from '@mantine/core';
import { IconPlus, IconStar, IconTrash } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { notifications } from '@mantine/notifications';
import { useAddWatchlistItem, useCreateWatchlist, useRemoveWatchlistItem, useWatchlists } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';
import { TickerSearch } from '../../components/TickerSearch';

export function WatchlistView() {
  const { data, isLoading } = useWatchlists();
  const createWl = useCreateWatchlist();
  const [activeId, setActiveId] = useState<string | null>(null);
  const active = useMemo(() => data?.find((w) => w.id === (activeId ?? data?.[0]?.id ?? null)) ?? data?.[0] ?? null, [data, activeId]);

  const addItem = useAddWatchlistItem(active?.id ?? '');
  const remItem = useRemoveWatchlistItem(active?.id ?? '');

  const [newOpen, setNewOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [addingSymbol, setAddingSymbol] = useState<string | null>(null);

  const createNew = async () => {
    try {
      const wl = await createWl.mutateAsync({ name: newName.trim() });
      setActiveId(wl.id);
      setNewOpen(false);
      setNewName('');
      notifications.show({ message: 'Watchlist created', color: 'teal' });
    } catch (e) {
      notifications.show({ message: (e as Error).message, color: 'red' });
    }
  };

  const addSymbol = async (symbol: string) => {
    if (!active) return;
    try {
      await addItem.mutateAsync({ symbol });
      setAddingSymbol(null);
    } catch (e) {
      notifications.show({ message: (e as Error).message, color: 'red' });
    }
  };

  const removeSymbol = async (itemId: string) => {
    if (!active) return;
    try { await remItem.mutateAsync(itemId); }
    catch (e) { notifications.show({ message: (e as Error).message, color: 'red' }); }
  };

  return (
    <Stack gap="lg">
      <Group justify="space-between" wrap="wrap">
        <Title order={2}>Watchlist</Title>
        <Group>
          {data && data.length > 0 && (
            <Select
              data={data.map((w) => ({ value: w.id, label: w.name }))}
              value={active?.id ?? null}
              onChange={setActiveId}
              w={200}
            />
          )}
          <Button leftSection={<IconPlus size={16} />} onClick={() => setNewOpen(true)}>New watchlist</Button>
        </Group>
      </Group>

      {isLoading ? (
        <Text c="dimmed">Loading...</Text>
      ) : !data || data.length === 0 ? (
        <EmptyState
          icon={<IconStar size={48} stroke={1.2} />}
          title="No watchlists yet"
          description="Track tickers you don't own yet — without affecting your portfolio."
          actionLabel="New watchlist"
          onAction={() => setNewOpen(true)}
        />
      ) : active && (
        <Card withBorder radius="md" padding="md">
          <Stack>
            <Group justify="space-between">
              <Title order={5}>{active.name}</Title>
              <Group>
                <TickerSearch value={addingSymbol} onSelect={(i) => addSymbol(i.symbol)} label="" placeholder="Add ticker..." />
              </Group>
            </Group>
            {active.items.length === 0 ? (
              <Text c="dimmed" size="sm">No tickers yet.</Text>
            ) : (
              <Table striped highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Symbol</Table.Th>
                    <Table.Th ta="right">Last</Table.Th>
                    <Table.Th ta="right">% Day</Table.Th>
                    <Table.Th />
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {active.items.map((it) => (
                    <Table.Tr key={it.id}>
                      <Table.Td><Text fw={500}>{it.symbol}</Text></Table.Td>
                      <Table.Td ta="right">{it.latestPrice == null ? '—' : it.latestPrice.toFixed(2)}</Table.Td>
                      <Table.Td ta="right" c={it.changePercent == null ? undefined : it.changePercent >= 0 ? 'teal' : 'red'}>
                        {it.changePercent == null ? '—' : `${it.changePercent >= 0 ? '+' : ''}${it.changePercent.toFixed(2)}%`}
                      </Table.Td>
                      <Table.Td ta="right">
                        <Tooltip label="Remove">
                          <ActionIcon variant="subtle" color="red" onClick={() => removeSymbol(it.id)}>
                            <IconTrash size={16} />
                          </ActionIcon>
                        </Tooltip>
                      </Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Stack>
        </Card>
      )}

      <Modal opened={newOpen} onClose={() => setNewOpen(false)} title="New watchlist" centered>
        <Stack>
          <TextInput label="Name" placeholder="Tech ideas" value={newName} onChange={(e) => setNewName(e.currentTarget.value)} required />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setNewOpen(false)}>Cancel</Button>
            <Button onClick={createNew} loading={createWl.isPending} disabled={!newName.trim()}>Create</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
