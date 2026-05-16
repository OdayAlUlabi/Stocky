import { useState } from 'react';
import { Badge, Button, Card, Group, Modal, NumberInput, SimpleGrid, Stack, Table, Text, TextInput, Title } from '@mantine/core';
import { IconLayoutGrid } from '@tabler/icons-react';
import { useModelTemplates, useApplyModelTemplate } from '../../api/hooks';
import type { ModelPortfolioTemplateDto } from '../../api/types';
import { useNavigate } from 'react-router-dom';

const RISK_COLOR: Record<string, string> = {
  Conservative: 'teal',
  Moderate: 'blue',
  Aggressive: 'orange'
};

export function ModelTemplates() {
  const { data: templates } = useModelTemplates();
  const apply = useApplyModelTemplate();
  const navigate = useNavigate();

  const [selected, setSelected] = useState<ModelPortfolioTemplateDto | null>(null);
  const [name, setName] = useState('');
  const [baseCurrency, setBaseCurrency] = useState('USD');
  const [initialCash, setInitialCash] = useState<number | ''>('');

  const open = (t: ModelPortfolioTemplateDto) => {
    setSelected(t);
    setName(t.name);
    setInitialCash('');
  };

  const submit = async () => {
    if (!selected || !name.trim()) return;
    const created = await apply.mutateAsync({
      slug: selected.slug,
      portfolioName: name,
      baseCurrency,
      initialCashDeposit: typeof initialCash === 'number' ? initialCash : null
    });
    setSelected(null);
    navigate(`/portfolios/${created.id}`);
  };

  return (
    <Stack>
      <Title order={3}><IconLayoutGrid size={20} /> Model portfolio templates</Title>
      <Text c="dimmed" size="sm">Apply a curated allocation as a new portfolio with rebalance targets pre-configured.</Text>

      <SimpleGrid cols={{ base: 1, sm: 2 }}>
        {(templates ?? []).map((t) => (
          <Card key={t.slug} withBorder padding="md">
            <Group justify="space-between" align="flex-start">
              <div>
                <Title order={5}>{t.name}</Title>
                <Badge color={RISK_COLOR[t.risk] ?? 'gray'} variant="light" mt={4}>{t.risk}</Badge>
              </div>
              <Button size="xs" onClick={() => open(t)}>Apply</Button>
            </Group>
            <Text size="sm" c="dimmed" mt="xs">{t.description}</Text>
            <Table mt="md" withTableBorder fz="xs">
              <Table.Thead>
                <Table.Tr><Table.Th>Symbol</Table.Th><Table.Th>Asset class</Table.Th><Table.Th ta="right">Weight</Table.Th></Table.Tr>
              </Table.Thead>
              <Table.Tbody>
                {t.allocations.map((a) => (
                  <Table.Tr key={a.symbol}>
                    <Table.Td><Badge variant="outline">{a.symbol}</Badge></Table.Td>
                    <Table.Td>{a.assetClass}</Table.Td>
                    <Table.Td ta="right">{a.weightPercent}%</Table.Td>
                  </Table.Tr>
                ))}
              </Table.Tbody>
            </Table>
          </Card>
        ))}
      </SimpleGrid>

      <Modal opened={!!selected} onClose={() => setSelected(null)} title={selected ? `Apply ${selected.name}` : ''}>
        <Stack>
          <TextInput label="Portfolio name" value={name} onChange={(e) => setName(e.currentTarget.value)} />
          <TextInput label="Base currency" value={baseCurrency} onChange={(e) => setBaseCurrency(e.currentTarget.value.toUpperCase())} maxLength={3} />
          <NumberInput
            label="Initial cash deposit (optional)"
            value={initialCash}
            onChange={(v) => setInitialCash(typeof v === 'number' ? v : '')}
            min={0}
            decimalScale={2}
          />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setSelected(null)}>Cancel</Button>
            <Button onClick={submit} loading={apply.isPending}>Create portfolio</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
