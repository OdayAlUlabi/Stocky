import { useState } from 'react';
import { ActionIcon, Badge, Button, Card, Group, Modal, Stack, Text, Textarea, TextInput, Title } from '@mantine/core';
import { IconNotes, IconPlus, IconTrash, IconEdit } from '@tabler/icons-react';
import { usePositionNotes, useCreatePositionNote, useUpdatePositionNote, useDeletePositionNote } from '../../api/hooks';
import type { PositionNoteDto } from '../../api/types';

export function PositionNotes() {
  const [symbolFilter, setSymbolFilter] = useState('');
  const { data: notes } = usePositionNotes(symbolFilter ? { symbol: symbolFilter.toUpperCase() } : {});
  const create = useCreatePositionNote();
  const update = useUpdatePositionNote();
  const del = useDeletePositionNote();

  const [modalOpen, setModalOpen] = useState(false);
  const [editing, setEditing] = useState<PositionNoteDto | null>(null);
  const [symbol, setSymbol] = useState('');
  const [body, setBody] = useState('');

  const openCreate = () => { setEditing(null); setSymbol(''); setBody(''); setModalOpen(true); };
  const openEdit = (n: PositionNoteDto) => { setEditing(n); setSymbol(n.symbol); setBody(n.body); setModalOpen(true); };

  const submit = async () => {
    if (editing) {
      await update.mutateAsync({ id: editing.id, body: { body } });
    } else {
      if (!symbol.trim() || !body.trim()) return;
      await create.mutateAsync({ symbol: symbol.toUpperCase(), body });
    }
    setModalOpen(false);
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}><IconNotes size={20} /> Position notes</Title>
        <Group>
          <TextInput
            placeholder="Filter symbol"
            value={symbolFilter}
            onChange={(e) => setSymbolFilter(e.currentTarget.value)}
            w={160}
          />
          <Button leftSection={<IconPlus size={16} />} onClick={openCreate}>New note</Button>
        </Group>
      </Group>

      <Stack>
        {(notes ?? []).map((n) => (
          <Card key={n.id} withBorder padding="md">
            <Group justify="space-between" align="flex-start">
              <Stack gap={4} style={{ flex: 1 }}>
                <Group gap="xs">
                  <Badge>{n.symbol}</Badge>
                  <Text size="xs" c="dimmed">Updated {new Date(n.updatedAt).toLocaleString()}</Text>
                </Group>
                <Text style={{ whiteSpace: 'pre-wrap' }}>{n.body}</Text>
              </Stack>
              <Group gap={4}>
                <ActionIcon variant="subtle" onClick={() => openEdit(n)}><IconEdit size={16} /></ActionIcon>
                <ActionIcon variant="subtle" color="red" onClick={() => del.mutate(n.id)}><IconTrash size={16} /></ActionIcon>
              </Group>
            </Group>
          </Card>
        ))}
        {notes && notes.length === 0 && <Text c="dimmed" ta="center">No notes yet.</Text>}
      </Stack>

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title={editing ? `Edit note — ${editing.symbol}` : 'New position note'}>
        <Stack>
          {!editing && (
            <TextInput
              label="Symbol"
              value={symbol}
              onChange={(e) => setSymbol(e.currentTarget.value.toUpperCase())}
              maxLength={16}
            />
          )}
          <Textarea
            label="Note"
            value={body}
            onChange={(e) => setBody(e.currentTarget.value)}
            minRows={6}
            autosize
            maxLength={4000}
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button onClick={submit} loading={create.isPending || update.isPending}>
              {editing ? 'Save changes' : 'Create'}
            </Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
