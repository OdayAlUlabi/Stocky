import { Button, Drawer, Group, NumberInput, Select, Stack, Textarea, TextInput, Text } from '@mantine/core';
import { DateTimePicker } from '@mantine/dates';
import { useEffect, useState } from 'react';
import { notifications } from '@mantine/notifications';
import dayjs from 'dayjs';
import { TickerSearch } from '../../components/TickerSearch';
import type { CreateTransactionRequest, TransactionDto, TransactionType } from '../../api/types';
import { useCreateTransaction, useUpdateTransaction } from '../../api/hooks';

interface TradeDrawerProps {
  portfolioId: string;
  opened: boolean;
  onClose: () => void;
  editing: TransactionDto | null;
  defaultCurrency: string;
}

const TYPES: TransactionType[] = ['Buy', 'Sell', 'Dividend', 'Deposit', 'Withdrawal', 'Split'];

export function TradeDrawer({ portfolioId, opened, onClose, editing, defaultCurrency }: TradeDrawerProps) {
  const create = useCreateTransaction(portfolioId);
  const update = useUpdateTransaction(portfolioId);

  const [type, setType] = useState<TransactionType>('Buy');
  const [symbol, setSymbol] = useState<string | null>(null);
  const [quantity, setQuantity] = useState<number | string>('');
  const [price, setPrice] = useState<number | string>('');
  const [fee, setFee] = useState<number | string>(0);
  const [currency, setCurrency] = useState(defaultCurrency);
  const [executedAt, setExecutedAt] = useState<Date | null>(new Date());
  const [notes, setNotes] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!opened) return;
    if (editing) {
      setType(editing.type);
      setSymbol(editing.symbol);
      setQuantity(editing.quantity);
      setPrice(editing.price);
      setFee(editing.fee);
      setCurrency(editing.currency);
      setExecutedAt(dayjs(editing.executedAt).toDate());
      setNotes(editing.notes ?? '');
    } else {
      setType('Buy');
      setSymbol(null);
      setQuantity('');
      setPrice('');
      setFee(0);
      setCurrency(defaultCurrency);
      setExecutedAt(new Date());
      setNotes('');
    }
    setError(null);
  }, [opened, editing, defaultCurrency]);

  const symbolRequired = type === 'Buy' || type === 'Sell' || type === 'Dividend' || type === 'Split';

  const submit = async () => {
    setError(null);
    const qty = typeof quantity === 'number' ? quantity : parseFloat(quantity);
    const px = typeof price === 'number' ? price : parseFloat(price);
    const fe = typeof fee === 'number' ? fee : parseFloat(String(fee));

    if (!qty || qty <= 0) { setError('Quantity must be greater than zero.'); return; }
    if (Number.isNaN(px) || px < 0) { setError('Price must be zero or more.'); return; }
    if (Number.isNaN(fe) || fe < 0) { setError('Fee must be zero or more.'); return; }
    if (symbolRequired && !symbol) { setError('Symbol is required for this trade type.'); return; }
    if (!executedAt) { setError('Executed date is required.'); return; }

    const body: CreateTransactionRequest = {
      symbol: symbol ?? null,
      type,
      quantity: qty,
      price: Number.isNaN(px) ? 0 : px,
      fee: Number.isNaN(fe) ? 0 : fe,
      currency: currency.toUpperCase() || defaultCurrency,
      executedAt: executedAt.toISOString(),
      notes: notes.trim() ? notes.trim() : null
    };

    try {
      if (editing) await update.mutateAsync({ id: editing.id, body });
      else await create.mutateAsync(body);
      notifications.show({ message: editing ? 'Trade updated' : 'Trade logged', color: 'teal' });
      onClose();
    } catch (e) {
      const msg = (e as Error).message;
      setError(msg);
      notifications.show({ message: msg, color: 'red' });
    }
  };

  const busy = create.isPending || update.isPending;

  return (
    <Drawer opened={opened} onClose={onClose} position="right" size="md" title={editing ? 'Edit trade' : 'Add trade'}>
      <Stack>
        <Select
          label="Type"
          data={TYPES.map((t) => ({ value: t, label: t }))}
          value={type}
          onChange={(v) => v && setType(v as TransactionType)}
          allowDeselect={false}
        />
        {symbolRequired && (
          <TickerSearch value={symbol} onSelect={(i) => { setSymbol(i.symbol); if (i.currency) setCurrency(i.currency); }} required />
        )}
        <NumberInput label="Quantity" value={quantity} onChange={setQuantity} min={0} decimalScale={8} thousandSeparator="," />
        <NumberInput label="Price per unit" value={price} onChange={setPrice} min={0} decimalScale={8} thousandSeparator="," />
        <NumberInput label="Fee" value={fee} onChange={setFee} min={0} decimalScale={4} thousandSeparator="," />
        <TextInput label="Currency" value={currency} onChange={(e) => setCurrency(e.currentTarget.value.toUpperCase())} maxLength={3} />
        <DateTimePicker label="Executed at" value={executedAt} onChange={(v) => setExecutedAt(v ? new Date(v) : null)} clearable={false} />
        <Textarea label="Notes" value={notes} onChange={(e) => setNotes(e.currentTarget.value)} autosize minRows={2} />
        {error && <Text c="red" size="sm">{error}</Text>}
        <Group justify="flex-end">
          <Button variant="default" onClick={onClose} disabled={busy}>Cancel</Button>
          <Button onClick={submit} loading={busy}>{editing ? 'Save' : 'Add trade'}</Button>
        </Group>
      </Stack>
    </Drawer>
  );
}
