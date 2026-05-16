import { ActionIcon, Anchor, Badge, Button, Card, CopyButton, Group, Loader, Modal, Stack, Switch, Table, Text, TextInput, Title } from '@mantine/core';
import { useMemo, useState } from 'react';
import { useCreateShareToken, useDeleteShareToken, usePortfolios, useRevokeShareToken, useShareTokens } from '../../api/hooks';
import type { CreateShareTokenRequest } from '../../api/types';

/**
 * M11 #54 — Manage revocable read-only share links for portfolios.
 */
export function ShareLinks() {
  const { data: portfolios } = usePortfolios();
  const { data: tokens, isLoading } = useShareTokens();
  const create = useCreateShareToken();
  const revoke = useRevokeShareToken();
  const del = useDeleteShareToken();

  const [open, setOpen] = useState(false);
  const [justCreated, setJustCreated] = useState<{ url: string; token: string } | null>(null);
  const [form, setForm] = useState<CreateShareTokenRequest>({
    portfolioId: '',
    label: '',
    expiresAt: '',
    includeTransactions: false,
    includeCostBasis: false,
  });

  const portfolioMap = useMemo(() => Object.fromEntries((portfolios ?? []).map(p => [p.id, p.name])), [portfolios]);

  const submit = async () => {
    if (!form.portfolioId) return;
    const created = await create.mutateAsync({
      portfolioId: form.portfolioId,
      label: form.label || null,
      expiresAt: form.expiresAt ? new Date(form.expiresAt).toISOString() : null,
      includeTransactions: form.includeTransactions,
      includeCostBasis: form.includeCostBasis,
    });
    setOpen(false);
    setForm({ portfolioId: '', label: '', expiresAt: '', includeTransactions: false, includeCostBasis: false });
    if (created?.token && created?.shareUrl) {
      setJustCreated({ url: created.shareUrl, token: created.token });
    }
  };

  return (
    <Stack>
      <Group justify="space-between">
        <div>
          <Title order={3}>Share links</Title>
          <Text c="dimmed" size="sm">Generate read-only URLs to share a portfolio with an advisor. Revoke anytime.</Text>
        </div>
        <Button onClick={() => setOpen(true)} disabled={!(portfolios && portfolios.length)}>New share link</Button>
      </Group>

      <Card withBorder>
        {isLoading ? <Loader /> : (!tokens || tokens.length === 0) ? (
          <Text c="dimmed">No share links yet.</Text>
        ) : (
          <Table striped highlightOnHover>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Label</Table.Th>
                <Table.Th>Portfolio</Table.Th>
                <Table.Th>Created</Table.Th>
                <Table.Th>Expires</Table.Th>
                <Table.Th>Status</Table.Th>
                <Table.Th>Views</Table.Th>
                <Table.Th>Last viewed</Table.Th>
                <Table.Th>URL</Table.Th>
                <Table.Th />
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {tokens.map(t => (
                <Table.Tr key={t.id}>
                  <Table.Td>{t.label ?? <Text span c="dimmed">—</Text>}</Table.Td>
                  <Table.Td>{portfolioMap[t.portfolioId] ?? t.portfolioId.slice(0, 8)}</Table.Td>
                  <Table.Td>{new Date(t.createdAt).toLocaleString()}</Table.Td>
                  <Table.Td>{t.expiresAt ? new Date(t.expiresAt).toLocaleString() : '—'}</Table.Td>
                  <Table.Td>
                    {t.revokedAt
                      ? <Badge color="red" variant="light">Revoked</Badge>
                      : t.isActive
                        ? <Badge color="teal" variant="light">Active</Badge>
                        : <Badge color="gray" variant="light">Expired</Badge>}
                  </Table.Td>
                  <Table.Td>{t.viewCount}</Table.Td>
                  <Table.Td>{t.lastViewedAt ? new Date(t.lastViewedAt).toLocaleString() : '—'}</Table.Td>
                  <Table.Td>
                    <Group gap={4} wrap="nowrap">
                      <Text size="xs" c="dimmed" ff="monospace">{t.tokenPrefix}…</Text>
                    </Group>
                  </Table.Td>
                  <Table.Td>
                    <Group gap="xs" justify="flex-end">
                      {t.isActive && (
                        <Button size="xs" variant="light" color="orange" onClick={() => revoke.mutate(t.id)}>Revoke</Button>
                      )}
                      <ActionIcon variant="subtle" color="red" onClick={() => { if (confirm('Delete share link permanently?')) del.mutate(t.id); }}>×</ActionIcon>
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        )}
      </Card>

      <Modal opened={!!justCreated} onClose={() => setJustCreated(null)} title="Copy this link — it won't be shown again" centered>
        <Stack>
          <Text size="sm" c="dimmed">
            For security, share-link URLs are stored hashed. This is your only opportunity to copy it.
          </Text>
          {justCreated && (
            <Group gap="xs" wrap="nowrap">
              <TextInput readOnly value={justCreated.url} style={{ flex: 1 }} />
              <CopyButton value={justCreated.url}>
                {({ copied, copy }) => <Button onClick={copy}>{copied ? 'Copied' : 'Copy'}</Button>}
              </CopyButton>
              <Anchor href={justCreated.url} target="_blank" rel="noreferrer">open</Anchor>
            </Group>
          )}
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setJustCreated(null)}>Done</Button>
          </Group>
        </Stack>
      </Modal>

      <Modal opened={open} onClose={() => setOpen(false)} title="New share link" centered>
        <Stack>
          <TextInput label="Label (optional)" placeholder="Advisor — Jane" value={form.label ?? ''} onChange={(e) => setForm({ ...form, label: e.currentTarget.value })} />
          <TextInput
            label="Portfolio"
            placeholder="(select)"
            value={portfolioMap[form.portfolioId] ?? ''}
            onChange={() => { /* readonly display; pick from below */ }}
            disabled
          />
          <select
            value={form.portfolioId}
            onChange={(e) => setForm({ ...form, portfolioId: e.target.value })}
            style={{ padding: 6, borderRadius: 4, border: '1px solid #ced4da' }}
          >
            <option value="">— pick a portfolio —</option>
            {(portfolios ?? []).map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
          <TextInput
            label="Expires (optional)"
            type="datetime-local"
            value={form.expiresAt ?? ''}
            onChange={(e) => setForm({ ...form, expiresAt: e.currentTarget.value })}
          />
          <Switch label="Include transactions" checked={!!form.includeTransactions} onChange={(e) => setForm({ ...form, includeTransactions: e.currentTarget.checked })} />
          <Switch label="Include cost basis / P&L" checked={!!form.includeCostBasis} onChange={(e) => setForm({ ...form, includeCostBasis: e.currentTarget.checked })} />
          <Group justify="flex-end">
            <Button variant="default" onClick={() => setOpen(false)}>Cancel</Button>
            <Button onClick={submit} loading={create.isPending} disabled={!form.portfolioId}>Create</Button>
          </Group>
        </Stack>
      </Modal>
    </Stack>
  );
}
