import { Card, Loader, SimpleGrid, Stack, Text, Title } from '@mantine/core';
import { useParams } from 'react-router-dom';
import { Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip } from 'recharts';
import { useAllocation } from '../../api/hooks';
import type { AllocationSliceDto } from '../../api/types';
import { EmptyState } from '../../components/EmptyState';

const palette = ['#228be6', '#82c91e', '#fab005', '#fd7e14', '#e64980', '#15aabf', '#7950f2', '#40c057', '#f06595', '#868e96'];

function Slice({ title, items }: { title: string; items: AllocationSliceDto[] }) {
  return (
    <Card withBorder>
      <Title order={5} mb="xs">{title}</Title>
      {items.length === 0 ? <Text c="dimmed">No data.</Text> : (
        <ResponsiveContainer width="100%" height={240}>
          <PieChart>
            <Pie data={items} dataKey="value" nameKey="label" outerRadius={80} label={(e) => `${e.label} ${e.percent.toFixed(0)}%`}>
              {items.map((_, i) => <Cell key={i} fill={palette[i % palette.length]} />)}
            </Pie>
            <Tooltip />
            <Legend />
          </PieChart>
        </ResponsiveContainer>
      )}
    </Card>
  );
}

export function Allocation() {
  const { id } = useParams();
  const { data, isLoading } = useAllocation(id);
  if (isLoading) return <Loader />;
  if (!data || data.totalValue <= 0) return <EmptyState title="No allocation" description="Add some holdings to see allocation." />;
  return (
    <Stack>
      <Title order={3}>Allocation</Title>
      <SimpleGrid cols={{ base: 1, md: 2 }}>
        <Slice title="By asset class" items={data.byAsset} />
        <Slice title="By sector" items={data.bySector} />
        <Slice title="By currency" items={data.byCurrency} />
        <Slice title="By symbol" items={data.bySymbol} />
      </SimpleGrid>
    </Stack>
  );
}
