import { useState } from 'react';
import { Button, Card, Group, Loader, Modal, NumberInput, RingProgress, Select, SimpleGrid, Stack, Text, TextInput, Title, ActionIcon, Badge } from '@mantine/core';
import { DatePickerInput } from '@mantine/dates';
import { IconPlus, IconTrash, IconEdit } from '@tabler/icons-react';
import { LineChart, Line, XAxis, YAxis, Tooltip, ResponsiveContainer, CartesianGrid, Legend } from 'recharts';
import { useCreateGoal, useDeleteGoal, useGoals, usePortfolios, useUpdateGoal } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';
import type { GoalDto, GoalCreateDto } from '../../api/types';

/** M9 #104 — Goals & target-NAV tracking page. */

interface GoalFormState {
  id?: string;
  name: string;
  portfolioId: string | null;
  targetValue: number;
  targetDate: Date | null;
  monthlyContribution: number;
  expectedReturnPct: number;
}

const EMPTY: GoalFormState = {
  name: '',
  portfolioId: null,
  targetValue: 100000,
  targetDate: new Date(Date.now() + 5 * 365 * 86400000),
  monthlyContribution: 500,
  expectedReturnPct: 7,
};

function toIso(d: Date | null): string {
  const dd = d ?? new Date();
  return dd.toISOString().slice(0, 10);
}

function GoalCard({ goal, onEdit, onDelete }: { goal: GoalDto; onEdit: () => void; onDelete: () => void }) {
  const pct = Math.min(100, Math.max(0, goal.progressPercent));
  return (
    <Card withBorder>
      <Group justify="space-between" align="flex-start">
        <div>
          <Group gap="xs"><Title order={5}>{goal.name}</Title>
            <Badge color={goal.onTrack ? 'teal' : 'red'} variant="light">{goal.onTrack ? 'On track' : 'Behind'}</Badge>
          </Group>
          <Text size="xs" c="dimmed">Target: ${goal.targetValue.toLocaleString()} by {goal.targetDate}</Text>
        </div>
        <Group gap={4}>
          <ActionIcon variant="subtle" onClick={onEdit}><IconEdit size={16} /></ActionIcon>
          <ActionIcon variant="subtle" color="red" onClick={onDelete}><IconTrash size={16} /></ActionIcon>
        </Group>
      </Group>
      <Group mt="md" align="center">
        <RingProgress size={120} thickness={12} sections={[{ value: pct, color: goal.onTrack ? 'teal' : 'orange' }]}
          label={<Text ta="center" fw={700} size="sm">{pct.toFixed(0)}%</Text>} />
        <Stack gap={4}>
          <Text size="xs" c="dimmed">Current</Text>
          <Text fw={600}>${goal.currentValue.toLocaleString(undefined, { maximumFractionDigits: 0 })}</Text>
          <Text size="xs" c="dimmed">Projected</Text>
          <Text fw={600}>${goal.projectedFinalValue.toLocaleString(undefined, { maximumFractionDigits: 0 })}</Text>
          {goal.projectedHitDate && <Text size="xs" c="teal">Projected hit: {goal.projectedHitDate}</Text>}
        </Stack>
      </Group>
      {goal.projection.length > 0 && (
        <ResponsiveContainer width="100%" height={180}>
          <LineChart data={goal.projection}>
            <CartesianGrid strokeDasharray="3 3" />
            <XAxis dataKey="date" hide />
            <YAxis />
            <Tooltip />
            <Legend />
            <Line type="monotone" dataKey="projectedValue" name="Projected" stroke="#228be6" dot={false} />
            <Line type="monotone" dataKey="targetTrajectory" name="On-track" stroke="#fa5252" strokeDasharray="4 4" dot={false} />
          </LineChart>
        </ResponsiveContainer>
      )}
    </Card>
  );
}

export function Goals() {
  const { data: goals, isLoading } = useGoals();
  const { data: portfolios } = usePortfolios();
  const create = useCreateGoal();
  const update = useUpdateGoal();
  const del = useDeleteGoal();
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<GoalFormState>(EMPTY);

  const openCreate = () => { setForm(EMPTY); setOpen(true); };
  const openEdit = (g: GoalDto) => {
    setForm({
      id: g.id, name: g.name, portfolioId: g.portfolioId,
      targetValue: g.targetValue, targetDate: new Date(g.targetDate),
      monthlyContribution: g.monthlyContribution,
      expectedReturnPct: g.expectedReturn * 100,
    });
    setOpen(true);
  };

  const submit = async () => {
    const dto: GoalCreateDto = {
      name: form.name,
      portfolioId: form.portfolioId,
      targetValue: form.targetValue,
      targetDate: toIso(form.targetDate),
      monthlyContribution: form.monthlyContribution,
      expectedReturn: form.expectedReturnPct / 100,
    };
    if (form.id) await update.mutateAsync({ id: form.id, dto });
    else await create.mutateAsync(dto);
    setOpen(false);
  };

  return (
    <Stack>
      <Group justify="space-between">
        <Title order={3}>Goals</Title>
        <Button leftSection={<IconPlus size={16} />} onClick={openCreate}>New goal</Button>
      </Group>
      {isLoading ? <Loader /> : !goals || goals.length === 0 ? (
        <EmptyState title="No goals yet" description="Create a savings goal — retirement, house down payment, college — and watch the projection update with your portfolio." />
      ) : (
        <SimpleGrid cols={{ base: 1, md: 2 }}>
          {goals.map(g => (
            <GoalCard key={g.id} goal={g} onEdit={() => openEdit(g)} onDelete={() => del.mutate(g.id)} />
          ))}
        </SimpleGrid>
      )}

      <Modal opened={open} onClose={() => setOpen(false)} title={form.id ? 'Edit goal' : 'New goal'} centered>
        <Stack>
          <TextInput label="Name" required value={form.name} onChange={(e) => setForm({ ...form, name: e.currentTarget.value })} />
          <Select label="Portfolio (optional)" placeholder="All portfolios"
            data={(portfolios ?? []).map(p => ({ value: p.id, label: p.name }))}
            value={form.portfolioId} onChange={(v) => setForm({ ...form, portfolioId: v })} clearable />
          <NumberInput label="Target value ($)" min={1} value={form.targetValue} onChange={(v) => setForm({ ...form, targetValue: typeof v === 'number' ? v : 0 })} thousandSeparator />
          <DatePickerInput label="Target date" value={form.targetDate} onChange={(v) => setForm({ ...form, targetDate: v ? new Date(v) : null })} />
          <NumberInput label="Monthly contribution ($)" min={0} value={form.monthlyContribution} onChange={(v) => setForm({ ...form, monthlyContribution: typeof v === 'number' ? v : 0 })} thousandSeparator />
          <NumberInput label="Expected annual return (%)" min={0} max={50} step={0.5} value={form.expectedReturnPct} onChange={(v) => setForm({ ...form, expectedReturnPct: typeof v === 'number' ? v : 0 })} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setOpen(false)}>Cancel</Button>
            <Button onClick={submit} disabled={!form.name.trim() || form.targetValue <= 0}>{form.id ? 'Save' : 'Create'}</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
