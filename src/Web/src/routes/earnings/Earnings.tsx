import { Card, Loader, Stack, Table, Text, Title } from '@mantine/core';
import { useEarnings } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';

export function Earnings() {
  const { data, isLoading } = useEarnings();
  if (isLoading) return <Loader />;
  if (!data || data.length === 0) return <EmptyState title="No upcoming earnings" />;
  const byDate = data.reduce<Record<string, typeof data>>((acc, e) => {
    const k = e.date.slice(0, 10);
    (acc[k] ??= [] as typeof data).push(e);
    return acc;
  }, {});
  return (
    <Stack>
      <Title order={3}>Earnings calendar (next 14 days)</Title>
      {Object.entries(byDate).map(([date, items]) => (
        <Card key={date} withBorder>
          <Text fw={600} mb="xs">{date}</Text>
          <Table>
            <Table.Thead><Table.Tr>
              <Table.Th>Symbol</Table.Th><Table.Th>Time</Table.Th>
              <Table.Th>EPS est</Table.Th><Table.Th>Revenue est</Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {items.map(e => (
                <Table.Tr key={e.id}>
                  <Table.Td>{e.symbol}</Table.Td>
                  <Table.Td>{e.time ?? '—'}</Table.Td>
                  <Table.Td>{e.epsEstimate ?? '—'}</Table.Td>
                  <Table.Td>{e.revenueEstimate ?? '—'}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Card>
      ))}
    </Stack>
  );
}
