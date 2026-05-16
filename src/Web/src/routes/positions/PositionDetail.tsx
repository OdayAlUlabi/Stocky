import { Anchor, Badge, Card, Group, Loader, NumberFormatter, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core';
import { Link, useParams } from 'react-router-dom';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts';
import { usePositionDetail } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';

export function PositionDetail() {
  const { id, symbol } = useParams();
  const { data, isLoading, error } = usePositionDetail(id, symbol);

  if (isLoading) return <Loader />;
  if (error) return <EmptyState title="Could not load position" description={String(error)} />;
  if (!data) return <EmptyState title="No data" />;

  const fmt = (v: number | null | undefined) => v == null ? '—' : <NumberFormatter value={v} thousandSeparator decimalScale={2} prefix={data.currency === 'USD' ? '$' : ''} suffix={data.currency !== 'USD' ? ` ${data.currency}` : ''} />;

  return (
    <Stack>
      <Group justify="space-between">
        <div>
          <Title order={3}>{data.symbol} <Text component="span" c="dimmed">— {data.name}</Text></Title>
          <Group gap="xs" mt={4}>
            <Badge variant="light">{data.assetClass}</Badge>
            {data.sector && <Badge variant="light" color="grape">{data.sector}</Badge>}
            <Badge variant="outline">{data.currency}</Badge>
          </Group>
        </div>
        <Anchor component={Link} to={`/portfolios/${id}`}>← Back to portfolio</Anchor>
      </Group>

      <SimpleGrid cols={{ base: 2, md: 4 }}>
        <Card withBorder><Text size="xs" c="dimmed">Quantity</Text><Text fw={600}>{data.quantity}</Text></Card>
        <Card withBorder><Text size="xs" c="dimmed">Avg cost</Text><Text fw={600}>{fmt(data.averageCost)}</Text></Card>
        <Card withBorder><Text size="xs" c="dimmed">Market value</Text><Text fw={600}>{fmt(data.marketValue)}</Text></Card>
        <Card withBorder>
          <Text size="xs" c="dimmed">Unrealized P/L</Text>
          <Text fw={600} c={data.unrealizedPnL >= 0 ? 'teal' : 'red'}>
            {fmt(data.unrealizedPnL)} ({data.unrealizedPnLPercent.toFixed(2)}%)
          </Text>
        </Card>
      </SimpleGrid>

      <Card withBorder>
        <Title order={5} mb="xs">Price history (180d)</Title>
        {data.priceHistory.length === 0 ? <Text c="dimmed">No history yet.</Text> : (
          <ResponsiveContainer width="100%" height={240}>
            <LineChart data={data.priceHistory.map(p => ({ ...p, date: p.date.slice(0, 10) }))}>
              <CartesianGrid strokeDasharray="3 3" />
              <XAxis dataKey="date" />
              <YAxis domain={['auto', 'auto']} />
              <Tooltip />
              <Line type="monotone" dataKey="value" stroke="#228be6" dot={false} />
            </LineChart>
          </ResponsiveContainer>
        )}
      </Card>

      <Card withBorder>
        <Title order={5} mb="xs">Open lots</Title>
        {data.lots.length === 0 ? <Text c="dimmed">No open lots.</Text> : (
          <Table striped>
            <Table.Thead>
              <Table.Tr><Table.Th>Opened</Table.Th><Table.Th>Qty</Table.Th><Table.Th>Remaining</Table.Th><Table.Th>Cost/share</Table.Th><Table.Th>Cost basis</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.lots.map(l => (
                <Table.Tr key={l.id}>
                  <Table.Td>{l.openedAt.slice(0, 10)}</Table.Td>
                  <Table.Td>{l.quantity}</Table.Td>
                  <Table.Td>{l.remainingQuantity}</Table.Td>
                  <Table.Td>{fmt(l.costPerShare)}</Table.Td>
                  <Table.Td>{fmt(l.costBasis)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Card withBorder>
        <Title order={5} mb="xs">Transactions</Title>
        {data.transactions.length === 0 ? <Text c="dimmed">No transactions.</Text> : (
          <Table striped>
            <Table.Thead>
              <Table.Tr><Table.Th>Date</Table.Th><Table.Th>Type</Table.Th><Table.Th>Qty</Table.Th><Table.Th>Price</Table.Th><Table.Th>Fee</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {data.transactions.map(t => (
                <Table.Tr key={t.id}>
                  <Table.Td>{t.executedAt.slice(0, 10)}</Table.Td>
                  <Table.Td><Badge variant="light">{t.type}</Badge></Table.Td>
                  <Table.Td>{t.quantity}</Table.Td>
                  <Table.Td>{fmt(t.price)}</Table.Td>
                  <Table.Td>{fmt(t.fee)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>
    </Stack>
  );
}
