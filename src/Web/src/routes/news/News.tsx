import { Anchor, Badge, Card, Group, Loader, Stack, Text, Title } from '@mantine/core';
import { useNews } from '../../api/hooks';
import { EmptyState } from '../../components/EmptyState';

export function News() {
  const { data, isLoading } = useNews();
  if (isLoading) return <Loader />;
  if (!data || data.length === 0) return <EmptyState title="No news" description="Add holdings or watchlist symbols to see headlines." />;
  return (
    <Stack>
      <Title order={3}>News</Title>
      {data.map(n => (
        <Card key={n.id} withBorder>
          <Group justify="space-between">
            <div>
              <Text fw={600}>{n.headline}</Text>
              {n.summary && <Text size="sm" c="dimmed" mt={4}>{n.summary}</Text>}
              <Group gap="xs" mt="xs">
                <Badge variant="light">{n.source}</Badge>
                {n.symbol && <Badge variant="outline">{n.symbol}</Badge>}
                <Badge variant="light" color="grape">{n.category}</Badge>
                <Text size="xs" c="dimmed">{new Date(n.publishedAt).toLocaleString()}</Text>
              </Group>
            </div>
            {n.url && <Anchor href={n.url} target="_blank" rel="noreferrer">Read</Anchor>}
          </Group>
        </Card>
      ))}
    </Stack>
  );
}
