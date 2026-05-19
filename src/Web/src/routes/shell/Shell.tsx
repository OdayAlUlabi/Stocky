import { ActionIcon, AppShell, Burger, Group, NavLink, ScrollArea, Title, Avatar, Menu, UnstyledButton, Text, useMantineColorScheme, useComputedColorScheme, Tooltip } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
import { useEffect, useRef } from 'react';
import {
  IconChartPie,
  IconBriefcase,
  IconStar,
  IconLogout,
  IconBell,
  IconReportAnalytics,
  IconNews,
  IconCalendarTime,
  IconChartLine,
  IconChartDonut,
  IconSettings,
  IconCash,
  IconSearch,
  IconNotes,
  IconHistory,
  IconLayoutGrid,
  IconUserCog,
  IconKey,
  IconSun,
  IconMoon
} from '@tabler/icons-react';
import { NavLink as RouterNavLink, Outlet, useNavigate, useParams } from 'react-router-dom';
import { useGoogleAuth, isAuthConfigured } from '../../auth/googleAuth';
import { usePortfolios, useSettings, useUpdateSettings } from '../../api/hooks';

interface NavItem {
  to: string;
  label: string;
  icon: typeof IconChartPie;
  end?: boolean;
  portfolioScoped?: boolean;
}

const topItems: NavItem[] = [
  { to: '/', label: 'Dashboard', icon: IconChartPie, end: true },
  { to: '/portfolios', label: 'Portfolios', icon: IconBriefcase },
  { to: '/watchlist', label: 'Watchlist', icon: IconStar },
  { to: '/screener', label: 'Screener', icon: IconSearch },
  { to: '/alerts', label: 'Alerts', icon: IconBell },
  { to: '/news', label: 'News', icon: IconNews },
  { to: '/earnings', label: 'Earnings', icon: IconCalendarTime },
  { to: '/calendar/economic', label: 'Econ calendar', icon: IconCalendarTime },
  { to: '/calendar/earnings', label: 'Earnings calendar', icon: IconCalendarTime },
  { to: '/goals', label: 'Goals', icon: IconCash },
  { to: '/cash', label: 'Cash', icon: IconCash },
  { to: '/notes', label: 'Notes', icon: IconNotes },
  { to: '/templates', label: 'Templates', icon: IconLayoutGrid },
  { to: '/reports/schedules', label: 'Scheduled reports', icon: IconReportAnalytics },
  { to: '/reports/share', label: 'Share links', icon: IconReportAnalytics },
  { to: '/admin/audit', label: 'Audit log', icon: IconHistory },
  { to: '/account', label: 'Account', icon: IconUserCog },
  { to: '/account/api-keys', label: 'API keys', icon: IconKey },
  { to: '/settings', label: 'Settings', icon: IconSettings }
];

const portfolioItems: { to: (id: string) => string; label: string; icon: typeof IconChartPie }[] = [
  { to: (id) => `/portfolios/${id}/performance`, label: 'Performance', icon: IconChartLine },
  { to: (id) => `/portfolios/${id}/allocation`, label: 'Allocation', icon: IconChartDonut },
  { to: (id) => `/portfolios/${id}/reports`, label: 'Reports', icon: IconReportAnalytics },
  { to: (id) => `/portfolios/${id}/capital-gains`, label: 'Cap gains', icon: IconCash }
];

