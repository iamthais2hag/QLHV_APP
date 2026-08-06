import {
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';

export type LookupKey = string | number;

export interface SearchLookupProps<T, TKey extends LookupKey> {
  id: string;
  label: string;
  value: T | null;
  onChange: (value: T | null) => void;
  loadOptions: (keyword: string, signal: AbortSignal) => Promise<readonly T[]>;
  getKey: (option: T) => TKey;
  getLabel: (option: T) => string;
  getDescription?: (option: T) => string | null | undefined;
  inputValue?: string;
  onInputValueChange?: (value: string) => void;
  placeholder?: string;
  loadingText?: string;
  emptyText?: string;
  errorText?: string;
  clearLabel?: string;
  disabled?: boolean;
  readOnly?: boolean;
  required?: boolean;
  debounceMs?: number;
  maxResults?: number;
  minQueryLength?: number;
  className?: string;
  renderOption?: (option: T) => ReactNode;
}

type LookupStatus = 'idle' | 'loading' | 'success' | 'error';

interface DropdownPosition {
  left: number;
  width: number;
  top?: number;
  bottom?: number;
  maxHeight: number;
}

export default function SearchLookup<T, TKey extends LookupKey>({
  id,
  label,
  value,
  onChange,
  loadOptions,
  getKey,
  getLabel,
  getDescription,
  inputValue,
  onInputValueChange,
  placeholder,
  loadingText = 'Đang tìm...',
  emptyText = 'Không có kết quả',
  errorText = 'Không tải được dữ liệu tra cứu.',
  clearLabel,
  disabled = false,
  readOnly = false,
  required = false,
  debounceMs = 200,
  maxResults = 20,
  minQueryLength = 1,
  className = '',
  renderOption,
}: SearchLookupProps<T, TKey>) {
  const generatedId = useId().replace(/:/g, '');
  const listboxId = `${id}-${generatedId}-listbox`;
  const statusId = `${id}-${generatedId}-status`;
  const [internalInput, setInternalInput] = useState(() => value ? getLabel(value) : '');
  const [options, setOptions] = useState<readonly T[]>(() => value ? [value] : []);
  const [status, setStatus] = useState<LookupStatus>('idle');
  const [open, setOpen] = useState(false);
  const [focused, setFocused] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const [position, setPosition] = useState<DropdownPosition | null>(null);
  const wrapperRef = useRef<HTMLDivElement | null>(null);
  const menuRef = useRef<HTMLDivElement | null>(null);
  const requestSequence = useRef(0);
  const loadOptionsRef = useRef(loadOptions);
  const inputValueChangeRef = useRef(onInputValueChange);
  const valueKey = value === null ? null : getKey(value);
  const selectedLabel = value === null ? '' : getLabel(value);
  const controlledInput = inputValue !== undefined;
  const currentInput = controlledInput ? inputValue : internalInput;
  const canInteract = !disabled && !readOnly;

  useEffect(() => {
    loadOptionsRef.current = loadOptions;
    inputValueChangeRef.current = onInputValueChange;
  }, [loadOptions, onInputValueChange]);

  useEffect(() => {
    if (value === null) return;
    if (!controlledInput) setInternalInput(selectedLabel);
    if (inputValue !== undefined && inputValue !== selectedLabel) {
      inputValueChangeRef.current?.(selectedLabel);
    }
    setOptions((current) => current.length === 1 && String(getKey(current[0])) === String(valueKey) ? current : [value]);
  }, [controlledInput, inputValue, selectedLabel, value, valueKey]);

  useEffect(() => {
    if (!focused || !canInteract) return undefined;
    const keyword = currentInput.trim();
    if (value !== null && keyword === selectedLabel.trim()) {
      requestSequence.current += 1;
      setOptions([value]);
      setStatus('success');
      return undefined;
    }
    if (keyword.length < minQueryLength) {
      requestSequence.current += 1;
      setOptions([]);
      setStatus('idle');
      setOpen(false);
      return undefined;
    }

    const sequence = ++requestSequence.current;
    const controller = new AbortController();
    setStatus('loading');
    setOpen(true);
    setActiveIndex(-1);
    const timer = window.setTimeout(async () => {
      try {
        const result = await loadOptionsRef.current(keyword, controller.signal);
        if (controller.signal.aborted || sequence !== requestSequence.current) return;
        setOptions(result.slice(0, maxResults));
        setStatus('success');
        setOpen(true);
      } catch {
        if (controller.signal.aborted || sequence !== requestSequence.current) return;
        setOptions([]);
        setStatus('error');
        setOpen(true);
      }
    }, debounceMs);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [canInteract, currentInput, debounceMs, focused, maxResults, minQueryLength, selectedLabel, value]);

  useEffect(() => {
    if (!open) return undefined;
    function closeOnOutsidePointer(event: PointerEvent) {
      const target = event.target as Node;
      if (!wrapperRef.current?.contains(target) && !menuRef.current?.contains(target)) {
        setOpen(false);
        setActiveIndex(-1);
      }
    }
    document.addEventListener('pointerdown', closeOnOutsidePointer);
    return () => document.removeEventListener('pointerdown', closeOnOutsidePointer);
  }, [open]);

  useLayoutEffect(() => {
    if (!open) return undefined;
    function updatePosition() {
      const rect = wrapperRef.current?.getBoundingClientRect();
      if (!rect) return;
      const margin = 8;
      const desiredHeight = 240;
      const availableBelow = window.innerHeight - rect.bottom - margin;
      const availableAbove = rect.top - margin;
      const width = Math.max(220, Math.min(rect.width, window.innerWidth - margin * 2));
      const left = Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin));
      if (availableBelow < 140 && availableAbove > availableBelow) {
        setPosition({ left, width, bottom: window.innerHeight - rect.top + 4, maxHeight: Math.min(desiredHeight, availableAbove) });
      } else {
        setPosition({ left, width, top: rect.bottom + 4, maxHeight: Math.min(desiredHeight, Math.max(96, availableBelow)) });
      }
    }
    updatePosition();
    window.addEventListener('resize', updatePosition);
    window.addEventListener('scroll', updatePosition, true);
    return () => {
      window.removeEventListener('resize', updatePosition);
      window.removeEventListener('scroll', updatePosition, true);
    };
  }, [open]);

  useEffect(() => {
    if (!open || activeIndex < 0) return;
    document.getElementById(`${listboxId}-option-${activeIndex}`)?.scrollIntoView({ block: 'nearest' });
  }, [activeIndex, listboxId, open]);

  function setInput(next: string) {
    if (!controlledInput) setInternalInput(next);
    inputValueChangeRef.current?.(next);
  }

  function choose(option: T) {
    setInput(getLabel(option));
    onChange(option);
    setOptions([option]);
    setStatus('success');
    setOpen(false);
    setActiveIndex(-1);
  }

  function clear() {
    requestSequence.current += 1;
    setInput('');
    onChange(null);
    setOptions([]);
    setStatus('idle');
    setOpen(false);
    setActiveIndex(-1);
  }

  function handleInput(next: string) {
    setInput(next);
    if (value !== null && next !== selectedLabel) onChange(null);
    setOpen(next.trim().length >= minQueryLength);
    setActiveIndex(-1);
  }

  function openCurrentLookup() {
    if (!canInteract) return;
    if (value !== null) {
      setOptions([value]);
      setStatus('success');
      setOpen(true);
    } else if (currentInput.trim().length >= minQueryLength) {
      setOpen(true);
    }
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>) {
    if (!canInteract) return;
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      if (!open) setOpen(true);
      setActiveIndex((current) => options.length === 0 ? -1 : Math.min(current + 1, options.length - 1));
      return;
    }
    if (event.key === 'ArrowUp') {
      event.preventDefault();
      if (!open) setOpen(true);
      setActiveIndex((current) => options.length === 0 ? -1 : current <= 0 ? options.length - 1 : current - 1);
      return;
    }
    if (event.key === 'Enter' && open) {
      event.preventDefault();
      if (activeIndex >= 0 && activeIndex < options.length) choose(options[activeIndex]);
      return;
    }
    if (event.key === 'Escape' && open) {
      event.preventDefault();
      setOpen(false);
      setActiveIndex(-1);
    }
  }

  const selectedKey = valueKey === null ? null : String(valueKey);
  const statusMessage = status === 'loading'
    ? loadingText
    : status === 'error'
      ? errorText
      : status === 'success' && options.length === 0
        ? emptyText
        : '';
  const dropdownStyle: CSSProperties | undefined = position
    ? { left: position.left, width: position.width, top: position.top, bottom: position.bottom, maxHeight: position.maxHeight }
    : undefined;

  return (
    <div className={`field field--autocomplete lookup-field ${className}`.trim()} ref={wrapperRef}>
      <label className="field__label" htmlFor={id}>{label}{required ? ' *' : ''}</label>
      <div className="lookup-input-wrap">
        <input
          id={id}
          className="field__input lookup-input"
          type="text"
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={open}
          aria-controls={listboxId}
          aria-activedescendant={open && activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined}
          aria-describedby={statusMessage ? statusId : undefined}
          value={currentInput}
          onChange={(event) => handleInput(event.target.value)}
          onFocus={() => {
            setFocused(true);
            openCurrentLookup();
          }}
          onClick={openCurrentLookup}
          onBlur={(event) => {
            setFocused(false);
            if (!menuRef.current?.contains(event.relatedTarget as Node | null)) {
              setOpen(false);
              setActiveIndex(-1);
            }
          }}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          autoComplete="off"
          disabled={disabled}
          readOnly={readOnly}
        />
        {canInteract && (value !== null || currentInput.length > 0) && (
          <button
            type="button"
            className="lookup-clear"
            onClick={clear}
            aria-label={clearLabel || `Xóa lựa chọn ${label}`}
            title={clearLabel || `Xóa lựa chọn ${label}`}
          >
            ×
          </button>
        )}
      </div>
      {statusMessage && <div id={statusId} className={status === 'error' ? 'field__warning' : 'lookup-status'} role={status === 'error' ? 'alert' : 'status'}>{statusMessage}</div>}
      {open && position && createPortal(
        <div
          id={listboxId}
          ref={menuRef}
          className="autocomplete-menu lookup-menu-portal"
          role="listbox"
          aria-label={`Kết quả ${label}`}
          style={dropdownStyle}
        >
          {status === 'loading' && <div className="autocomplete-empty">{loadingText}</div>}
          {status === 'error' && <div className="autocomplete-empty is-error">{errorText}</div>}
          {status === 'success' && options.length === 0 && <div className="autocomplete-empty">{emptyText}</div>}
          {status === 'success' && options.map((option, index) => {
            const key = String(getKey(option));
            const selected = selectedKey === key;
            const description = getDescription?.(option);
            return (
              <button
                id={`${listboxId}-option-${index}`}
                key={key}
                type="button"
                role="option"
                aria-selected={selected}
                className={`autocomplete-option${index === activeIndex ? ' is-active' : ''}${selected ? ' is-selected' : ''}`}
                onPointerDown={(event) => event.preventDefault()}
                onClick={() => choose(option)}
                onMouseEnter={() => setActiveIndex(index)}
                title={getLabel(option)}
              >
                <span className="lookup-option-main">{renderOption ? renderOption(option) : getLabel(option)}</span>
                {description && <small className="lookup-option-description">{description}</small>}
                {selected && <span className="lookup-option-check" aria-hidden="true">✓</span>}
              </button>
            );
          })}
        </div>,
        document.body,
      )}
    </div>
  );
}

export function filterLookupOptions<T>(
  items: readonly T[],
  keyword: string,
  getSearchText: (item: T) => string,
  limit = 20,
): T[] {
  const normalizedKeyword = normalizeLookupText(keyword);
  if (!normalizedKeyword) return items.slice(0, limit);
  return items
    .filter((item) => normalizeLookupText(getSearchText(item)).includes(normalizedKeyword))
    .slice(0, limit);
}

export function normalizeLookupText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .toLocaleUpperCase('vi-VN')
    .trim();
}
