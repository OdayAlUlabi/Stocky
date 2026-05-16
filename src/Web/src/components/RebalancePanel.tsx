import { useEffect, useMemo, useState } from 'react';
import {
  Badge,
  Button,
  Card,
  Group,
  NumberFormatter,
  NumberInput,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
  Tooltip,
} from '@mantine/core';
import {
  useHoldings,
  useRebalance,
  useRebalanceTargets,
  useSaveRebalanceTargets,
} from '../api/hooks';
import type { RebalanceTargetDto } from '../api/types';

function actionColor(action: 'Buy' | 'Sell' | 'Hold') {
  return action === 'Buy' ? 'teal' : action === 'Sell' ? 'red' : 'gray';
}

export function RebalancePanel({ portfolioId, currency }: { portfolioId: string; currency: string }) {
  const holdings = useHoldings(portfolioId);
  const targets = useRebalanceTargets(portfolioId);
  const report = useRebalance(portfolioId);
  const save = useSaveRebalanceTargets(portfolioId);

  // Build editable target map keyed by symbol (uppercased).
  const [editor, setEditor] = useState<Record<string, number>>({});
  const [newSymbol, setNewSymbol] = useState('');

  // Seed editor from server data + currently held symbols (target 0 by default).
  useEffect(() => {
    if (!targets.data || !holdings.data) return;
    setEditor((prev) => {
      if (Object.keys(prev).length > 0) return prev;
      const seeded: Record<string, number> = {};
      for (const h of holdings.data) seeded[h.symbol.toUpperCase()] = 0;
      for (const t of targets.data) seeded[t.symbol.toUpperCase()] = t.targetWeightPercent;
      return seeded;
    });
  }, [targets.data, holdings.data]);

  const total = useMemo(() => Object.values(editor).reduce((a, b) => a + (Number.isFinite(b) ? b : 0), 0), [editor]);
  const overAllocated = total > 100.001;
  const cashTarget = Math.max(0, 100 - total);

  const handleSave = () => {
    const payload: RebalanceTargetDto[] = Object.entries(editor)
      .filter(([, v]) => v > 0)
      .map(([symbol, targetWeightPercent]) => ({ symbol, targetWeightPercent }));
    save.mutate(payload, {
      onSuccess: () => {
        report.refetch();
        targets.refetch();
      },
    });
  };

  const addSymbol = () => {
    const s = newSymbol.trim().toUpperCase();
    if (!s) return;
    setEditor((prev) => ({ ...prev, [s]: prev[s] ?? 0 }));
    setNewSymbol('');
  };

  return (
    <Card withBorder>
      <Stack>
        <Group justify="space-between" align="flex-end">
          <div>
            <Title order={5}>Rebalance</Title>
            <Text c="dimmed" size="sm">Set per-symbol target weights — we'll suggest trades to restore them.</Text>
          </div>
          <Group gap="xs">
            <Badge color={overAllocated ? 'red' : total > 99.999 ? 'teal' : 'blue'} variant="light">
              Targets: {total.toFixed(2)}%
            </Badge>
            <Badge color="gray" variant="light">Cash target: {cashTarget.toFixed(2)}%</Badge>
          </Group>
        </Group>

        <Table withTableBorder withColumnBorders striped>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Symbol</Table.Th>
              <Table.Th ta="right">Target %</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {Object.keys(editor).sort().map((sym) => (
              <Table.Tr key={sym}>
                <Table.Td><Text fw={500}>{sym}</Text></Table.Td>
                <Table.Td>
                  <NumberInput
                    value={editor[sym]}
                    onChange={(v) => setEditor((prev) => ({ ...prev, [sym]: typeof v === 'number' ? v : 0 }))}
                    min={0}
                    max={100}
                    step={1}
                    decimalScale={2}
                    suffix=" %"
                    styles={{ input: { textAlign: 'right' } }}
                  />
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>

        <Group>
          <TextInput
            placeholder="Add symbol (e.g. VTI)"
            value={newSymbol}
            onChange={(e) => setNewSymbol(e.currentTarget.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); addSymbol(); } }}
          />
          <Button variant="default" onClick={addSymbol}>Add</Button>
          <Tooltip label={overAllocated ? 'Targets exceed 100%' : 'Save targets'} disabled={!overAllocated}>
            <Button onClick={handleSave} loading={save.isPending} disabled={overAllocated}>Save targets</Button>
          </Tooltip>
        </Group>

        {report.data && report.data.suggestions.length > 0 && (
          <Stack gap="xs">
            <Title order={6}>Suggested trades</Title>
            <Table withTableBorder striped>
              <Table.Thead>
                <Table.Tr>
                  <Table.Th>Symbol</Table.Th>
                  <Table.Th ta="right">Current %</Table.Th>
                  <Table.Th ta="right">Target %</Table.Th>
                  <Table.Th ta="right">Drift</Table.Th>
                  <Table.Th ta="right">Trade value</Table.Th>
                  <Table.Th>Action</Table.Th>
                </Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {report.data.suggestions.map((s) => (
                  <Table.Tr key={s.symbol}>
                    <Table.Td><Text fw={500}>{s.symbol}</Text></Table.Td>
                    <Table.Td ta="right">{s.currentWeightPercent.toFixed(2)}%</Table.Td>
                    <Table.Td ta="right">{s.targetWeightPercent.toFixed(2)}%</Table.Td>
                    <Table.Td ta="right" c={Math.abs(s.driftPercent) < 1 ? 'dimmed' : s.driftPercent > 0 ? 'red' : 'teal'}>
                      {s.driftPercent > 0 ? '+' : ''}{s.driftPercent.toFixed(2)}%
                    </Table.Td>
                    <Table.Td ta="right">
                      <NumberFormatter value={s.tradeValue} prefix={currency === 'USD' ? '$' : ''} thousandSeparator decimalScale={2} fixedDecimalScale />
                    </Table.Td>
                    <Table.Td><Badge color={actionColor(s.action)} variant="light">{s.action}</Badge></Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Stack>
        )}
      </Stack>
    </Card>
  );
}
