import { useEffect, useRef } from "react";

export function useModalFocus(open, onCancel, initialFocusSelector) {
  const modalRef = useRef(null);
  const cancelRef = useRef(onCancel);
  useEffect(() => { cancelRef.current = onCancel; }, [onCancel]);
  useEffect(() => {
    const modal = modalRef.current;
    if (!open || !modal) return;
    const previous = document.activeElement;
    const focusable = () => [...modal.querySelectorAll('button:not(:disabled), [href], input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]')];
    (modal.querySelector(initialFocusSelector || '[data-autofocus]') || focusable()[0] || modal).focus();
    const keydown = (event) => {
      if (event.key === "Escape") { event.preventDefault(); cancelRef.current?.(); }
      if (event.key !== "Tab") return;
      const nodes = focusable();
      const first = nodes[0];
      const last = nodes.at(-1);
      if (!first) { event.preventDefault(); modal.focus(); }
      else if (event.shiftKey && (document.activeElement === first || document.activeElement === modal)) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    modal.addEventListener("keydown", keydown);
    return () => { modal.removeEventListener("keydown", keydown); if (previous?.isConnected) previous.focus(); };
  }, [open, initialFocusSelector]);
  return modalRef;
}
