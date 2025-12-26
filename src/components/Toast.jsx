import { useEffect } from "react";

export default function Toast({ open, tone = "info", message, duration = 2400, onClose }) {
  useEffect(() => {
    if (!open) return;
    const id = setTimeout(() => onClose?.(), duration);
    return () => clearTimeout(id);
  }, [open, duration, onClose]);

  if (!open || !message) return null;
  return (
    <div className={`toast toast--${tone}`} role="status" aria-live="polite">
      {message}
      <button className="toast__close" onClick={onClose} type="button" aria-label="Dismiss notification">
        ✕
      </button>
    </div>
  );
}
