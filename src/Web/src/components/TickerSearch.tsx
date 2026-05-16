import { Autocomplete, Loader } from '@mantine/core';
import { useState } from 'react';
import { useSecuritySearch } from '../api/hooks';
import type { InstrumentDto } from '../api/types';

interface TickerSearchProps {
  value: string | null;
  onSelect: (instrument: InstrumentDto) => void;
  label?: string;
  placeholder?: string;
  required?: boolean;
  error?: string;
}

export function TickerSearch({ value, onSelect, label = 'Symbol', placeholder = 'Search e.g. AAPL', required, error }: TickerSearchProps) {
  const [text, setText] = useState(value ?? '');
  const { data, isFetching } = useSecuritySearch(text);

  const items = (data ?? []).map((i) => ({ value: i.symbol, label: `${i.symbol} — ${i.name}`, raw: i }));

  return (
    <Autocomplete
      label={label}
      placeholder={placeholder}
      required={required}
      error={error}
      value={text}
      onChange={setText}
      data={items}
      onOptionSubmit={(symbol) => {
        const match = items.find((i) => i.value === symbol);
        if (match) {
          setText(match.value);
          onSelect(match.raw);
        }
      }}
      rightSection={isFetching ? <Loader size="xs" /> : null}
      limit={10}
    />
  );
}
