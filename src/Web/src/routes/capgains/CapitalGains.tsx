import { Badge, Card, Loader, NumberFormatter, NumberInput, SimpleGrid, Stack, Table, Text, Title } from '@mantine/core';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useCapitalGains, useWashSales } from '../../api/hooks';

export function CapitalGains() {
  const { id } = useParams();
  const [year, setYear] = useState<number>(new Date().getUTCFullYear());
  const { data, isLoading } = useCapitalGains(id, year);
  const { data: wash } = useWashSales(id, year);

  return (
    <Stack>
      <Title order={3}>Capital gains</Title>
      <NumberInput label="Year" value={year} onChange={(v) => setYear(Number(v) || new Date().getUTCFullYear())} w={140} />
      {isLoading ? <Loader /> : !data ? null : (
        <>
          <SimpleGrid cols={{ base: 1, md: 3 }}>
            <Card withBorder><Text size="xs" c="dimmed">Short-term</Text><Text fw={600} c={data.shortTermGain >= 0 ? 'teal' : 'red'}><NumberFormatter value={data.shortTermGain} thousandSeparator decimalScale={2} /></Text></Card>
            <Card withBorder><Text size="xs" c="dimmed">Long-term</Text><Text fw={600} c={data.longTermGain >= 0 ? 'teal' : 'red'}><NumberFormatter value={data.longTermGain} thousandSeparator decimalScale={2} /></Text></Card>
            <Card withBorder><Text size="xs" c="dimmed">Total</Text><Text fw={600} c={data.totalGain >= 0 ? 'teal' : 'red'}><NumberFormatter value={data.totalGain} thousandSeparator decimalScale={2} /></Text></Card>
          </SimpleGrid>
          <Card withBorder>
            <Title order={5} mb="xs">Realized lots</Title>
            {data.lots.length === 0 ? <Text c="dimmed">No realized gains in {year}.</Text> : (
              <Table striped>
                <Table.Thead><Table.Tr>
                  <Table.Th>Symbol</Table.Th><Table.Th>Acquired</Table.Th><Table.Th>Sold</Table.Th>
                  <Table.Th>Qty</Table.Th><Table.Th>Cost</Table.Th><Table.Th>Proceeds</Table.Th>
                  <Table.Th>Gain</Table.Th><Table.Th>Term</Table.Th>
                </Table.Tr></Table.Thead>
                <Table.Tbody>
                  {data.lots.map(g => (
                    <Table.Tr key={g.id}>
                      <Table.Td>{g.symbol}</Table.Td>
                      <Table.Td>{g.acquiredAt.slice(0, 10)}</Table.Td>
                      <Table.Td>{g.soldAt.slice(0, 10)}</Table.Td>
                      <Table.Td>{g.quantity}</Table.Td>
                      <Table.Td><NumberFormatter value={g.costBasis} thousandSeparator decimalScale={2} /></Table.Td>
                      <Table.Td><NumberFormatter value={g.proceeds} thousandSeparator decimalScale={2} /></Table.Td>
                      <Table.Td c={g.gain >= 0 ? 'teal' : 'red'}><NumberFormatter value={g.gain} thousandSeparator decimalScale={2} /></Table.Td>
                      <Table.Td><Badge variant="light" color={g.isLongTerm ? 'blue' : 'orange'}>{g.isLongTerm ? 'Long' : 'Short'}</Badge></Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Card>
        </>
      )}
      {wash && (
        <Card withBorder>
          <Title order={5} mb="xs">Wash sales (IRS §1091)</Title>
          <Text size="sm" c="dimmed" mb="sm">
            Realised losses where you reacquired the same symbol within ±30 days. The
            disallowed portion must be added to the cost basis of the replacement shares.
          </Text>
          <SimpleGrid cols={{ base: 1, md: 2 }} mb="sm">
            <Card withBorder>
              <Text size="xs" c="dimmed">Total losses</Text>
              <Text fw={600} c="red"><NumberFormatter value={wash.totalLoss} thousandSeparator decimalScale={2} /></Text>
            </Card>
            <Card withBorder>
              <Text size="xs" c="dimmed">Disallowed (wash)</Text>
              <Text fw={600} c={wash.disallowedLoss === 0 ? undefined : 'orange'}>
                <NumberFormatter value={wash.disallowedLoss} thousandSeparator decimalScale={2} />
              </Text>
            </Card>
          </SimpleGrid>
          {wash.adjustments.length === 0 ? (
            <Text c="dimmed">No wash-sale adjustments in {year}.</Text>
          ) : (
            <Table striped withTableBorder>
              <Table.Thead><Table.Tr>
                <Table.Th>Symbol</Table.Th>
                <Table.Th>Sold</Table.Th>
                <Table.Th>Lot loss</Table.Th>
                <Table.Th>Replacement sh.</Table.Th>
                <Table.Th>Disallowed</Table.Th>
                <Table.Th>Allowed</Table.Th>
              </Table.Tr></Table.Thead>
              <Table.Tbody>
                {wash.adjustments.map(a => (
                  <Table.Tr key={a.lotId}>
                    <Table.Td>{a.symbol}</Table.Td>
                    <Table.Td>{a.soldAt.slice(0, 10)}</Table.Td>
                    <Table.Td c="red"><NumberFormatter value={a.lotLoss} thousandSeparator decimalScale={2} /></Table.Td>
                    <Table.Td><NumberFormatter value={a.replacementShares} thousandSeparator decimalScale={4} /></Table.Td>
                    <Table.Td c="orange"><NumberFormatter value={a.disallowedLoss} thousandSeparator decimalScale={2} /></Table.Td>
                    <Table.Td c={a.allowedLoss === 0 ? 'dimmed' : 'red'}><NumberFormatter value={a.allowedLoss} thousandSeparator decimalScale={2} /></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          )}
        </Card>
      )}
    </Stack>
  );
}
