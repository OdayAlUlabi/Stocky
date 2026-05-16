import { useState } from 'react';
import { Badge, Card, Group, NumberInput, Select, Stack, Table, Text, Title } from '@mantine/core';
import { IconHistory } from '@tabler/icons-react';
import { useAuditLog } from '../../api/hooks';

const RESOURCES = ['', 'Portfolio', 'CashTransaction', 'PositionNote', 'Account', 'ReportSchedule', 'ShareToken'];

export function AuditLog() {
  const [take, setTake] = useState<number>(200);
  const [resource, setResource] = useState<string>('');
  const { data: rows, isLoading } = useAuditLog({ take, resource: resource || undefined });

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}><IconHistory size={20} /> Audit log</Title>
        <Group>
          <Select
            label="Resource"
            data={RESOURCES.map((r) => ({ value: r, label: r || 'All' }))}
            value={resource}
            onChange={(v) => setResource(v ?? '')}
            w={180}
          />
          <NumberInput
            label="Take"
            value={take}
            onChange={(v) => setTake(typeof v === 'number' ? v : 200)}
            min={1}
            max={1000}
            w={120}
          />
        </Group>
      </Group>

      <Card withBorder padding="md">
        <Table striped highlightOnHover>
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Timestamp</Table.Th>
              <Table.Th>Action</Table.Th>
              <Table.Th>Resource</Table.Th>
              <Table.Th>Resource id</Table.Th>
              <Table.Th>Method</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th>Details</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {(rows ?? []).map((a) => (
              <Table.Tr key={a.id}>
                <Table.Td>{new Date(a.timestamp).toLocaleString()}</Table.Td>
                <Table.Td><Badge variant="light">{a.action}</Badge></Table.Td>
                <Table.Td>{a.resource}</Table.Td>
                <Table.Td><Text size="xs" c="dimmed">{a.resourceId}</Text></Table.Td>
                <Table.Td>{a.method}</Table.Td>
                <Table.Td>{a.statusCode}</Table.Td>
                <Table.Td><Text size="xs" lineClamp={2}>{a.details}</Text></Table.Td>
              </Table.Tr>
            ))}
            {!isLoading && rows && rows.length === 0 && (
              <Table.Tr><Table.Td colSpan={7}><Text ta="center" c="dimmed">No audit entries.</Text></Table.Td></Table.Tr>
            )}
          </Table.Tbody>
        </Table>
      </Card>
    </Stack>
  );
}
