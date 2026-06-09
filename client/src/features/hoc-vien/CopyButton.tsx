import { useState } from 'react';

interface CopyButtonProps {
  /** Giá trị đầy đủ sẽ được sao chép (ví dụ Mã đăng ký đầy đủ). */
  value: string;
}

export default function CopyButton({ value }: CopyButtonProps) {
  const [copied, setCopied] = useState(false);

  async function handleCopy() {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(value);
      } else {
        const temp = document.createElement('textarea');
        temp.value = value;
        document.body.appendChild(temp);
        temp.select();
        document.execCommand('copy');
        document.body.removeChild(temp);
      }
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1500);
    } catch {
      setCopied(false);
    }
  }

  return (
    <button
      type="button"
      className="btn btn--ghost btn--sm"
      onClick={handleCopy}
      title="Sao chép mã đăng ký"
      aria-label="Sao chép mã đăng ký"
    >
      {copied ? 'Đã chép' : 'Chép'}
    </button>
  );
}
