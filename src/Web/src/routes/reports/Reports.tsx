import { Card, Loader, NumberFormatter, NumberInput, SimpleGrid, Stack, Table, Title, Text } from '@mantine/core';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { useDividends, useReportSummary } from '../../api/hooks';

export function Reports() {
  const { id } = useParams();
  const [year, setYear] = useState<number>(new Date().getUTCFullYear());
  const { data: summary, isLoading } = useReportSummary(id);
  const { data: divs } = useDividends(id, year);

  const Money = ({ v, cur }: { v: number; cur: string }) =>
    <NumberFormatter value={v} thousandSeparator decimalScale={2} prefix={cur === 'USD' ? '$' : ''} suffix={cur !== 'USD' ? ` ${cur}` : ''} />;

  return (
    <Stack>
      <Title order={3}>Reports</Title>
      {isLoading || !summary ? <Loader /> : (
        <SimpleGrid cols={{ base: 2, md: 4 }}>
          <Card withBorder><Text size="xs" c="dimmed">Net contributions</Text><Text fw={600}><Money v={summary.netContributions} cur={summary.currency} /></Text></Card>
          <Card withBorder><Text size="xs" c="dimmed">Market value</Text><Text fw={600}><Money v={summary.marketValue} cur={summary.currency} /></Text></Card>
          <Card withBorder><Text size="xs" c="dimmed">Realized P/L</Text><Text fw={600} c={summary.realizedPnL >= 0 ? 'teal' : 'red'}><Money v={summary.realizedPnL} cur={summary.currency} /></Text></Card>
          <Card withBorder><Text size="xs" c="dimmed">Unrealized P/L</Text><Text fw={600} c={summary.unrealizedPnL >= 0 ? 'teal' : 'red'}><Money v={summary.unrealizedPnL} cur={summary.currency} /></Text></Card>
          <Card withBorder><Text size="xs" c="dimmed">Dividends</Text><Text fw={600}><Money v={summary.dividends} cur={summary.currency} /></Text></Card>
          <Card withBorder><Text size="xs" c="dimmed">Fees</Text><Text fw={600}><Money v={summary.fees} cur={summary.currency} /></Text></Card>
        </SimpleGrid>
      )}

      <Card withBorder>
        <Title order={5} mb="xs">Dividends</Title>
        <NumberInput value={year} onChange={(v) => setYear(Number(v) || new Date().getUTCFullYear())} label="Year" w={120} mb="sm" />
        {!divs || divs.length === 0 ? <Text c="dimmed">No dividends in {year}.</Text> : (
          <Table striped>
            <Table.Thead><Table.Tr><Table.Th>Date</Table.Th><Table.Th>Symbol</Table.Th><Table.Th>Amount</Table.Th><Table.Th>Currency</Table.Th></Table.Tr></Table.Thead>
            <Table.Tbody>
              {divs.map((d, i) => (
                <Table.Tr key={i}>
                  <Table.Td>{d.date.slice(0, 10)}</Table.Td>
                  <Table.Td>{d.symbol}</Table.Td>
                  <Table.Td><NumberFormatter value={d.amount} thousandSeparator decimalScale={2} /></Table.Td>
                  <Table.Td>{d.currency}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>
    </Stack>
  );
}
