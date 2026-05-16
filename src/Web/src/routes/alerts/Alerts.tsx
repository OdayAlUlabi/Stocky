import { ActionIcon, Badge, Button, Card, Group, Loader, Modal, MultiSelect, NumberInput, Select, Stack, Table, TextInput, Title, Text } from '@mantine/core';
import { useState } from 'react';
import { Link } from 'react-router-dom';
import { IconTrash, IconPlus, IconZzz, IconHistory } from '@tabler/icons-react';
import {
  useAlerts, useCreateAlert, useDeleteAlert,
  useSnoozeAlert, useReactivateAlert
} from '../../api/hooks';
import type { AlertCondition, AlertType } from '../../api/types';

const typeOptions: { value: AlertType; label: string }[] = [
  { value: 'Price', label: 'Price' },
  { value: 'Technical', label: 'Technical' },
  { value: 'Earnings', label: 'Earnings' },
  { value: 'News', label: 'News' },
  { value: 'Drift', label: 'Portfolio drift' },
  { value: 'Insider', label: 'Insider activity' }
];

const conditionsByType: Record<AlertType, { value: AlertCondition; label: string }[]> = {
  Price: [
    { value: 'PriceAbove', label: 'Price above' },
    { value: 'PriceBelow', label: 'Price below' },
    { value: 'DayChangePercentAbove', label: 'Day change % above' },
    { value: 'DayChangePercentBelow', label: 'Day change % below' }
  ],
  Technical: [
    { value: 'SmaCrossAbove', label: 'SMA cross above' },
    { value: 'SmaCrossBelow', label: 'SMA cross below' },
    { value: 'RsiAbove', label: 'RSI above' },
    { value: 'RsiBelow', label: 'RSI below' }
  ],
  Earnings: [{ value: 'EarningsWithinDays', label: 'Earnings within N days' }],
  News: [{ value: 'NewsKeyword', label: 'News keyword / sentiment' }],
  Drift: [{ value: 'DriftAbovePercent', label: 'Drift above % pts' }],
  Insider: [
    { value: 'InsiderClusterBuy', label: 'Insider cluster buy' },
    { value: 'InsiderClusterSell', label: 'Insider cluster sell' }
  ]
};

const channelOptions = ['Inbox', 'Email', 'Push', 'Webhook'];

