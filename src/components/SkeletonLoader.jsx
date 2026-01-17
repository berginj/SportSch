/**
 * Skeleton loader component for better perceived performance
 */
export default function SkeletonLoader({ type = 'text', count = 1, className = '' }) {
  const skeletons = Array.from({ length: count }, (_, i) => i);

  if (type === 'card') {
    return (
      <div className={`skeleton-card ${className}`}>
        <div className="skeleton skeleton--title mb-3" />
        <div className="skeleton skeleton--text mb-2" />
        <div className="skeleton skeleton--text mb-2" />
        <div className="skeleton skeleton--text w-2/3" />
      </div>
    );
  }

  if (type === 'table') {
    return (
      <div className={`skeleton-table ${className}`}>
        <div className="skeleton skeleton--text mb-3 w-full h-8" />
        {skeletons.map((i) => (
          <div key={i} className="skeleton skeleton--text mb-2 w-full h-12" />
        ))}
      </div>
    );
  }

  if (type === 'circle') {
    return (
      <div className={`skeleton skeleton--circle ${className}`} />
    );
  }

  // Default: text lines
  return (
    <div className={className}>
      {skeletons.map((i) => (
        <div key={i} className="skeleton skeleton--text mb-2" />
      ))}
    </div>
  );
}
