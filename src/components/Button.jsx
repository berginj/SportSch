/**
 * Enhanced button component with loading state
 */
export default function Button({
  children,
  variant = 'default',
  size = 'default',
  loading = false,
  disabled = false,
  className = '',
  type = 'button',
  ...props
}) {
  const variantClasses = {
    default: 'btn',
    primary: 'btn btn--primary',
    danger: 'btn btn--danger',
    ghost: 'btn btn--ghost',
  };

  const sizeClasses = {
    sm: 'text-xs px-2.5 py-1.5',
    default: '',
    lg: 'text-base px-4 py-3',
  };

  const baseClass = variantClasses[variant] || variantClasses.default;
  const sizeClass = sizeClasses[size] || '';
  const isDisabled = disabled || loading;

  return (
    <button
      type={type}
      className={`${baseClass} ${sizeClass} ${className} ${loading ? 'btn--loading' : ''}`}
      disabled={isDisabled}
      {...props}
    >
      {loading && (
        <span className="btn__spinner" role="status" aria-label="Loading">
          <svg
            className="btn__spinner-icon"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
          >
            <circle
              className="opacity-25"
              cx="12"
              cy="12"
              r="10"
              stroke="currentColor"
              strokeWidth="4"
            />
            <path
              className="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
            />
          </svg>
        </span>
      )}
      <span className={loading ? 'opacity-70' : ''}>{children}</span>
    </button>
  );
}
