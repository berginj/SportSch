/**
 * Error card component with structured error display and recovery actions
 */
export default function ErrorCard({
  title = 'Something went wrong',
  message = '',
  error = null,
  onRetry = null,
  showDetails = false,
}) {
  const errorMessage = message || error?.message || 'An unexpected error occurred.';
  const errorCode = error?.code;
  const errorDetails = error?.details;

  return (
    <div className="error-card">
      <div className="error-card__header">
        <span className="error-card__icon" role="img" aria-label="Error">
          ⚠️
        </span>
        <h3 className="error-card__title">{title}</h3>
      </div>

      <p className="error-card__message">{errorMessage}</p>

      {errorCode && (
        <p className="error-card__code">
          Error code: <code>{errorCode}</code>
        </p>
      )}

      {showDetails && errorDetails && (
        <details className="error-card__details">
          <summary className="text-xs text-muted cursor-pointer">
            Technical details
          </summary>
          <pre className="mt-2 p-2 rounded bg-black/30 text-xs overflow-x-auto">
            {JSON.stringify(errorDetails, null, 2)}
          </pre>
        </details>
      )}

      {onRetry && (
        <button className="btn btn--primary mt-3" onClick={onRetry}>
          Try again
        </button>
      )}
    </div>
  );
}
