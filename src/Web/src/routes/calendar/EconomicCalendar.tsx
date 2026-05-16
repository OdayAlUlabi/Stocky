import { useState } from 'react';
import { Badge, Card, Group, Loader, SegmentedControl, Stack, Table, Text, Title } from '@mantine/core';
import { useEconomicCalendar } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';

function isoDate(d: Date) { return d.toISOString().slice(0, 10); }

export function EconomicCalendar() {
  const today = new Date();
  const from = isoDate(today);
  const to = isoDate(new Date(today.getTime() + 14 * 86400000));
  const [filter, setFilter] = useState<'All' | 'High' | 'Medium' | 'Low'>('All');
  const { data, isLoading, error } = useEconomicCalendar(from, to);

  if (isLoading) return <Loader />;
  if (error) return <EmptyState title="Could not load calendar" description={String(error)} />;
  if (!data || data.length === 0) return <EmptyState title="No events" />;

  const rows = filter === 'All' ? data : data.filter(e => e.importance === filter);
  const grouped = rows.reduce<Record<string, typeof rows>>((acc, ev) => {
    (acc[ev.date] ||= []).push(ev); return acc;
  }, {});

  const colorFor = (i: string) => i === 'High' ? 'red' : i === 'Medium' ? 'yellow' : 'gray';

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Economic Calendar</Title>
        <SegmentedControl value={filter} onChange={(v) => setFilter(v as typeof filter)}
          data={['All', 'High', 'Medium', 'Low']} />
      </Group>
      {Object.entries(grouped).map(([date, events]) => (
        <Card withBorder key={date}>
          <Title order={5} mb="xs">{date}</Title>
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Time</Table.Th>
                <Table.Th>Country</Table.Th>
                <Table.Th>Indicator</Table.Th>
                <Table.Th>Importance</Table.Th>
                <Table.Th>Actual</Table.Th>
                <Table.Th>Forecast</Table.Th>
                <Table.Th>Previous</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {events.map(e => (
                <Table.Tr key={e.id}>
                  <Table.Td>{e.time}</Table.Td>
                  <Table.Td>{e.country}</Table.Td>
                  <Table.Td>{e.indicator}</Table.Td>
                  <Table.Td><Badge color={colorFor(e.importance)} variant="light">{e.importance}</Badge></Table.Td>
                  <Table.Td>{e.actual == null ? '—' : `${e.actual}${e.unit}`}</Table.Td>
                  <Table.Td>{e.forecast == null ? '—' : `${e.forecast}${e.unit}`}</Table.Td>
                  <Table.Td><Text c="dimmed">{e.previous == null ? '—' : `${e.previous}${e.unit}`}</Text></Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Card>
      ))}
    </Stack>
  );
}
