import { useState, useEffect } from 'react';

/**
 * CollapsibleSection - Progressive disclosure component for less frequently used features
 *
 * Usage:
 * <CollapsibleSection
 *   title="Advanced Settings"
 *   subtitle="Configure rarely-changed league options"
 *   defaultExpanded={false}
 *   storageKey="league-settings-advanced" // Optional: persist state in localStorage
 *   badge="Setup Only"
 *   badgeColor="blue"
 * >
 *   {content}
 * </CollapsibleSection>
 *
 * Controlled mode (for Expand All / Collapse All):
 * <CollapsibleSection
 *   isExpanded={controlledState}
 *   onToggle={(state) => setControlledState(state)}
 * />
 */
export default function CollapsibleSection({
  title,
  subtitle,
  children,
  defaultExpanded = false,
  storageKey,
  badge,
  badgeColor = 'gray',
  icon,
  className = '',
  headerClassName = '',
  onChange,
  // Controlled mode props
  isExpanded: controlledIsExpanded,
  onToggle: controlledOnToggle,
}) {
  // Load saved state from localStorage if storageKey is provided
  const [internalIsExpanded, setInternalIsExpanded] = useState(() => {
    if (!storageKey || typeof window === 'undefined') return defaultExpanded;
    try {
      const saved = localStorage.getItem(`collapsible-${storageKey}`);
      return saved !== null ? JSON.parse(saved) : defaultExpanded;
    } catch {
      return defaultExpanded;
    }
  });

  // Use controlled state if provided, otherwise use internal state
  const isControlled = controlledIsExpanded !== undefined;
  const isExpanded = isControlled ? controlledIsExpanded : internalIsExpanded;

  // Save to localStorage when state changes (only in uncontrolled mode)
  useEffect(() => {
    if (isControlled || !storageKey || typeof window === 'undefined') return;
    try {
      localStorage.setItem(`collapsible-${storageKey}`, JSON.stringify(internalIsExpanded));
    } catch {
      // Ignore localStorage errors (quota exceeded, private browsing, etc.)
    }
  }, [internalIsExpanded, storageKey, isControlled]);

  const toggleExpanded = () => {
    const newState = !isExpanded;
    if (isControlled) {
      // Controlled mode: call the onToggle callback
      if (controlledOnToggle) controlledOnToggle(newState);
    } else {
      // Uncontrolled mode: update internal state
      setInternalIsExpanded(newState);
    }
    if (onChange) onChange(newState);
  };

  const badgeColorClasses = {
    gray: 'softballBadge softballBadge--neutral',
    blue: 'softballBadge softballBadge--offer',
    yellow: 'softballBadge softballBadge--request',
    green: 'softballBadge softballBadge--practice',
    red: 'softballBadge softballBadge--both',
    purple: 'softballBadge softballBadge--neutral'
  };

  return (
    <div className={`card overflow-hidden ${className}`}>
      {/* Collapsible Header */}
      <button
        type="button"
        className={`w-full flex items-center justify-between gap-4 px-4 py-3 hover:bg-amber-50 transition-colors ${headerClassName}`}
        onClick={toggleExpanded}
        aria-expanded={isExpanded}
      >
        <div className="flex items-center gap-3 flex-1 min-w-0 text-left">
          {icon && <span className="text-lg flex-shrink-0">{icon}</span>}
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <h3 className="font-semibold text-base">{title}</h3>
              {badge && (
                <span className={badgeColorClasses[badgeColor] || badgeColorClasses.gray}>
                  {badge}
                </span>
              )}
            </div>
            {subtitle && (
              <p className="subtle mt-0.5">{subtitle}</p>
            )}
          </div>
        </div>

        {/* Expand/Collapse Icon */}
        <svg
          className={`w-5 h-5 flex-shrink-0 text-muted transition-transform ${isExpanded ? 'rotate-180' : ''}`}
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {/* Collapsible Content */}
      {isExpanded && (
        <div className="px-4 py-4 border-t-2 border-border">
          {children}
        </div>
      )}
    </div>
  );
}
