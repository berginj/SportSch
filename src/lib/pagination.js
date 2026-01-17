// src/lib/pagination.js

import { useState, useCallback, useEffect } from "react";

/**
 * Custom hook for client-side pagination of an array.
 * @param {Array} items - The full array of items to paginate
 * @param {number} pageSize - Number of items per page (default: 50)
 * @returns {Object} Pagination state and controls
 */
export function usePagination(items = [], pageSize = 50) {
  const [currentPage, setCurrentPage] = useState(1);

  const totalPages = Math.ceil(items.length / pageSize);
  const startIndex = (currentPage - 1) * pageSize;
  const endIndex = startIndex + pageSize;
  const currentItems = items.slice(startIndex, endIndex);

  const goToPage = useCallback((page) => {
    setCurrentPage(Math.max(1, Math.min(page, totalPages)));
  }, [totalPages]);

  const nextPage = useCallback(() => {
    if (currentPage < totalPages) {
      setCurrentPage(currentPage + 1);
    }
  }, [currentPage, totalPages]);

  const prevPage = useCallback(() => {
    if (currentPage > 1) {
      setCurrentPage(currentPage - 1);
    }
  }, [currentPage]);

  // Reset to page 1 when items change
  useEffect(() => {
    setCurrentPage(1);
  }, [items.length]);

  return {
    currentPage,
    totalPages,
    pageSize,
    currentItems,
    hasNextPage: currentPage < totalPages,
    hasPrevPage: currentPage > 1,
    goToPage,
    nextPage,
    prevPage,
    totalItems: items.length,
    startIndex,
    endIndex: Math.min(endIndex, items.length)
  };
}

/**
 * Custom hook for server-side pagination with continuation tokens.
 * @param {Function} fetchFunction - Async function that fetches data: (continuationToken, pageSize) => Promise<{items, continuationToken}>
 * @param {number} initialPageSize - Number of items per page (default: 50)
 * @returns {Object} Pagination state and controls
 */
export function useServerPagination(fetchFunction, initialPageSize = 50) {
  const [items, setItems] = useState([]);
  const [continuationToken, setContinuationToken] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);
  const [hasMore, setHasMore] = useState(false);

  const loadMore = useCallback(async () => {
    if (loading) return;

    setLoading(true);
    setError(null);

    try {
      const result = await fetchFunction(continuationToken, initialPageSize);
      setItems(prev => [...prev, ...result.items]);
      setContinuationToken(result.continuationToken);
      setHasMore(!!result.continuationToken);
    } catch (err) {
      console.error("Pagination error:", err);
      setError(err.message || "Failed to load data");
    } finally {
      setLoading(false);
    }
  }, [fetchFunction, continuationToken, initialPageSize, loading]);

  const reset = useCallback(() => {
    setItems([]);
    setContinuationToken(null);
    setHasMore(false);
    setError(null);
  }, []);

  // Auto-load first page
  useEffect(() => {
    if (items.length === 0 && !loading && !error) {
      loadMore();
    }
  }, []);

  return {
    items,
    loading,
    error,
    hasMore,
    loadMore,
    reset,
    pageSize: initialPageSize
  };
}

/**
 * Builds pagination info text (e.g., "Showing 1-50 of 234")
 * @param {number} startIndex - Zero-based start index
 * @param {number} endIndex - Zero-based end index (exclusive)
 * @param {number} totalItems - Total number of items
 * @returns {string} Formatted pagination info
 */
export function getPaginationInfo(startIndex, endIndex, totalItems) {
  if (totalItems === 0) return "No items";
  const start = startIndex + 1;
  const end = Math.min(endIndex, totalItems);
  return `Showing ${start}–${end} of ${totalItems}`;
}

/**
 * Calculates page numbers to show in pagination UI.
 * Shows first, last, current, and nearby pages with ellipsis.
 * @param {number} currentPage - Current page number (1-indexed)
 * @param {number} totalPages - Total number of pages
 * @param {number} siblings - Number of sibling pages to show on each side (default: 1)
 * @returns {Array} Array of page numbers and 'ellipsis' markers
 */
export function getPaginationRange(currentPage, totalPages, siblings = 1) {
  if (totalPages <= 7) {
    // Show all pages if 7 or fewer
    return Array.from({ length: totalPages }, (_, i) => i + 1);
  }

  const range = [];
  const leftSibling = Math.max(currentPage - siblings, 1);
  const rightSibling = Math.min(currentPage + siblings, totalPages);

  // Always show first page
  range.push(1);

  // Add left ellipsis if needed
  if (leftSibling > 2) {
    range.push("ellipsis-left");
  }

  // Add sibling pages
  for (let i = leftSibling; i <= rightSibling; i++) {
    if (i !== 1 && i !== totalPages) {
      range.push(i);
    }
  }

  // Add right ellipsis if needed
  if (rightSibling < totalPages - 1) {
    range.push("ellipsis-right");
  }

  // Always show last page
  if (totalPages > 1) {
    range.push(totalPages);
  }

  return range;
}