export function Shell() {
  const [opened, { toggle }] = useDisclosure();
  const { user, isAuthenticated: isAuthed, signOut } = useGoogleAuth();
  const navigate = useNavigate();
  const params = useParams();
  const { data: portfolios } = usePortfolios();
  const { setColorScheme } = useMantineColorScheme();
  const computedScheme = useComputedColorScheme('light', { getInitialValueInEffect: true });
  const isDark = computedScheme === 'dark';

  // M14 #101 — sync server-persisted theme with the Mantine color scheme.
  const { data: settings } = useSettings();
  const updateSettings = useUpdateSettings();
  const themeApplied = useRef(false);
  useEffect(() => {
    if (settings?.theme && !themeApplied.current) {
      themeApplied.current = true;
      setColorScheme(settings.theme as 'light' | 'dark' | 'auto');
    }
  }, [settings?.theme, setColorScheme]);

  function toggleScheme() {
    const next = isDark ? 'light' : 'dark';
    setColorScheme(next);
    if (settings) {
      updateSettings.mutate({ ...settings, theme: next });
    }
  }

  // Try to pick the active portfolio id either from the route or the first one.
  const activePortfolioId = params.id ?? portfolios?.[0]?.id;

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 240, breakpoint: 'sm', collapsed: { mobile: !opened } }}
      footer={{ height: 56, collapsed: false }}
      padding="md"
    >
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between">
          <Group>
            <Burger opened={opened} onClick={toggle} hiddenFrom="sm" size="sm" />
            <Title order={4}>Stocky</Title>
          </Group>
          <Menu position="bottom-end" withinPortal>
            <Menu.Target>
              <UnstyledButton>
                <Group gap="xs">
                  <Tooltip label={isDark ? 'Switch to light' : 'Switch to dark'} withArrow>
                    <ActionIcon
                      component="div"
                      variant="default"
                      size="lg"
                      onClick={(e) => { e.stopPropagation(); toggleScheme(); }}
                      aria-label="Toggle color scheme"
                    >
                      {isDark ? <IconSun size={16} /> : <IconMoon size={16} />}
                    </ActionIcon>
                  </Tooltip>
                  <Avatar
                    radius="xl"
                    size="sm"
                    src={user?.picture}
                    color="blue"
                  >
                    {(user?.name ?? 'U').slice(0, 1).toUpperCase()}
                  </Avatar>
                  <Text size="sm" visibleFrom="sm">{user?.name ?? user?.email ?? 'Guest'}</Text>
                </Group>
              </UnstyledButton>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item onClick={() => navigate('/settings')}>Settings</Menu.Item>
              {isAuthConfigured && isAuthed ? (
                <Menu.Item leftSection={<IconLogout size={16} />} onClick={signOut}>Sign out</Menu.Item>
              ) : (
                <Menu.Item onClick={() => navigate('/login')}>Sign in</Menu.Item>
              )}
            </Menu.Dropdown>
          </Menu>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="sm">
        <ScrollArea>
          {topItems.map((it) => (
            <NavLink
              key={it.to}
              component={RouterNavLink}
              to={it.to}
              end={it.end}
              label={it.label}
              leftSection={<it.icon size={18} stroke={1.5} />}
            />
          ))}
          {activePortfolioId && (
            <>
              <Text size="xs" c="dimmed" mt="md" mb="xs" px="xs">PORTFOLIO TOOLS</Text>
              {portfolioItems.map((it) => (
                <NavLink
                  key={it.label}
                  component={RouterNavLink}
                  to={it.to(activePortfolioId)}
                  label={it.label}
                  leftSection={<it.icon size={18} stroke={1.5} />}
                />
              ))}
            </>
          )}
        </ScrollArea>
      </AppShell.Navbar>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
      {/* M14 #90 — mobile bottom tab bar */}
      <AppShell.Footer hiddenFrom="sm" p={4} withBorder>
        <Group justify="space-around" gap={0} wrap="nowrap" h="100%">
          {([
            { to: '/', label: 'Home', icon: IconChartPie, end: true as const },
            { to: '/portfolios', label: 'Portfolios', icon: IconBriefcase, end: false as const },
            { to: '/watchlist', label: 'Watch', icon: IconStar, end: false as const },
            { to: '/alerts', label: 'Alerts', icon: IconBell, end: false as const },
            { to: '/settings', label: 'More', icon: IconSettings, end: false as const }
          ]).map(it => (
            <RouterNavLink
              key={it.to}
              to={it.to}
              end={it.end}
              style={({ isActive }) => ({
                flex: 1,
                textAlign: 'center',
                padding: '6px 0',
                color: isActive ? 'var(--mantine-color-blue-6)' : 'var(--mantine-color-dimmed)',
                textDecoration: 'none'
              })}
            >
              <Group justify="center" gap={2} wrap="nowrap" style={{ flexDirection: 'column' }}>
                <it.icon size={20} />
                <Text size="xs">{it.label}</Text>
              </Group>
            </RouterNavLink>
          ))}
        </Group>
      </AppShell.Footer>
    </AppShell>
  );
}
