import { Button, Card, Group, Modal, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { IconPlus, IconBriefcase } from '@tabler/icons-react';
import { useNavigate } from 'react-router-dom';
import { useState } from 'react';
import { notifications } from '@mantine/notifications';
import dayjs from 'dayjs';
import { useCreatePortfolio, usePortfolios } from '../../api/hooks';
import { formatApiError } from '../../api/client';
import { EmptyState } from '../../components/EmptyState';

export function PortfolioList() {
  const navigate = useNavigate();
  const { data, isLoading } = usePortfolios();
  const createMut = useCreatePortfolio();
  const [opened, { open, close }] = useDisclosure(false);
  const [name, setName] = useState('');
  const [ccy, setCcy] = useState('USD');

  const submit = async () => {
    try {
      await createMut.mutateAsync({ name: name.trim(), baseCurrency: ccy.toUpperCase() });
      notifications.show({ message: `Created ${name}`, color: 'teal' });
      setName('');
      close();
    } catch (e) {
      notifications.show({ message: formatApiError(e), color: 'red' });
    }
  };

  return (
    <Stack gap="lg">
      <Group justify="space-between">
        <Title order={2}>Portfolios</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={open}>New portfolio</Button>
      </Group>

      {isLoading ? (
        <Text c="dimmed">Loading...</Text>
      ) : !data || data.length === 0 ? (
        <EmptyState
          icon={<IconBriefcase size={48} stroke={1.2} />}
          title="No portfolios yet"
          description="Create a portfolio to start tracking trades and positions."
          actionLabel="New portfolio"
          onAction={open}
        />
      ) : (
        <Card withBorder radius="md" padding="0">
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Name</Table.Th>
                <Table.Th>Base currency</Table.Th>
                <Table.Th>Created</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.map((p) => (
                <Table.Tr key={p.id} onClick={() => navigate(`/portfolios/${p.id}`)} style={{ cursor: 'pointer' }}>
                  <Table.Td><Text fw={500}>{p.name}</Text></Table.Td>
                  <Table.Td>{p.baseCurrency}</Table.Td>
                  <Table.Td>{dayjs(p.createdAt).format('MMM D, YYYY')}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Card>
      )}

      <Modal opened={opened} onClose={close} title="New portfolio" centered>
        <Stack>
          <TextInput label="Name" placeholder="Long-term" value={name} onChange={(e) => setName(e.currentTarget.value)} required />
          <TextInput label="Base currency" value={ccy} onChange={(e) => setCcy(e.currentTarget.value)} maxLength={3} />
          <Group justify="flex-end">
            <Button variant="default" onClick={close}>Cancel</Button>
            <Button onClick={submit} loading={createMut.isPending} disabled={!name.trim()}>Create</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
