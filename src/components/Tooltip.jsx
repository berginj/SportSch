import { useState } from 'react';

/**
 * Tooltip component for contextual help
 */
export default function Tooltip({ content, children, position = 'top' }) {
  const [isVisible, setIsVisible] = useState(false);

  const positionClasses = {
    top: 'tooltip--top',
    bottom: 'tooltip--bottom',
    left: 'tooltip--left',
    right: 'tooltip--right',
  };

  return (
    <span className="tooltip-wrapper">
      <span
        className="tooltip-trigger"
        onMouseEnter={() => setIsVisible(true)}
        onMouseLeave={() => setIsVisible(false)}
        onFocus={() => setIsVisible(true)}
        onBlur={() => setIsVisible(false)}
        tabIndex={0}
        role="button"
        aria-label="Show tooltip"
      >
        {children}
      </span>
      {isVisible && (
        <span
          className={`tooltip ${positionClasses[position] || positionClasses.top}`}
          role="tooltip"
        >
          {content}
        </span>
      )}
    </span>
  );
}
