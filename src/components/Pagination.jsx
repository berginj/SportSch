/**
 * Pagination component for load-more pattern
 */
export default function Pagination({ hasMore, loading, onLoadMore, itemCount }) {
  if (!hasMore && itemCount === 0) {
    return null;
  }

  return (
    <div className="pagination">
      {hasMore && (
        <button
          className="btn btn--primary"
          onClick={onLoadMore}
          disabled={loading}
        >
          {loading ? 'Loading...' : 'Load More'}
        </button>
      )}
      {itemCount > 0 && (
        <span className="pagination__info">
          Showing {itemCount} {itemCount === 1 ? 'item' : 'items'}
        </span>
      )}
    </div>
  );
}
