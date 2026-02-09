import { useState } from 'react';

/**
 * CollapsibleSection - Progressive disclosure component for less frequently used features
 *
 * Usage:
 * <CollapsibleSection
 *   title="Advanced Settings"
 *   subtitle="Configure rarely-changed league options"
 *   defaultExpanded={false}
 *   badge="Setup Only"
 *   badgeColor="blue"
 * >
 *   {content}
 * </CollapsibleSection>
 */
export default function CollapsibleSection({
  title,
  subtitle,
  children,
  defaultExpanded = false,
  badge,
  badgeColor = 'gray',
  icon,
  className = '',
  headerClassName = '',
  onChange
}) {
  const [isExpanded, setIsExpanded] = useState(defaultExpanded);

  const toggleExpanded = () => {
    const newState = !isExpanded;
    setIsExpanded(newState);
    if (onChange) onChange(newState);
  };

  const badgeColorClasses = {
    gray: 'bg-gray-100 text-gray-700 border-gray-300',
    blue: 'bg-blue-50 text-blue-700 border-blue-300',
    yellow: 'bg-yellow-50 text-yellow-700 border-yellow-300',
    green: 'bg-green-50 text-green-700 border-green-300',
    red: 'bg-red-50 text-red-700 border-red-300',
    purple: 'bg-purple-50 text-purple-700 border-purple-300'
  };

  return (
    <div className={`border-2 border-border rounded-2xl bg-white overflow-hidden ${className}`}>
      {/* Collapsible Header */}
      <button
        type="button"
        className={`w-full flex items-center justify-between gap-4 px-4 py-3 hover:bg-gray-50 transition-colors ${headerClassName}`}
        onClick={toggleExpanded}
        aria-expanded={isExpanded}
      >
        <div className="flex items-center gap-3 flex-1 min-w-0 text-left">
          {icon && <span className="text-xl flex-shrink-0">{icon}</span>}
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 flex-wrap">
              <h3 className="font-semibold text-base text-gray-900">{title}</h3>
              {badge && (
                <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium border ${badgeColorClasses[badgeColor] || badgeColorClasses.gray}`}>
                  {badge}
                </span>
              )}
            </div>
            {subtitle && (
              <p className="text-sm text-gray-600 mt-0.5">{subtitle}</p>
            )}
          </div>
        </div>

        {/* Expand/Collapse Icon */}
        <svg
          className={`w-5 h-5 flex-shrink-0 text-gray-500 transition-transform ${isExpanded ? 'rotate-180' : ''}`}
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
