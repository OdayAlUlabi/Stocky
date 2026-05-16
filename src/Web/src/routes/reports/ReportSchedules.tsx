import { ActionIcon, Badge, Button, Card, Group, Loader, Modal, NumberFormatter, Select, Stack, Switch, Table, Text, TextInput, Title, Tooltip } from '@mantine/core';
import { useMemo, useState } from 'react';
import {
  useCreateReportSchedule,
  useDeleteReportSchedule,
  useDownloadReportDelivery,
  useGenerateOnDemandReport,
  usePortfolios,
  useReportDeliveries,
  useReportSchedules,
  useRunReportSchedule,
  useUpdateReportSchedule
} from '../../api/hooks';
import type { CreateReportScheduleRequest, ReportCadenceName, ReportFormatName, ReportTypeName } from '../../api/types';

const TYPES: ReportTypeName[] = ['CapitalGains', 'WashSales', 'Dividends'];
const FORMATS: ReportFormatName[] = ['Csv', 'Pdf'];
const CADENCES: ReportCadenceName[] = ['OnDemand', 'Weekly', 'Monthly', 'Quarterly'];

/**
 * M11 #55 — Scheduled & on-demand exports of capital gains / wash sales / dividends.
 * Schedule editor + delivery history with one-click download.
 */
export function ReportSchedules() {
  const { data: portfolios } = usePortfolios();
  const { data: schedules, isLoading: loadingSched } = useReportSchedules();
  const { data: deliveries } = useReportDeliveries();
  const create = useCreateReportSchedule();
  const update = useUpdateReportSchedule();
  const del = useDeleteReportSchedule();
  const run = useRunReportSchedule();
  const oneShot = useGenerateOnDemandReport();
  const download = useDownloadReportDelivery();

  const [modalOpen, setModalOpen] = useState(false);
  const [form, setForm] = useState<CreateReportScheduleRequest>({
    portfolioId: '',
    type: 'CapitalGains',
    format: 'Csv',
    cadence: 'Monthly',
    email: '',
    enabled: true,
  });

  const portfolioMap = useMemo(() => Object.fromEntries((portfolios ?? []).map(p => [p.id, p.name])), [portfolios]);

  const submit = async () => {
    if (!form.portfolioId) return;
    await create.mutateAsync({ ...form, email: form.email || null });
    setModalOpen(false);
  };

  return (
    <Stack>
      <Group justify="space-between">
        <div>
          <Title order={3}>Scheduled exports</Title>
          <Text c="dimmed" size="sm">Auto-mail or archive capital-gains, wash-sales and dividends reports on a cadence.</Text>
        </div>
        <Group>
          <Button onClick={() => setModalOpen(true)} disabled={!(portfolios && portfolios.length)}>New schedule</Button>
        </Group>
      </Group>

      <Card withBorder>
        <Title order={5} mb="sm">Schedules</Title>
        {loadingSched ? <Loader /> : (!schedules || schedules.length === 0) ? (
          <Text c="dimmed">No schedules yet. Create one to start emailing or archiving reports automatically.</Text>
        ) : (
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Portfolio</Table.Th>
                <Table.Th>Type</Table.Th>
                <Table.Th>Format</Table.Th>
                <Table.Th>Cadence</Table.Th>
                <Table.Th>Email</Table.Th>
                <Table.Th>Enabled</Table.Th>
                <Table.Th>Next run</Table.Th>
                <Table.Th>Last run</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {schedules.map(s => (
                <Table.Tr key={s.id}>
                  <Table.Td>{portfolioMap[s.portfolioId] ?? s.portfolioId.slice(0, 8)}</Table.Td>
                  <Table.Td><Badge variant="light">{s.type}</Badge></Table.Td>
                  <Table.Td>{s.format}</Table.Td>
                  <Table.Td>{s.cadence}</Table.Td>
                  <Table.Td>{s.email ?? <Text span c="dimmed">inbox</Text>}</Table.Td>
                  <Table.Td>
                    <Switch
                      checked={s.enabled}
                      onChange={(e) => update.mutate({ id: s.id, body: { enabled: e.currentTarget.checked } })}
                    />
                  </Table.Td>
                  <Table.Td>{s.cadence === 'OnDemand' ? '—' : new Date(s.nextRunUtc).toLocaleString()}</Table.Td>
                  <Table.Td>{s.lastRunUtc ? new Date(s.lastRunUtc).toLocaleString() : '—'}</Table.Td>
                  <Table.Td>
                    <Group gap="xs" justify="flex-end">
                      <Button size="xs" variant="light" onClick={() => run.mutate(s.id)} loading={run.isPending}>Run now</Button>
                      <Button size="xs" color="red" variant="subtle" onClick={() => { if (confirm('Delete schedule?')) del.mutate(s.id); }}>Delete</Button>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Card withBorder>
        <Group justify="space-between" mb="sm">
          <Title order={5}>Recent deliveries</Title>
          <Group gap="xs">
            {portfolios?.[0] && TYPES.map(t => (
              <Tooltip key={t} label={`On-demand ${t} CSV for ${portfolios[0].name}`}>
                <Button
                  size="xs"
                  variant="default"
                  loading={oneShot.isPending}
                  onClick={() => oneShot.mutate({ portfolioId: portfolios[0].id, type: t, format: 'Csv' })}
                >
                  {t} CSV
                </Button>
              </Tooltip>
            ))}
          </Group>
        </Group>
        {!deliveries || deliveries.length === 0 ? (
          <Text c="dimmed">No deliveries yet. Click <em>Run now</em> on a schedule or use the on-demand buttons above.</Text>
        ) : (
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Generated</Table.Th>
                <Table.Th>Portfolio</Table.Th>
                <Table.Th>Type</Table.Th>
                <Table.Th>Format</Table.Th>
                <Table.Th>Size</Table.Th>
                <Table.Th>Trigger</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {deliveries.map(d => (
                <Table.Tr key={d.id}>
                  <Table.Td>{new Date(d.generatedAt).toLocaleString()}</Table.Td>
                  <Table.Td>{portfolioMap[d.portfolioId] ?? d.portfolioId.slice(0, 8)}</Table.Td>
                  <Table.Td>{d.type}</Table.Td>
                  <Table.Td>{d.format}</Table.Td>
                  <Table.Td><NumberFormatter value={d.sizeBytes} thousandSeparator /> bytes</Table.Td>
                  <Table.Td><Badge variant="light" color={d.trigger === 'schedule' ? 'blue' : 'gray'}>{d.trigger}</Badge></Table.Td>
                  <Table.Td>
                    <ActionIcon variant="subtle" onClick={() => download.mutate(d)} loading={download.isPending}>↓</ActionIcon>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal opened={modalOpen} onClose={() => setModalOpen(false)} title="New schedule" centered>
        <Stack>
          <Select
            label="Portfolio"
            data={(portfolios ?? []).map(p => ({ value: p.id, label: p.name }))}
            value={form.portfolioId}
            onChange={(v) => setForm({ ...form, portfolioId: v ?? '' })}
            required
          />
          <Select label="Report type" data={TYPES} value={form.type} onChange={(v) => setForm({ ...form, type: (v as ReportTypeName) ?? 'CapitalGains' })} />
          <Select label="Format" data={FORMATS} value={form.format} onChange={(v) => setForm({ ...form, format: (v as ReportFormatName) ?? 'Csv' })} />
          <Select label="Cadence" data={CADENCES} value={form.cadence} onChange={(v) => setForm({ ...form, cadence: (v as ReportCadenceName) ?? 'Monthly' })} />
          <TextInput label="Email (optional)" placeholder="advisor@example.com" value={form.email ?? ''} onChange={(e) => setForm({ ...form, email: e.currentTarget.value })} />
          <Switch label="Enabled" checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.currentTarget.checked })} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button onClick={submit} loading={create.isPending} disabled={!form.portfolioId}>Create</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
