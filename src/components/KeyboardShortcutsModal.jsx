/**
 * Modal displaying available keyboard shortcuts
 */
export default function KeyboardShortcutsModal({ isOpen, onClose }) {
  if (!isOpen) return null;

  const shortcuts = [
    {
      category: 'Navigation',
      items: [
        { keys: 'g h', description: 'Go to Home' },
        { keys: 'g c', description: 'Go to Calendar' },
        { keys: 'g m', description: 'Go to Manage' },
        { keys: 'g a', description: 'Go to Admin (if authorized)' },
      ],
    },
    {
      category: 'General',
      items: [
        { keys: 'Ctrl+K', description: 'Quick search (future)' },
        { keys: '?', description: 'Show this help' },
        { keys: 'Esc', description: 'Close dialogs' },
      ],
    },
    {
      category: 'Accessibility',
      items: [
        { keys: 'Tab', description: 'Navigate forward' },
        { keys: 'Shift+Tab', description: 'Navigate backward' },
        { keys: 'Enter', description: 'Activate focused element' },
        { keys: 'Space', description: 'Toggle checkboxes/buttons' },
      ],
    },
  ];

  return (
    <div className="modalOverlay" onClick={onClose}>
      <div
        className="modal modal--shortcuts"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-labelledby="shortcuts-title"
        aria-modal="true"
      >
        <div className="modal__header">
          <h2 id="shortcuts-title" className="text-lg font-semibold">
            ⌨️ Keyboard Shortcuts
          </h2>
          <button
            className="modal__close"
            onClick={onClose}
            aria-label="Close shortcuts dialog"
          >
            ✕
          </button>
        </div>

        <div className="modal__body mt-4">
          {shortcuts.map((section) => (
            <div key={section.category} className="shortcuts-section">
              <h3 className="shortcuts-section__title">{section.category}</h3>
              <dl className="shortcuts-list">
                {section.items.map((item, idx) => (
                  <div key={idx} className="shortcuts-item">
                    <dt className="shortcuts-keys">
                      {item.keys.split('+').map((key, i) => (
                        <kbd key={i} className="kbd">
                          {key}
                        </kbd>
                      ))}
                    </dt>
                    <dd className="shortcuts-description">{item.description}</dd>
                  </div>
                ))}
              </dl>
            </div>
          ))}
        </div>

        <div className="modal__footer">
          <p className="text-xs text-muted">
            Tip: Press <kbd className="kbd">?</kbd> anytime to show this help
          </p>
        </div>
      </div>
    </div>
  );
}
