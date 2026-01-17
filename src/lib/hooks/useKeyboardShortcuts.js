import { useEffect } from 'react';

/**
 * Hook for handling keyboard shortcuts
 * @param {Object} shortcuts - Map of key combinations to handlers
 * @param {boolean} enabled - Whether shortcuts are enabled
 *
 * Example:
 * useKeyboardShortcuts({
 *   'Ctrl+K': () => console.log('Search'),
 *   'g h': () => navigate('home'),
 *   '?': () => showHelp()
 * });
 */
export function useKeyboardShortcuts(shortcuts, enabled = true) {
  useEffect(() => {
    if (!enabled || !shortcuts) return;

    let sequenceKeys = [];
    let sequenceTimeout = null;

    const handleKeyDown = (event) => {
      // Don't trigger shortcuts when typing in inputs
      if (
        event.target.matches('input, textarea, select, [contenteditable]') &&
        !event.ctrlKey &&
        !event.metaKey
      ) {
        return;
      }

      const key = event.key;
      const ctrl = event.ctrlKey || event.metaKey;
      const shift = event.shiftKey;
      const alt = event.altKey;

      // Build key combination string
      let combo = [];
      if (ctrl) combo.push('Ctrl');
      if (shift) combo.push('Shift');
      if (alt) combo.push('Alt');
      combo.push(key);
      const comboString = combo.join('+');

      // Check for direct match (e.g., "Ctrl+K")
      if (shortcuts[comboString]) {
        event.preventDefault();
        shortcuts[comboString](event);
        return;
      }

      // Check for single key match (e.g., "?")
      if (shortcuts[key] && !ctrl && !shift && !alt) {
        event.preventDefault();
        shortcuts[key](event);
        return;
      }

      // Handle sequences (e.g., "g h")
      if (!ctrl && !shift && !alt) {
        sequenceKeys.push(key);
        const sequence = sequenceKeys.join(' ');

        // Clear sequence after timeout
        clearTimeout(sequenceTimeout);
        sequenceTimeout = setTimeout(() => {
          sequenceKeys = [];
        }, 1000);

        // Check if sequence matches
        if (shortcuts[sequence]) {
          event.preventDefault();
          shortcuts[sequence](event);
          sequenceKeys = [];
          clearTimeout(sequenceTimeout);
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);

    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      clearTimeout(sequenceTimeout);
    };
  }, [shortcuts, enabled]);
}

/**
 * Common keyboard shortcuts for the app
 */
export const COMMON_SHORTCUTS = {
  SEARCH: 'Ctrl+K',
  HELP: '?',
  GO_HOME: 'g h',
  GO_CALENDAR: 'g c',
  GO_MANAGE: 'g m',
  GO_ADMIN: 'g a',
  ESCAPE: 'Escape',
  SAVE: 'Ctrl+S',
  REFRESH: 'Ctrl+R',
};
