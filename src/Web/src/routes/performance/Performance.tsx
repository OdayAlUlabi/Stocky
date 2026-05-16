import { Card, Group, Loader, SegmentedControl, SimpleGrid, Stack, Text, Title } from '@mantine/core';
import { useState } from 'react';
import { useParams } from 'react-router-dom';
import { Area, AreaChart, CartesianGrid, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useAnalytics, useCorrelation, usePerformance } from '../../api/hooks';
import { CorrelationMatrix } from '../../components/CorrelationMatrix';
import { EmptyState } from '../../components/EmptyState';

export function Performance() {
  const { id } = useParams();
  const [days, setDays] = useState<number>(90);
  const { data, isLoading } = usePerformance(id, days);
  const { data: analytics } = useAnalytics(id);
  const { data: correlation, isLoading: corrLoading } = useCorrelation(id, days);

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Performance</Title>
        <SegmentedControl
          value={String(days)}
          onChange={(v) => setDays(Number(v))}
          data={[
            { value: '30', label: '1M' },
            { value: '90', label: '3M' },
            { value: '180', label: '6M' },
            { value: '365', label: '1Y' },
            { value: '1095', label: '3Y' }
          ]}
        />
      </Group>
      {isLoading ? <Loader /> : !data ? <EmptyState title="No performance data yet" description="Snapshots accrue daily; come back tomorrow." /> : (
        <>
          <SimpleGrid cols={{ base: 2, md: 4 }}>
            <Card withBorder>
              <Text size="xs" c="dimmed">TWRR</Text>
              <Text fw={600} c={data.twrPercent >= 0 ? 'teal' : 'red'}>{data.twrPercent.toFixed(2)}%</Text>
              <Text size="xs" c="dimmed">Time-weighted, chain-linked</Text>
            </Card>
            <Card withBorder>
              <Text size="xs" c="dimmed">MWRR (XIRR)</Text>
              <Text fw={600} c={data.mwrPercent >= 0 ? 'teal' : 'red'}>{data.mwrPercent.toFixed(2)}%</Text>
              <Text size="xs" c="dimmed">Annualised, money-weighted</Text>
            </Card>
            <Card withBorder><Text size="xs" c="dimmed">Best day</Text><Text fw={600} c="teal">+{data.best1Day.toFixed(2)}%</Text></Card>
            <Card withBorder><Text size="xs" c="dimmed">Worst day</Text><Text fw={600} c="red">{data.worst1Day.toFixed(2)}%</Text></Card>
          </SimpleGrid>

          {analytics && (
            <SimpleGrid cols={{ base: 2, md: 4 }}>
              <Card withBorder>
                <Text size="xs" c="dimmed">Volatility (ann.)</Text>
                <Text fw={600}>{analytics.volatility.toFixed(2)}%</Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">Sharpe (rf=0)</Text>
                <Text fw={600} c={analytics.sharpe >= 1 ? 'teal' : analytics.sharpe >= 0 ? undefined : 'red'}>
                  {analytics.sharpe.toFixed(2)}
                </Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">Beta vs {analytics.benchmarkSymbol}</Text>
                <Text fw={600}>{analytics.beta.toFixed(2)}</Text>
                <Text size="xs" c="dimmed">
                  {analytics.beta === 0 ? 'No benchmark data' : analytics.beta > 1 ? 'More volatile than market' : analytics.beta < 0 ? 'Inversely correlated' : 'Less volatile than market'}
                </Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">Max drawdown</Text>
                <Text fw={600} c="red">{analytics.maxDrawdown.toFixed(2)}%</Text>
                <Text size="xs" c="dimmed">on {analytics.maxDrawdownDate}</Text>
              </Card>
              <Card withBorder>
                <Text size="xs" c="dimmed">TTM dividend yield</Text>
                <Text fw={600}>{analytics.dividendYield.toFixed(2)}%</Text>
                <Text size="xs" c="dimmed">${analytics.ttmDividends.toFixed(2)} paid</Text>
              </Card>
            </SimpleGrid>
          )}

          <Card withBorder>
            <Title order={5} mb="xs">Value vs cost basis</Title>
            {data.series.length === 0 ? <Text c="dimmed">Awaiting first snapshot…</Text> : (
              <ResponsiveContainer width="100%" height={300}>
                <AreaChart data={data.series.map(p => ({ ...p, date: p.date.slice(0, 10) }))}>
                  <CartesianGrid strokeDasharray="3 3" />
                  <XAxis dataKey="date" />
                  <YAxis />
                  <Tooltip />
                  <Area type="monotone" dataKey="value" stroke="#228be6" fillOpacity={0.2} fill="#228be6" />
                  <Line type="monotone" dataKey="costBasis" stroke="#fab005" dot={false} />
                </AreaChart>
              </ResponsiveContainer>
            )}
          </Card>

          {corrLoading ? <Loader /> : correlation && <CorrelationMatrix data={correlation} />}
        </>
      )}
    </Stack>
  );
}
