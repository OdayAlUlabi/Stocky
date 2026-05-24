import { Anchor, Badge, Card, Group, Loader, NumberFormatter, Progress, SimpleGrid, Stack, Table, Tabs, Text, Title } from '@mantine/core';
import { Link, useParams } from 'react-router-dom';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid } from 'recharts';
import { useEffect, useRef } from 'react';
import { createChart, CandlestickSeries, type IChartApi, type CandlestickData, type Time } from 'lightweight-charts';
import {
  usePositionDetail, useOrderBook, useExtendedQuote, useFilings,
  useInsiderTrades, useShortInterest, useOptionsFlow,
  useBars, useAnalystRating, useEarningsSurprises
} from '../../api/hooks';
import { usePriceTicks } from '../../api/priceStream';
import { EmptyState } from '../../components/EmptyState';
import { ApiErrorAlert } from '../../components/ApiErrorAlert';
import type { OhlcBarDto, AnalystRatingDto, EarningsSurprisePointDto } from '../../api/types';

/** M9 #21 — TradingView Lightweight Charts candlestick on Position Detail. */
function CandlestickChart({ bars }: { bars: OhlcBarDto[] }) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const chartRef = useRef<IChartApi | null>(null);
  useEffect(() => {
    if (!containerRef.current) return;
    const chart = createChart(containerRef.current, {
      height: 320,
      layout: { background: { color: 'transparent' }, textColor: '#888' },
      grid: { vertLines: { color: '#2a2a2a' }, horzLines: { color: '#2a2a2a' } },
      timeScale: { timeVisible: false, secondsVisible: false },
      autoSize: true,
    });
    chartRef.current = chart;
    const series = chart.addSeries(CandlestickSeries, {
      upColor: '#26a69a', downColor: '#ef5350',
      borderUpColor: '#26a69a', borderDownColor: '#ef5350',
      wickUpColor: '#26a69a', wickDownColor: '#ef5350',
    });
    const data: CandlestickData[] = bars.map(b => ({
      time: b.date as Time,
      open: b.open, high: b.high, low: b.low, close: b.close,
    }));
    series.setData(data);
    chart.timeScale().fitContent();
    return () => { chart.remove(); chartRef.current = null; };
  }, [bars]);
  return <div ref={containerRef} style={{ width: '100%', height: 320 }} />;
}

