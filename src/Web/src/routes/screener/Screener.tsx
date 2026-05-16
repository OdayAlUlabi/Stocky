import { Badge, Button, Card, Group, NumberInput, Select, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { IconRefresh, IconSearch } from '@tabler/icons-react';
import { useMemo, useState } from 'react';
import { useScreener, useScreenerFacets } from '../../api/hooks';
import type { ScreenerQuery } from '../../api/types';

type SortValue = NonNullable<ScreenerQuery['sort']>;

const SORT_OPTIONS: { value: SortValue; label: string }[] = [
  { value: 'marketcap-desc', label: 'Market cap (high → low)' },
  { value: 'marketcap-asc',  label: 'Market cap (low → high)' },
  { value: 'divyield-desc',  label: 'Dividend yield (high → low)' },
  { value: 'beta-asc',       label: 'Beta (low → high)' },
  { value: 'symbol',         label: 'Symbol (A → Z)' }
];

function formatMarketCap(value: number | null): string {
  if (value == null) return '—';
  const abs = Math.abs(value);
  if (abs >= 1e12) return `$${(value / 1e12).toFixed(2)}T`;
  if (abs >= 1e9)  return `$${(value / 1e9).toFixed(2)}B`;
  if (abs >= 1e6)  return `$${(value / 1e6).toFixed(2)}M`;
  return `$${value.toFixed(0)}`;
}

function formatPercent(value: number | null): string {
  if (value == null) return '—';
  return `${(value * 100).toFixed(2)}%`;
}

function formatPrice(value: number | null): string {
  if (value == null) return '—';
  return `$${value.toFixed(2)}`;
}

export function Screener() {
  const facets = useScreenerFacets();

  const [search, setSearch] = useState('');
  const [assetClass, setAssetClass] = useState<string | null>(null);
  const [sector, setSector] = useState<string | null>(null);
  const [country, setCountry] = useState<string | null>(null);
  const [minMarketCap, setMinMarketCap] = useState<number | ''>('');
  const [minDividendYield, setMinDividendYield] = useState<number | ''>('');
  const [maxBeta, setMaxBeta] = useState<number | ''>('');
  const [sort, setSort] = useState<SortValue>('marketcap-desc');
  const [appliedQuery, setAppliedQuery] = useState<ScreenerQuery>({ sort: 'marketcap-desc', limit: 100 });

  const query = useScreener(appliedQuery);

  const facetData = useMemo(() => ({
    assetClasses: (facets.data?.assetClasses ?? []).map((v) => ({ value: v, label: v })),
    sectors: (facets.data?.sectors ?? []).map((v) => ({ value: v, label: v })),
    countries: (facets.data?.countries ?? []).map((v) => ({ value: v, label: v }))
  }), [facets.data]);

  const apply = () => {
    setAppliedQuery({
      q: search.trim() || undefined,
      assetClass: assetClass ?? undefined,
      sector: sector ?? undefined,
      country: country ?? undefined,
      minMarketCap: typeof minMarketCap === 'number' ? minMarketCap : undefined,
      minDividendYield: typeof minDividendYield === 'number' ? minDividendYield / 100 : undefined,
      maxBeta: typeof maxBeta === 'number' ? maxBeta : undefined,
      sort,
      limit: 100
    });
  };

  const reset = () => {
    setSearch('');
    setAssetClass(null);
    setSector(null);
    setCountry(null);
    setMinMarketCap('');
    setMinDividendYield('');
    setMaxBeta('');
    setSort('marketcap-desc');
    setAppliedQuery({ sort: 'marketcap-desc', limit: 100 });
  };

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Stack gap={0}>
          <Title order={2}>Stock screener</Title>
          <Text c="dimmed" size="sm">Filter the local instrument universe by sector, market cap, yield and beta.</Text>
        </Stack>
      </Group>

      <Card withBorder padding="md">
        <Stack gap="sm">
          <Group grow align="end">
            <TextInput
              label="Search"
              placeholder="Symbol or name"
              value={search}
              onChange={(e) => setSearch(e.currentTarget.value)}
              leftSection={<IconSearch size={14} />}
            />
            <Select
              label="Asset class"
              data={facetData.assetClasses}
              value={assetClass}
              onChange={setAssetClass}
              clearable
              placeholder="Any"
            />
            <Select
              label="Sector"
              data={facetData.sectors}
              value={sector}
              onChange={setSector}
              clearable
              placeholder="Any"
            />
            <Select
              label="Country"
              data={facetData.countries}
              value={country}
              onChange={setCountry}
              clearable
              placeholder="Any"
            />
          </Group>
          <Group grow align="end">
            <NumberInput
              label="Min market cap (USD)"
              placeholder="e.g. 10000000000"
              value={minMarketCap}
              onChange={(v) => setMinMarketCap(typeof v === 'number' ? v : '')}
              min={0}
              thousandSeparator=","
            />
            <NumberInput
              label="Min dividend yield (%)"
              placeholder="e.g. 2"
              value={minDividendYield}
              onChange={(v) => setMinDividendYield(typeof v === 'number' ? v : '')}
              min={0}
              max={100}
              decimalScale={2}
            />
            <NumberInput
              label="Max beta"
              placeholder="e.g. 1.2"
              value={maxBeta}
              onChange={(v) => setMaxBeta(typeof v === 'number' ? v : '')}
              min={0}
              decimalScale={2}
            />
            <Select
              label="Sort by"
              data={SORT_OPTIONS}
              value={sort}
              onChange={(v) => v && setSort(v as SortValue)}
            />
          </Group>
          <Group justify="flex-end">
            <Button variant="default" onClick={reset}>Reset</Button>
            <Button leftSection={<IconRefresh size={14} />} onClick={apply} loading={query.isFetching}>
              Run screen
            </Button>
          </Group>
        </Stack>
      </Card>

      <Card withBorder padding="md">
        <Group justify="space-between" mb="xs">
          <Text fw={500}>Results</Text>
          <Badge variant="light">{query.data?.total ?? 0} match{(query.data?.total ?? 0) === 1 ? '' : 'es'}</Badge>
        </Group>
        {query.isLoading ? (
          <Text c="dimmed" size="sm">Loading…</Text>
        ) : query.isError ? (
          <Text c="red" size="sm">{(query.error as Error).message}</Text>
        ) : !query.data || query.data.rows.length === 0 ? (
          <Text c="dimmed" size="sm">No instruments match the current filters.</Text>
        ) : (
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Symbol</Table.Th>
                <Table.Th>Name</Table.Th>
                <Table.Th>Sector</Table.Th>
                <Table.Th>Country</Table.Th>
                <Table.Th ta="right">Market cap</Table.Th>
                <Table.Th ta="right">Beta</Table.Th>
                <Table.Th ta="right">Div. yield</Table.Th>
                <Table.Th ta="right">Price</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {query.data.rows.map((row) => (
                <Table.Tr key={row.symbol}>
                  <Table.Td><Text fw={600}>{row.symbol}</Text></Table.Td>
                  <Table.Td>{row.name}</Table.Td>
                  <Table.Td>{row.sector ?? '—'}</Table.Td>
                  <Table.Td>{row.country ?? '—'}</Table.Td>
                  <Table.Td ta="right">{formatMarketCap(row.marketCap)}</Table.Td>
                  <Table.Td ta="right">{row.beta?.toFixed(2) ?? '—'}</Table.Td>
                  <Table.Td ta="right">{formatPercent(row.dividendYield)}</Table.Td>
                  <Table.Td ta="right">{formatPrice(row.latestPrice)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>
    </Stack>
  );
}
