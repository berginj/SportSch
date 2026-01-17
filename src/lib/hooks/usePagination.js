import { useState, useCallback } from 'react';

/**
 * Hook for handling paginated API responses
 * @param {Function} fetchFunction - Async function to fetch data (params: continuationToken, pageSize)
 * @param {number} initialPageSize - Default page size
 * @returns {object} Pagination state and controls
 */
export function usePagination(fetchFunction, initialPageSize = 50) {
  const [items, setItems] = useState([]);
  const [continuationToken, setContinuationToken] = useState(null);
  const [loading, setLoading] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [error, setError] = useState(null);

  const loadPage = useCallback(async (token = null, append = false) => {
    setLoading(true);
    setError(null);

    try {
      const result = await fetchFunction(token, initialPageSize);

      // Handle different response formats
      const data = result?.data || result;
      const newItems = data?.items || data || [];
      const nextToken = data?.continuationToken || null;

      setItems(prev => append ? [...prev, ...newItems] : newItems);
      setContinuationToken(nextToken);
      setHasMore(!!nextToken);
    } catch (err) {
      setError(err.message || 'Failed to load data');
      if (!append) {
        setItems([]);
      }
    } finally {
      setLoading(false);
    }
  }, [fetchFunction, initialPageSize]);

  const loadMore = useCallback(() => {
    if (continuationToken && !loading) {
      loadPage(continuationToken, true);
    }
  }, [continuationToken, loading, loadPage]);

  const reset = useCallback(() => {
    setItems([]);
    setContinuationToken(null);
    setHasMore(false);
    setError(null);
    loadPage(null, false);
  }, [loadPage]);

  const initialLoad = useCallback(() => {
    loadPage(null, false);
  }, [loadPage]);

  return {
    items,
    loading,
    error,
    hasMore,
    loadMore,
    reset,
    initialLoad,
  };
}
