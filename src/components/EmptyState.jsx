/**
 * Empty state component with helpful messaging and actions
 */
export default function EmptyState({
  icon = '📭',
  title = 'No data found',
  message = 'There's nothing here yet.',
  action = null,
  actionLabel = '',
  onAction = null,
}) {
  return (
    <div className="empty-state">
      <div className="empty-state__icon">{icon}</div>
      <h3 className="empty-state__title">{title}</h3>
      <p className="empty-state__message">{message}</p>
      {action && onAction && (
        <button className="btn btn--primary mt-4" onClick={onAction}>
          {actionLabel || action}
        </button>
      )}
    </div>
  );
}
