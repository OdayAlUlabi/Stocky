import { Alert, Badge, Card, Center, Container, Group, Loader, NumberFormatter, Stack, Table, Text, Title } from '@mantine/core';
import { useParams } from 'react-router-dom';
import { usePublicShare } from '../../api/hooks';

/**
 * M11 #54 — Public read-only portfolio view. Renders at /share/:token outside
 * the authenticated shell.
 */
export function PublicPortfolioView() {
  const { token } = useParams();
  const { data, error, isLoading } = usePublicShare(token);

  return (
    <Container size="lg" py="xl">
      <Stack>
        <Group justify="space-between">
          <Title order={2}>Shared portfolio</Title>
          <Text c="dimmed" size="sm">Read-only view</Text>
        </Group>

        {isLoading && <Center><Loader /></Center>}

        {error && (
          <Alert color="red" title="Link unavailable">
            This share link is invalid, expired, or has been revoked by the owner.
          </Alert>
        )}

        {data && (
          <>
            <Card withBorder>
              <Group justify="space-between">
                <div>
                  <Title order={3}>{data.portfolioName}</Title>
                  <Text size="xs" c="dimmed">Snapshot as of {new Date(data.generatedAt).toLocaleString()} — {data.baseCurrency}</Text>
                </div>
                <Group>
                  <div>
                    <Text size="xs" c="dimmed">Total market value</Text>
                    <Text fw={700} size="xl">
                      <NumberFormatter value={data.totalMarketValue} thousandSeparator decimalScale={2} prefix={data.baseCurrency === 'USD' ? '$' : ''} suffix={data.baseCurrency !== 'USD' ? ` ${data.baseCurrency}` : ''} />
                    </Text>
                  </div>
                  {data.includesCostBasis && data.totalUnrealizedPnL !== null && (
                    <div>
                      <Text size="xs" c="dimmed">Unrealized P/L</Text>
                      <Text fw={700} size="xl" c={data.totalUnrealizedPnL >= 0 ? 'teal' : 'red'}>
                        <NumberFormatter value={data.totalUnrealizedPnL} thousandSeparator decimalScale={2} prefix={data.baseCurrency === 'USD' ? '$' : ''} />
                      </Text>
                    </div>
                  )}
                </Group>
              </Group>
            </Card>

            <Card withBorder>
              <Group justify="space-between" mb="sm">
                <Title order={5}>Holdings</Title>
                <Group gap="xs">
                  {data.includesCostBasis && <Badge variant="light">Cost basis</Badge>}
                  {data.includesTransactions && <Badge variant="light">Transactions</Badge>}
                </Group>
              </Group>
              <Table striped highlightOnHover>
                <Table.Thead>
                  <Table.Tr>
                    <Table.Th>Symbol</Table.Th>
                    <Table.Th>Quantity</Table.Th>
                    <Table.Th>Price</Table.Th>
                    <Table.Th>Market value</Table.Th>
                    {data.includesCostBasis && <Table.Th>Avg cost</Table.Th>}
                    {data.includesCostBasis && <Table.Th>Unrealized</Table.Th>}
                  </Table.Tr>
                </Table.Thead>
                <Table.Tbody>
                  {data.holdings.map(h => (
                    <Table.Tr key={h.symbol}>
                      <Table.Td>{h.symbol}</Table.Td>
                      <Table.Td><NumberFormatter value={h.quantity} thousandSeparator decimalScale={4} /></Table.Td>
                      <Table.Td>{h.latestPrice !== null ? <NumberFormatter value={h.latestPrice} thousandSeparator decimalScale={2} /> : '—'}</Table.Td>
                      <Table.Td>{h.marketValue !== null ? <NumberFormatter value={h.marketValue} thousandSeparator decimalScale={2} /> : '—'}</Table.Td>
                      {data.includesCostBasis && <Table.Td>{h.averageCost !== null ? <NumberFormatter value={h.averageCost} thousandSeparator decimalScale={2} /> : '—'}</Table.Td>}
                      {data.includesCostBasis && (
                        <Table.Td c={h.unrealizedPnL !== null && h.unrealizedPnL >= 0 ? 'teal' : 'red'}>
                          {h.unrealizedPnL !== null ? <NumberFormatter value={h.unrealizedPnL} thousandSeparator decimalScale={2} /> : '—'}
                        </Table.Td>
                      )}
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            </Card>

            {data.includesTransactions && data.transactions && (
              <Card withBorder>
                <Title order={5} mb="sm">Recent transactions</Title>
                <Table striped>
                  <Table.Thead>
                    <Table.Tr>
                      <Table.Th>Date</Table.Th>
                      <Table.Th>Type</Table.Th>
                      <Table.Th>Symbol</Table.Th>
                      <Table.Th>Qty</Table.Th>
                      <Table.Th>Price</Table.Th>
                    </Table.Tr>
                  </Table.Thead>
                  <Table.Tbody>
                    {data.transactions.map((t, i) => (
                      <Table.Tr key={i}>
                        <Table.Td>{new Date(t.executedAt).toLocaleDateString()}</Table.Td>
                        <Table.Td>{t.type}</Table.Td>
                        <Table.Td>{t.symbol ?? '—'}</Table.Td>
                        <Table.Td><NumberFormatter value={t.quantity} thousandSeparator decimalScale={4} /></Table.Td>
                        <Table.Td><NumberFormatter value={t.price} thousandSeparator decimalScale={2} /></Table.Td>
                      </Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </Card>
            )}

            <Text c="dimmed" size="xs" ta="center">Powered by Stocky — this link is read-only and can be revoked at any time.</Text>
          </>
        )}
      </Stack>
    </Container>
  );
}
