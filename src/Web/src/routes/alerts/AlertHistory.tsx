import { Badge, Card, Group, Loader, Stack, Table, Text, Title } from '@mantine/core';
import { useAlertHistory } from '../../api/hooks';

export function AlertHistory() {
  const { data, isLoading } = useAlertHistory(500);
  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Alert history</Title>
      </Group>
      <Card withBorder>
        {isLoading ? <Loader /> : !data || data.length === 0 ? (
          <Text c="dimmed">No alert events recorded yet.</Text>
        ) : (
          <Table striped>
            <Table.Thead><Table.Tr>
              <Table.Th>When</Table.Th><Table.Th>Symbol</Table.Th><Table.Th>Type</Table.Th>
              <Table.Th>Condition</Table.Th><Table.Th>Value</Table.Th>
              <Table.Th>Channels</Table.Th><Table.Th>Message</Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {data.map(ev => (
                <Table.Tr key={ev.id}>
                  <Table.Td>{ev.triggeredAt.slice(0, 19).replace('T', ' ')}</Table.Td>
                  <Table.Td>{ev.symbol}</Table.Td>
                  <Table.Td><Badge variant="light">{ev.type}</Badge></Table.Td>
                  <Table.Td>{ev.condition}</Table.Td>
                  <Table.Td>{ev.triggeredValue ?? '—'}</Table.Td>
                  <Table.Td>{ev.channels}</Table.Td>
                  <Table.Td>{ev.message}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>
    </Stack>
  );
}
