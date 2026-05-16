import { AppShell, Burger, Group, NavLink, ScrollArea, Title, Avatar, Menu, UnstyledButton, Text } from '@mantine/core';
import { useDisclosure } from '@mantine/hooks';
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
  IconCash
} from '@tabler/icons-react';
import { NavLink as RouterNavLink, Outlet, useNavigate, useParams } from 'react-router-dom';
import { useMsal, useIsAuthenticated } from '@azure/msal-react';
import { isAuthConfigured } from '../../auth/msal';
import { usePortfolios } from '../../api/hooks';

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
  { to: '/alerts', label: 'Alerts', icon: IconBell },
  { to: '/news', label: 'News', icon: IconNews },
  { to: '/earnings', label: 'Earnings', icon: IconCalendarTime },
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
  const { instance, accounts } = useMsal();
  const isAuthed = useIsAuthenticated();
  const navigate = useNavigate();
  const params = useParams();
  const account = accounts[0];
  const { data: portfolios } = usePortfolios();

  // Try to pick the active portfolio id either from the route or the first one.
  const activePortfolioId = params.id ?? portfolios?.[0]?.id;

  return (
    <AppShell
      header={{ height: 56 }}
      navbar={{ width: 240, breakpoint: 'sm', collapsed: { mobile: !opened } }}
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
                  <Avatar radius="xl" size="sm" color="blue">
                    {(account?.name ?? account?.username ?? 'U').slice(0, 1).toUpperCase()}
                  </Avatar>
                  <Text size="sm" visibleFrom="sm">{account?.name ?? account?.username ?? 'Guest'}</Text>
                </Group>
              </UnstyledButton>
            </Menu.Target>
            <Menu.Dropdown>
              <Menu.Item onClick={() => navigate('/settings')}>Settings</Menu.Item>
              {isAuthConfigured && isAuthed ? (
                <Menu.Item leftSection={<IconLogout size={16} />} onClick={() => instance.logoutRedirect()}>Sign out</Menu.Item>
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
    </AppShell>
  );
}
