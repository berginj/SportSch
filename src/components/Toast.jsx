import { useEffect } from "react";

const ICONS = {
  info: 'ℹ️',
  success: '✅',
  error: '⚠️',
  warning: '⚡',
};

export default function Toast({ open = true, tone = "info", message, duration = 2400, onClose }) {
  useEffect(() => {
    if (!open) return;
    const id = setTimeout(() => onClose?.(), duration);
    return () => clearTimeout(id);
  }, [open, duration, onClose]);

  if (!open || !message) return null;

  const icon = ICONS[tone] || ICONS.info;

  return (
    <div className={`toast toast--${tone}`} role="status" aria-live="polite">
      <span className="toast__icon" role="img" aria-label={tone}>
        {icon}
      </span>
      <span className="toast__message">{message}</span>
      <button className="toast__close" onClick={onClose} type="button" aria-label="Dismiss notification">
        ✕
      </button>
    </div>
  );
}
