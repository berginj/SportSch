import { useEffect, useRef } from "react";

function useModalFocus(open, onCancel) {
  const modalRef = useRef(null);

  useEffect(() => {
    if (!open) return;
    const modal = modalRef.current;
    if (!modal) return;

    const focusable = modal.querySelectorAll(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    );
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (first) first.focus();

    const onKeyDown = (e) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onCancel?.();
      }
      if (e.key === "Tab" && focusable.length > 0) {
        if (e.shiftKey && document.activeElement === first) {
          e.preventDefault();
          last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
          e.preventDefault();
          first.focus();
        }
      }
    };

    modal.addEventListener("keydown", onKeyDown);
    return () => modal.removeEventListener("keydown", onKeyDown);
  }, [open, onCancel]);

  return modalRef;
}

export function ConfirmDialog({
  open,
  title = "Confirm",
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  onConfirm,
  onCancel,
}) {
  if (!open) return null;
  const modalRef = useModalFocus(open, onCancel);
  return (
    <div className="modalOverlay" role="presentation" onClick={onCancel}>
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        ref={modalRef}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal__header">{title}</div>
        {message ? <div className="modal__body">{message}</div> : null}
        <div className="modal__actions">
          <button className="btn btn--ghost" onClick={onCancel} type="button">
            {cancelLabel}
          </button>
          <button className="btn btn--primary" onClick={onConfirm} type="button">
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

export function PromptDialog({
  open,
  title = "Add a note",
  message,
  placeholder = "",
  value,
  onChange,
  confirmLabel = "Save",
  cancelLabel = "Cancel",
  readOnly = false,
  onConfirm,
  onCancel,
}) {
  if (!open) return null;
  const modalRef = useModalFocus(open, onCancel);
  return (
    <div className="modalOverlay" role="presentation" onClick={onCancel}>
      <div
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-label={title}
        ref={modalRef}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="modal__header">{title}</div>
        {message ? <div className="modal__body">{message}</div> : null}
        <textarea
          className="modal__input"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          readOnly={readOnly}
          rows={4}
        />
        <div className="modal__actions">
          <button className="btn btn--ghost" onClick={onCancel} type="button">
            {cancelLabel}
          </button>
          <button className="btn btn--primary" onClick={onConfirm} type="button">
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
