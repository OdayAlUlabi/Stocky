import { useState } from "react";
import {
  Alert, Badge, Box, Button, Card, Code, CopyButton, Group, Modal, PasswordInput, Stack,
  Table, Text, TextInput, Title, Tooltip
} from "@mantine/core";
import { IconCheck, IconCopy, IconKey } from "@tabler/icons-react";
import { useApiKeys, useCreateApiKey, useRevokeApiKey } from "../../api/hooks";

export function ApiKeys() {
  const { data, isLoading } = useApiKeys();
  const create = useCreateApiKey();
  const revoke = useRevokeApiKey();
  const [opened, setOpened] = useState(false);
  const [name, setName] = useState("");
  const [createdSecret, setCreatedSecret] = useState<string | null>(null);

  async function handleCreate() {
    if (!name.trim()) return;
    const res = await create.mutateAsync({ name: name.trim(), scopes: "read" });
    setCreatedSecret(res.plaintext);
    setName("");
  }

  return (
    <Stack p="md" gap="md">
      <Group justify="space-between" align="center">
        <Group gap="xs"><IconKey size={20} /><Title order={3}>API keys</Title></Group>
        <Button onClick={() => { setOpened(true); setCreatedSecret(null); }}>New key</Button>
      </Group>
      <Text size="sm" c="dimmed">
        Use these bearer keys with the public REST API at <Code>/v1/public/...</Code>. Keys grant
        read-only access to your portfolios, holdings, and transactions. Treat them like passwords —
        Stocky never displays a key's secret again after it is created.
      </Text>
      <Card withBorder>
        <Table verticalSpacing="sm">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Name</Table.Th>
              <Table.Th>Prefix</Table.Th>
              <Table.Th>Scopes</Table.Th>
              <Table.Th>Created</Table.Th>
              <Table.Th>Last used</Table.Th>
              <Table.Th>Status</Table.Th>
              <Table.Th></Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {isLoading && (
              <Table.Tr><Table.Td colSpan={7}><Text size="sm" c="dimmed">Loadingâ€¦</Text></Table.Td></Table.Tr>
            )}
            {!isLoading && (data ?? []).length === 0 && (
              <Table.Tr><Table.Td colSpan={7}><Text size="sm" c="dimmed">No API keys yet.</Text></Table.Td></Table.Tr>
            )}
            {(data ?? []).map(k => (
              <Table.Tr key={k.id}>
                <Table.Td>{k.name}</Table.Td>
                <Table.Td><Code>{k.prefix}â€¦</Code></Table.Td>
                <Table.Td>{k.scopes}</Table.Td>
                <Table.Td>{new Date(k.createdAt).toLocaleDateString()}</Table.Td>
                <Table.Td>{k.lastUsedAt ? new Date(k.lastUsedAt).toLocaleString() : "â€”"}</Table.Td>
                <Table.Td>
                  {k.isActive
                    ? <Badge color="green" variant="light">Active</Badge>
                    : <Badge color="gray" variant="light">{k.revokedAt ? "Revoked" : "Expired"}</Badge>}
                </Table.Td>
                <Table.Td>
                  {k.isActive && (
                    <Button size="xs" color="red" variant="subtle"
                      onClick={() => revoke.mutate(k.id)}>Revoke</Button>
                  )}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>
      </Card>

      <Modal opened={opened} onClose={() => setOpened(false)} title="New API key" size="lg">
        {!createdSecret ? (
          <Stack>
            <TextInput
              label="Name"
              placeholder="e.g. Personal scripts"
              value={name}
              onChange={e => setName(e.currentTarget.value)}
              autoFocus
            />
            <Group justify="flex-end">
              <Button variant="default" onClick={() => setOpened(false)}>Cancel</Button>
              <Button onClick={handleCreate} loading={create.isPending}>Create</Button>
            </Group>
          </Stack>
        ) : (
          <Stack>
            <Alert color="yellow" variant="light">
              Copy this key now â€” it will not be shown again.
            </Alert>
            <Box>
              <PasswordInput value={createdSecret} readOnly visible label="Secret" />
            </Box>
            <Group justify="space-between">
              <CopyButton value={createdSecret} timeout={2000}>
                {({ copied, copy }) => (
                  <Tooltip label={copied ? "Copied" : "Copy"} withArrow>
                    <Button leftSection={copied ? <IconCheck size={16} /> : <IconCopy size={16} />}
                      onClick={copy} variant="light">
                      {copied ? "Copied" : "Copy"}
                    </Button>
                  </Tooltip>
                )}
              </CopyButton>
              <Button onClick={() => { setOpened(false); setCreatedSecret(null); }}>Done</Button>
            </Group>
          </Stack>
        )}
      </Modal>
    </Stack>
  );
}