/** M9 #22 — Wall Street analyst-consensus panel. */
function AnalystRatingPanel({ rating, surprises }: { rating: AnalystRatingDto; surprises: EarningsSurprisePointDto[] | undefined }) {
  const d = rating.distribution;
  const total = Math.max(1, rating.analystCount);
  const seg = (n: number) => (n / total) * 100;
  const labelColor = rating.consensusScore >= 4 ? 'teal' : rating.consensusScore >= 3 ? 'blue' : rating.consensusScore >= 2 ? 'yellow' : 'red';
  return (
    <Card withBorder>
      <Group justify="space-between" mb="xs">
        <Title order={5}>Analyst ratings</Title>
        <Badge color={labelColor} variant="filled">{rating.consensusLabel}</Badge>
      </Group>
      <SimpleGrid cols={{ base: 2, md: 4 }} mb="sm">
        <Card withBorder><Text size="xs" c="dimmed">Score</Text><Text fw={600}>{rating.consensusScore.toFixed(2)} / 5</Text></Card>
        <Card withBorder><Text size="xs" c="dimmed">Analysts</Text><Text fw={600}>{rating.analystCount}</Text></Card>
        <Card withBorder><Text size="xs" c="dimmed">Target (mean)</Text><Text fw={600}>${rating.priceTargetMean.toFixed(2)}</Text></Card>
        <Card withBorder><Text size="xs" c="dimmed">Range</Text><Text fw={600}>${rating.priceTargetLow.toFixed(2)} – ${rating.priceTargetHigh.toFixed(2)}</Text></Card>
      </SimpleGrid>
      <Text size="xs" c="dimmed" mb={4}>Distribution</Text>
      <Progress.Root size="xl">
        <Progress.Section value={seg(d.strongBuy)} color="teal.7"><Progress.Label>SB {d.strongBuy}</Progress.Label></Progress.Section>
        <Progress.Section value={seg(d.buy)} color="teal.4"><Progress.Label>B {d.buy}</Progress.Label></Progress.Section>
        <Progress.Section value={seg(d.hold)} color="gray.5"><Progress.Label>H {d.hold}</Progress.Label></Progress.Section>
        <Progress.Section value={seg(d.sell)} color="red.4"><Progress.Label>S {d.sell}</Progress.Label></Progress.Section>
        <Progress.Section value={seg(d.strongSell)} color="red.7"><Progress.Label>SS {d.strongSell}</Progress.Label></Progress.Section>
      </Progress.Root>
      {surprises && surprises.length > 0 && (
        <>
          <Text size="xs" c="dimmed" mt="md" mb={4}>EPS surprise (last {surprises.length} quarters)</Text>
          <Table fz="xs">
            <Table.Thead>
              <Table.Tr><Table.Th>Quarter</Table.Th><Table.Th>Est.</Table.Th><Table.Th>Actual</Table.Th><Table.Th>Surprise</Table.Th></Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {surprises.map(s => (
                <Table.Tr key={s.date}>
                  <Table.Td>{s.date}</Table.Td>
                  <Table.Td>{s.epsEstimate?.toFixed(2) ?? '—'}</Table.Td>
                  <Table.Td>{s.epsActual?.toFixed(2) ?? '—'}</Table.Td>
                  <Table.Td c={(s.surprisePercent ?? 0) >= 0 ? 'teal' : 'red'}>
                    {s.surprisePercent == null ? '—' : `${s.surprisePercent >= 0 ? '+' : ''}${s.surprisePercent.toFixed(1)}%`}
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </>
      )}
    </Card>
  );
}

export function PositionDetail() {
  const { id, symbol } = useParams();
  const sym = symbol?.toUpperCase();
  const { data, isLoading, error } = usePositionDetail(id, symbol);
  usePriceTicks(sym ? [sym] : []);
  const ext = useExtendedQuote(sym);
  const book = useOrderBook(sym);
  const filings = useFilings(sym ? [sym] : undefined, 15);
  const insider = useInsiderTrades(sym, 15);
  const shortInt = useShortInterest(sym);
  const options = useOptionsFlow(sym, 15);
  const bars = useBars(sym, 180);
  const rating = useAnalystRating(sym);
  const surprises = useEarningsSurprises(sym, 8);

  if (isLoading) return <Loader />;
  if (error) return <ApiErrorAlert error={error} title="Could not load position" />;
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
            {ext.data && ext.data.session !== 'Regular' && (
              <Badge color={ext.data.extendedChange >= 0 ? 'teal' : 'red'} variant="filled">
                {ext.data.session}: {fmt(ext.data.extendedPrice)} ({ext.data.extendedChangePercent.toFixed(2)}%)
              </Badge>
            )}
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
        {bars.isLoading ? <Loader size="sm" /> : !bars.data || bars.data.length === 0 ? (
          data.priceHistory.length === 0 ? <Text c="dimmed">No history yet.</Text> : (
            <ResponsiveContainer width="100%" height={240}>
              <LineChart data={data.priceHistory.map(p => ({ ...p, date: p.date.slice(0, 10) }))}>
                <CartesianGrid strokeDasharray="3 3" />
                <XAxis dataKey="date" />
                <YAxis domain={['auto', 'auto']} />
                <Tooltip />
                <Line type="monotone" dataKey="value" stroke="#228be6" dot={false} />
              </LineChart>
            </ResponsiveContainer>
          )
        ) : (
          <CandlestickChart bars={bars.data} />
        )}
      </Card>

      {rating.data && <AnalystRatingPanel rating={rating.data} surprises={surprises.data} />}

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
      <Card withBorder>
        <Tabs defaultValue="book">
          <Tabs.List>
            <Tabs.Tab value="book">Order book</Tabs.Tab>
            <Tabs.Tab value="filings">Filings</Tabs.Tab>
            <Tabs.Tab value="insider">Insider trades</Tabs.Tab>
            <Tabs.Tab value="short">Short interest</Tabs.Tab>
            <Tabs.Tab value="options">Options flow</Tabs.Tab>
          </Tabs.List>

          <Tabs.Panel value="book" pt="md">
            {book.isLoading ? <Loader size="sm" /> : !book.data ? <Text c="dimmed">No data</Text> : (
              <SimpleGrid cols={2}>
                <Table>
                  <Table.Thead><Table.Tr><Table.Th>Bid</Table.Th><Table.Th>Size</Table.Th></Table.Tr></Table.Thead>
                  <Table.Tbody>
                    {book.data.bids.map((l, i) => (
                      <Table.Tr key={i}><Table.Td c="teal">{fmt(l.price)}</Table.Td><Table.Td>{l.size}</Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
                <Table>
                  <Table.Thead><Table.Tr><Table.Th>Ask</Table.Th><Table.Th>Size</Table.Th></Table.Tr></Table.Thead>
                  <Table.Tbody>
                    {book.data.asks.map((l, i) => (
                      <Table.Tr key={i}><Table.Td c="red">{fmt(l.price)}</Table.Td><Table.Td>{l.size}</Table.Td></Table.Tr>
                    ))}
                  </Table.Tbody>
                </Table>
              </SimpleGrid>
            )}
          </Tabs.Panel>

          <Tabs.Panel value="filings" pt="md">
            {filings.isLoading ? <Loader size="sm" /> : !filings.data || filings.data.length === 0 ? <Text c="dimmed">No filings</Text> : (
              <Table striped>
                <Table.Thead><Table.Tr><Table.Th>Filed</Table.Th><Table.Th>Form</Table.Th><Table.Th>Title</Table.Th><Table.Th>Link</Table.Th></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {filings.data.map(f => (
                    <Table.Tr key={f.id}>
                      <Table.Td>{f.filedAt}</Table.Td>
                      <Table.Td><Badge variant="light">{f.form}</Badge></Table.Td>
                      <Table.Td>{f.title}</Table.Td>
                      <Table.Td><Anchor href={f.url} target="_blank" rel="noreferrer">EDGAR</Anchor></Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Tabs.Panel>

          <Tabs.Panel value="insider" pt="md">
            {insider.isLoading ? <Loader size="sm" /> : !insider.data || insider.data.length === 0 ? <Text c="dimmed">No insider trades</Text> : (
              <Table striped>
                <Table.Thead><Table.Tr><Table.Th>Filed</Table.Th><Table.Th>Insider</Table.Th><Table.Th>Role</Table.Th><Table.Th>Side</Table.Th><Table.Th>Qty</Table.Th><Table.Th>Price</Table.Th><Table.Th>Value</Table.Th></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {insider.data.map(t => (
                    <Table.Tr key={t.id}>
                      <Table.Td>{t.filedAt}</Table.Td>
                      <Table.Td>{t.insider}</Table.Td>
                      <Table.Td>{t.role}</Table.Td>
                      <Table.Td><Badge color={t.side === 'Buy' ? 'teal' : 'red'} variant="light">{t.side}</Badge></Table.Td>
                      <Table.Td>{t.quantity}</Table.Td>
                      <Table.Td>{fmt(t.price)}</Table.Td>
                      <Table.Td>{fmt(t.value)}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Tabs.Panel>

          <Tabs.Panel value="short" pt="md">
            {shortInt.isLoading ? <Loader size="sm" /> : !shortInt.data ? <Text c="dimmed">No data</Text> : (
              <Stack>
                <SimpleGrid cols={{ base: 2, md: 4 }}>
                  <Card withBorder><Text size="xs" c="dimmed">Report date</Text><Text fw={600}>{shortInt.data.reportDate}</Text></Card>
                  <Card withBorder><Text size="xs" c="dimmed">% of float</Text><Text fw={600}>{shortInt.data.percentOfFloat.toFixed(2)}%</Text></Card>
                  <Card withBorder><Text size="xs" c="dimmed">Days to cover</Text><Text fw={600}>{shortInt.data.daysToCover.toFixed(2)}</Text></Card>
                  <Card withBorder><Text size="xs" c="dimmed">Short interest</Text><Text fw={600}>{shortInt.data.shortInterest.toLocaleString()}</Text></Card>
                </SimpleGrid>
                <ResponsiveContainer width="100%" height={180}>
                  <LineChart data={shortInt.data.history.map(h => ({ date: h.reportDate, pct: h.percentOfFloat }))}>
                    <CartesianGrid strokeDasharray="3 3" />
                    <XAxis dataKey="date" />
                    <YAxis />
                    <Tooltip />
                    <Line type="monotone" dataKey="pct" stroke="#fa5252" dot={false} />
                  </LineChart>
                </ResponsiveContainer>
              </Stack>
            )}
          </Tabs.Panel>

          <Tabs.Panel value="options" pt="md">
            {options.isLoading ? <Loader size="sm" /> : !options.data || options.data.rows.length === 0 ? <Text c="dimmed">No data</Text> : (
              <Table striped>
                <Table.Thead><Table.Tr><Table.Th>Side</Table.Th><Table.Th>Strike</Table.Th><Table.Th>Expiry</Table.Th><Table.Th>Volume</Table.Th><Table.Th>OI</Table.Th><Table.Th>V/OI</Table.Th><Table.Th>Premium</Table.Th></Table.Tr></Table.Thead>
                <Table.Tbody>
                  {options.data.rows.map((r, i) => (
                    <Table.Tr key={i}>
                      <Table.Td><Badge color={r.side === 'Call' ? 'teal' : 'red'} variant="light">{r.side}</Badge></Table.Td>
                      <Table.Td>{fmt(r.strike)}</Table.Td>
                      <Table.Td>{r.expiry}</Table.Td>
                      <Table.Td>{r.volume.toLocaleString()}</Table.Td>
                      <Table.Td>{r.openInterest.toLocaleString()}</Table.Td>
                      <Table.Td>{r.volumeOverOpenInterest >= 3 ? <Badge color="orange">{r.volumeOverOpenInterest.toFixed(2)}</Badge> : r.volumeOverOpenInterest.toFixed(2)}</Table.Td>
                      <Table.Td>{fmt(r.premium)}</Table.Td>
                    </Table.Tr>
                  ))}
                </Table.Tbody>
              </Table>
            )}
          </Tabs.Panel>
        </Tabs>
      </Card>
    </Stack>
  );
}
