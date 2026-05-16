import { useState } from 'react';
import { Alert, Button, Card, Group, Stack, Text, TextInput, Title } from '@mantine/core';
import { IconAlertTriangle, IconDownload, IconUserCog, IconTrash } from '@tabler/icons-react';
import { useExportAccount, useDeleteAccount } from '../../api/hooks';
import { useNavigate } from 'react-router-dom';

export function AccountSettings() {
  const exportMut = useExportAccount();
  const deleteMut = useDeleteAccount();
  const navigate = useNavigate();
  const [confirmText, setConfirmText] = useState('');

  const onExport = async () => {
    const data = await exportMut.mutateAsync();
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `stocky-export-${new Date().toISOString().slice(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const onDelete = async () => {
    if (confirmText !== 'DELETE') return;
    await deleteMut.mutateAsync();
    navigate('/login');
  };

  return (
    <Stack maw={720}>
      <Title order={3}><IconUserCog size={20} /> Account</Title>

      <Card withBorder padding="md">
        <Stack gap="xs">
          <Title order={5}>Export your data</Title>
          <Text size="sm" c="dimmed">
            Download a JSON archive of your portfolios, holdings, transactions, watchlists, alerts, notes, goals, and settings.
            Complies with GDPR data-portability requirements.
          </Text>
          <Group>
            <Button leftSection={<IconDownload size={16} />} onClick={onExport} loading={exportMut.isPending}>
              Download JSON export
            </Button>
          </Group>
        </Stack>
      </Card>

      <Card withBorder padding="md">
        <Stack gap="xs">
          <Title order={5} c="red">Delete account</Title>
          <Alert color="red" icon={<IconAlertTriangle />}>
            This permanently removes all your portfolios, holdings, transactions, watchlists, alerts, notes, goals, and settings.
            This action cannot be undone. A single audit entry is retained for compliance.
          </Alert>
          <Text size="sm">Type <strong>DELETE</strong> below to confirm:</Text>
          <TextInput value={confirmText} onChange={(e) => setConfirmText(e.currentTarget.value)} placeholder="DELETE" />
          <Group>
            <Button
              color="red"
              leftSection={<IconTrash size={16} />}
              disabled={confirmText !== 'DELETE'}
              loading={deleteMut.isPending}
              onClick={onDelete}
            >
              Permanently delete my account
            </Button>
          </Group>
        </Stack>
      </Card>
    </Stack>
  );
}
