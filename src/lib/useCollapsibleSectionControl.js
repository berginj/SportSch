import { useState, useCallback } from 'react';

/**
 * useCollapsibleSectionControl - Hook to control multiple CollapsibleSection components
 *
 * Returns:
 * - expanded: object mapping section IDs to their expanded state
 * - setExpanded: function to set expanded state for a specific section
 * - expandAll: function to expand all sections
 * - collapseAll: function to collapse all sections
 * - toggleAll: function to toggle all sections (expands if any collapsed, collapses if all expanded)
 *
 * Usage:
 * const control = useCollapsibleSectionControl(['section1', 'section2', 'section3']);
 *
 * <button onClick={control.expandAll}>Expand All</button>
 * <button onClick={control.collapseAll}>Collapse All</button>
 *
 * <CollapsibleSection
 *   isExpanded={control.expanded['section1']}
 *   onToggle={(state) => control.setExpanded('section1', state)}
 * />
 */
export function useCollapsibleSectionControl(sectionIds = []) {
  const [expanded, setExpandedState] = useState({});

  const setExpanded = useCallback((sectionId, isExpanded) => {
    setExpandedState((prev) => ({ ...prev, [sectionId]: isExpanded }));
  }, []);

  const expandAll = useCallback(() => {
    const newState = {};
    sectionIds.forEach((id) => {
      newState[id] = true;
    });
    setExpandedState(newState);
  }, [sectionIds]);

  const collapseAll = useCallback(() => {
    const newState = {};
    sectionIds.forEach((id) => {
      newState[id] = false;
    });
    setExpandedState(newState);
  }, [sectionIds]);

  const toggleAll = useCallback(() => {
    // If any section is collapsed, expand all. Otherwise, collapse all.
    const anyCollapsed = sectionIds.some((id) => !expanded[id]);
    if (anyCollapsed) {
      expandAll();
    } else {
      collapseAll();
    }
  }, [sectionIds, expanded, expandAll, collapseAll]);

  return {
    expanded,
    setExpanded,
    expandAll,
    collapseAll,
    toggleAll,
  };
}
