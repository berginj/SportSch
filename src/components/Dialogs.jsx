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
  return (
    <div className="modalOverlay" role="presentation" onClick={onCancel}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}>
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
  return (
    <div className="modalOverlay" role="presentation" onClick={onCancel}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}>
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
