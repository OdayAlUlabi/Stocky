import { Button, Card, Group, Loader, Select, Stack, Switch, TextInput, Title } from '@mantine/core';
import { useEffect, useState } from 'react';
import { useSettings, useUpdateSettings } from '../../api/hooks';

export function Settings() {
  const { data, isLoading } = useSettings();
  const update = useUpdateSettings();
  const [displayCurrency, setDisplayCurrency] = useState('USD');
  const [theme, setTheme] = useState('light');
  const [locale, setLocale] = useState('en-US');
  const [emailAlerts, setEmailAlerts] = useState(true);
  const [weeklyDigest, setWeeklyDigest] = useState(false);

  useEffect(() => {
    if (data) {
      setDisplayCurrency(data.displayCurrency);
      setTheme(data.theme);
      setLocale(data.locale);
      setEmailAlerts(data.emailAlerts);
      setWeeklyDigest(data.weeklyDigest);
    }
  }, [data]);

  if (isLoading) return <Loader />;

  return (
    <Stack maw={520}>
      <Title order={3}>Settings</Title>
      <Card withBorder>
        <Stack>
          <Select label="Display currency" value={displayCurrency} onChange={(v) => setDisplayCurrency(v ?? 'USD')}
            data={['USD', 'EUR', 'GBP', 'CAD', 'AUD', 'JPY', 'CHF']} />
          <Select label="Theme" value={theme} onChange={(v) => setTheme(v ?? 'light')} data={['light', 'dark', 'auto']} />
          <TextInput label="Locale" value={locale} onChange={(e) => setLocale(e.currentTarget.value)} />
          <Switch label="Email alerts" checked={emailAlerts} onChange={(e) => setEmailAlerts(e.currentTarget.checked)} />
          <Switch label="Weekly digest" checked={weeklyDigest} onChange={(e) => setWeeklyDigest(e.currentTarget.checked)} />
          <Group justify="flex-end">
            <Button loading={update.isPending} onClick={() => update.mutate({ displayCurrency, theme, locale, emailAlerts, weeklyDigest })}>Save</Button>
          </Group>
        </Stack>
      </Card>
    </Stack>
  );
}