export function Alerts() {
  const { data, isLoading } = useAlerts();
  const create = useCreateAlert();
  const del = useDeleteAlert();
  const snooze = useSnoozeAlert();
  const reactivate = useReactivateAlert();

  const [open, setOpen] = useState(false);
  const [type, setType] = useState<AlertType>('Price');
  const [symbol, setSymbol] = useState('');
  const [condition, setCondition] = useState<AlertCondition>('PriceAbove');
  const [threshold, setThreshold] = useState<number | string>(0);
  const [note, setNote] = useState('');
  const [channels, setChannels] = useState<string[]>(['Inbox']);
  const [webhookUrl, setWebhookUrl] = useState('');
  const [period, setPeriod] = useState<number | string>(14);
  const [keyword, setKeyword] = useState('');
  const [minSent, setMinSent] = useState<number | string>(0);
  const [days, setDays] = useState<number | string>(7);

  const onTypeChange = (v: AlertType) => {
    setType(v);
    setCondition(conditionsByType[v][0].value);
  };

  const submit = async () => {
    if (!symbol && type !== 'Drift') return;
    await create.mutateAsync({
      symbol: symbol || 'PORTFOLIO',
      condition,
      threshold: Number(threshold),
      note: note || null,
      type,
      channels: channels.join(','),
      webhookUrl: channels.includes('Webhook') ? webhookUrl || null : null,
      indicatorPeriod: type === 'Technical' ? Number(period) : null,
      keywordFilter: type === 'News' ? keyword || null : null,
      minSentiment: type === 'News' ? Number(minSent) : null,
      daysBeforeEarnings: type === 'Earnings' ? Number(days) : null
    });
    setOpen(false);
    setSymbol(''); setThreshold(0); setNote(''); setKeyword(''); setWebhookUrl('');
  };

  const onSnooze = (id: string, hours: number) => {
    const until = new Date(Date.now() + hours * 3600_000).toISOString();
    snooze.mutate({ id, body: { untilUtc: until } });
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Alerts</Title>
        <Group>
          <Button component={Link} to="/alerts/history" variant="light" leftSection={<IconHistory size={16} />}>History</Button>
          <Button leftSection={<IconPlus size={16} />} onClick={() => setOpen(true)}>New alert</Button>
        </Group>
      </Group>
      <Card withBorder>
        {isLoading ? <Loader /> : !data || data.length === 0 ? <Text c="dimmed">No alerts yet.</Text> : (
          <Table striped>
            <Table.Thead><Table.Tr>
              <Table.Th>Type</Table.Th><Table.Th>Symbol</Table.Th><Table.Th>Condition</Table.Th>
              <Table.Th>Threshold</Table.Th><Table.Th>Channels</Table.Th>
              <Table.Th>Status</Table.Th><Table.Th>Triggered</Table.Th><Table.Th></Table.Th>
            </Table.Tr></Table.Thead>
            <Table.Tbody>
              {data.map(a => (
                <Table.Tr key={a.id}>
                  <Table.Td><Badge variant="light">{a.type}</Badge></Table.Td>
                  <Table.Td>{a.symbol}</Table.Td>
                  <Table.Td>{a.condition}</Table.Td>
                  <Table.Td>{a.threshold}</Table.Td>
                  <Table.Td>{a.channels}</Table.Td>
                  <Table.Td>
                    <Badge color={a.status === 'Triggered' ? 'red' : a.status === 'Active' ? 'teal' : 'gray'}>
                      {a.status}
                    </Badge>
                    {a.snoozedUntil && <Text size="xs" c="dimmed">snoozed → {a.snoozedUntil.slice(0, 16).replace('T', ' ')}</Text>}
                  </Table.Td>
                  <Table.Td>{a.triggeredAt ? `${a.triggeredAt.slice(0, 10)} @ ${a.triggeredValue}` : '—'}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      {a.status !== 'Active' && (
                        <Button size="xs" variant="light" onClick={() => reactivate.mutate(a.id)}>Reactivate</Button>
                      )}
                      {a.status === 'Active' && (
                        <ActionIcon variant="subtle" title="Snooze 24h" onClick={() => onSnooze(a.id, 24)}>
                          <IconZzz size={16} />
                        </ActionIcon>
                      )}
                      <ActionIcon color="red" variant="subtle" onClick={() => del.mutate(a.id)}><IconTrash size={16} /></ActionIcon>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal opened={open} onClose={() => setOpen(false)} title="Create alert" size="lg">
        <Stack>
          <Select label="Type" value={type} onChange={(v) => onTypeChange((v as AlertType) ?? 'Price')} data={typeOptions} />
          <TextInput label="Symbol" value={symbol} onChange={(e) => setSymbol(e.currentTarget.value.toUpperCase())} placeholder="AAPL" />
          <Select label="Condition" value={condition} onChange={(v) => setCondition((v as AlertCondition) ?? conditionsByType[type][0].value)} data={conditionsByType[type]} />
          <NumberInput label="Threshold" value={threshold} onChange={setThreshold} decimalScale={4} />
          {type === 'Technical' && (
            <NumberInput label="Indicator period" value={period} onChange={setPeriod} min={2} max={200} />
          )}
          {type === 'Earnings' && (
            <NumberInput label="Days before earnings" value={days} onChange={setDays} min={1} max={60} />
          )}
          {type === 'News' && (
            <>
              <TextInput label="Keyword" value={keyword} onChange={(e) => setKeyword(e.currentTarget.value)} />
              <NumberInput label="Min sentiment (-1..+1)" value={minSent} onChange={setMinSent} min={-1} max={1} decimalScale={2} step={0.1} />
            </>
          )}
          <MultiSelect label="Channels" value={channels} onChange={setChannels} data={channelOptions} />
          {channels.includes('Webhook') && (
            <TextInput label="Webhook URL" value={webhookUrl} onChange={(e) => setWebhookUrl(e.currentTarget.value)} placeholder="https://hooks.example.com/..." />
          )}
          <TextInput label="Note (optional)" value={note} onChange={(e) => setNote(e.currentTarget.value)} />
          <Group justify="flex-end"><Button onClick={submit} loading={create.isPending}>Create</Button></Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
