import {
  ActionIcon,
  Badge,
  Card,
  Group,
  Loader,
  SimpleGrid,
  Stack,
  Text,
  Title,
  Tooltip as MTooltip
} from '@mantine/core';
import { IconArrowLeft, IconInfoCircle, IconTrendingDown, IconTrendingUp } from '@tabler/icons-react';
import { Link, useParams } from 'react-router-dom';
import { Area, AreaChart, Bar, BarChart, Cell, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { usePortfolioAnalytics } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';

function fmtPct(n: number, dp = 2) {
  return `${n >= 0 ? '+' : ''}${n.toFixed(dp)}%`;
}
function fmtCurrency(n: number, currency: string) {
  return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(n);
}

function Metric({
  label, value, sub, tone, hint
}: { label: string; value: string; sub?: string; tone?: 'pos' | 'neg' | 'neutral'; hint?: string }) {
  const color = tone === 'pos' ? 'teal' : tone === 'neg' ? 'red' : undefined;
  return (
    <Card withBorder padding="md">
      <Group gap={4} mb={4} wrap="nowrap">
        <Text size="xs" c="dimmed" tt="uppercase" fw={600}>{label}</Text>
        {hint && (
          <MTooltip label={hint} multiline w={260} withArrow>
            <IconInfoCircle size={12} style={{ opacity: 0.5 }} />
          </MTooltip>
        )}
      </Group>
      <Text fw={700} size="xl" c={color}>{value}</Text>
      {sub && <Text size="xs" c="dimmed" mt={2}>{sub}</Text>}
    </Card>
  );
}

export function PortfolioAnalytics() {
  const { id = '' } = useParams<{ id: string }>();
  const { data, isLoading } = usePortfolioAnalytics(id);

  if (isLoading) return <Loader />;
  if (!data) return <EmptyState title="No analytics" description="Could not load analytics for this portfolio." />;
  if (data.drawdownSeries.length === 0) {
    return (
      <Stack gap="md">
        <Group>
          <ActionIcon variant="default" component={Link} to={`/portfolios/${id}`}><IconArrowLeft size={16} /></ActionIcon>
          <Title order={2}>Performance analytics</Title>
        </Group>
        <EmptyState title="Not enough history" description="Add transactions to start computing TWRR, MWRR and drawdown." />
      </Stack>
    );
  }

  const tone = (n: number): 'pos' | 'neg' | 'neutral' => (n > 0 ? 'pos' : n < 0 ? 'neg' : 'neutral');

  return (
    <Stack gap="lg">
      <Group justify="space-between">
        <Group>
          <ActionIcon variant="default" component={Link} to={`/portfolios/${id}`}><IconArrowLeft size={16} /></ActionIcon>
          <Title order={2}>Performance analytics</Title>
        </Group>
        <Badge variant="light">{data.from} → {data.to}</Badge>
      </Group>

      <SimpleGrid cols={{ base: 2, sm: 3, md: 4 }} spacing="md">
        <Metric
          label="Total return"
          value={fmtPct(data.totalReturnPercent)}
          tone={tone(data.totalReturnPercent)}
          hint="Total equity minus net contributions, divided by net contributions. Simple cumulative %."
        />
        <Metric
          label="TWRR"
          value={fmtPct(data.twrr)}
          sub={`Annualised ${fmtPct(data.twrrAnnualised)}`}
          tone={tone(data.twrr)}
          hint="Time-weighted return — chains daily sub-period returns and neutralises the effect of deposits/withdrawals. Industry standard for comparing managers/portfolios."
        />
        <Metric
          label="MWRR (XIRR)"
          value={fmtPct(data.mwrr)}
          sub="annualised"
          tone={tone(data.mwrr)}
          hint="Money-weighted return — the annualised IRR your actual cash flows earned. Reflects timing of your deposits."
        />
        <Metric
          label="Sharpe"
          value={data.sharpe.toFixed(2)}
          sub={`Vol ${data.volatility.toFixed(1)}%/yr`}
          tone={data.sharpe > 1 ? 'pos' : data.sharpe < 0 ? 'neg' : 'neutral'}
          hint="Sharpe ratio with risk-free rate = 0. Daily-return stdev × √252 for volatility."
        />
        <Metric
          label="Max drawdown"
          value={fmtPct(data.maxDrawdown)}
          sub={`on ${data.maxDrawdownDate}`}
          tone="neg"
          hint="Largest peak-to-trough decline on the equity curve."
        />
        <Metric
          label="Peak equity"
          value={fmtCurrency(data.peakEquity, data.currency)}
          hint="Highest total-equity value reached over the period."
        />
        <Metric
          label="Best day"
          value={fmtPct(data.bestDay)}
          sub={data.bestDayDate}
          tone="pos"
        />
        <Metric
          label="Worst day"
          value={fmtPct(data.worstDay)}
          sub={data.worstDayDate}
          tone="neg"
        />
        <Metric
          label="Dividends (TTM)"
          value={fmtCurrency(data.ttmDividends, data.currency)}
          sub={`Yield ${data.dividendYield.toFixed(2)}%`}
          tone={data.ttmDividends > 0 ? 'pos' : 'neutral'}
          hint="Trailing 12-month dividends received. Yield = TTM divs / current equity."
        />
        <Metric
          label="Dividends (all-time)"
          value={fmtCurrency(data.totalDividends, data.currency)}
          tone={data.totalDividends > 0 ? 'pos' : 'neutral'}
        />
      </SimpleGrid>

      <Card withBorder>
        <Group justify="space-between" mb="sm">
          <Group gap={6}>
            <IconTrendingDown size={16} />
            <Title order={4}>Drawdown</Title>
          </Group>
          <Text size="xs" c="dimmed">Peak-to-trough decline over time</Text>
        </Group>
        <div style={{ width: '100%', height: 240 }}>
          <ResponsiveContainer>
            <AreaChart data={data.drawdownSeries}>
              <defs>
                <linearGradient id="ddFill" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="#fa5252" stopOpacity={0.4} />
                  <stop offset="100%" stopColor="#fa5252" stopOpacity={0.05} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
              <XAxis dataKey="date" minTickGap={40} fontSize={11} />
              <YAxis domain={[(dataMin: number) => Math.floor(Math.min(dataMin, 0)), 0]} tickFormatter={(v) => `${v.toFixed(0)}%`} fontSize={11} />
              <Tooltip formatter={(v: number) => fmtPct(v)} />
              <Area type="monotone" dataKey="drawdownPercent" stroke="#fa5252" fill="url(#ddFill)" />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      </Card>

      <Card withBorder>
        <Group justify="space-between" mb="sm">
          <Group gap={6}>
            <IconTrendingUp size={16} />
            <Title order={4}>Daily returns</Title>
          </Group>
          <Text size="xs" c="dimmed">Each bar is one trading day's TWRR contribution</Text>
        </Group>
        <div style={{ width: '100%', height: 220 }}>
          <ResponsiveContainer>
            <BarChart data={data.dailyReturnSeries}>
              <CartesianGrid strokeDasharray="3 3" opacity={0.3} />
              <XAxis dataKey="date" minTickGap={40} fontSize={11} />
              <YAxis tickFormatter={(v) => `${v.toFixed(1)}%`} fontSize={11} />
              <Tooltip formatter={(v: number) => fmtPct(v, 3)} />
              <Bar dataKey="returnPercent">
                {data.dailyReturnSeries.map((d, i) => (
                  <Cell key={i} fill={d.returnPercent >= 0 ? '#12b886' : '#fa5252'} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        </div>
      </Card>
    </Stack>
  );
}
