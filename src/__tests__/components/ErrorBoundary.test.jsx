import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import ErrorBoundary from '../../components/ErrorBoundary';
import { trackException } from '../../lib/telemetry';

vi.mock('../../lib/telemetry');

// Component that throws an error for testing
function ThrowError({ shouldThrow, message = 'Test error' }) {
  if (shouldThrow) {
    throw new Error(message);
  }
  return <div>No error</div>;
}

describe('ErrorBoundary', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Suppress console.error in tests (ErrorBoundary logs errors)
    vi.spyOn(console, 'error').mockImplementation(() => {});
  });

  it('renders children when no error occurs', () => {
    render(
      <ErrorBoundary>
        <div>Test content</div>
      </ErrorBoundary>
    );

    expect(screen.getByText('Test content')).toBeInTheDocument();
  });

  it('catches error and displays error UI', () => {
    render(
      <ErrorBoundary>
        <ThrowError shouldThrow={true} message="Something went wrong" />
      </ErrorBoundary>
    );

    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByText(/please try refreshing the page/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reload page/i })).toBeInTheDocument();
  });

  it('tracks exception to Application Insights when error caught', () => {
    render(
      <ErrorBoundary>
        <ThrowError shouldThrow={true} message="Test exception" />
      </ErrorBoundary>
    );

    expect(trackException).toHaveBeenCalledWith(
      expect.objectContaining({
        message: 'Test exception'
      }),
      expect.objectContaining({
        errorBoundary: true
      })
    );
  });

  it('shows error details in development mode', () => {
    // Simulate dev mode
    const originalEnv = import.meta.env.DEV;
    import.meta.env.DEV = true;

    render(
      <ErrorBoundary>
        <ThrowError shouldThrow={true} message="Dev error details" />
      </ErrorBoundary>
    );

    // Should show details summary in dev mode
    const detailsSummary = screen.getByText(/Error Details \(Dev Only\)/i);
    expect(detailsSummary).toBeInTheDocument();

    // Restore env
    import.meta.env.DEV = originalEnv;
  });

  it('reload button triggers window.location.reload', () => {
    const mockReload = vi.fn();
    Object.defineProperty(window, 'location', {
      value: { reload: mockReload },
      writable: true
    });

    render(
      <ErrorBoundary>
        <ThrowError shouldThrow={true} />
      </ErrorBoundary>
    );

    const reloadButton = screen.getByRole('button', { name: /reload page/i });
    reloadButton.click();

    expect(mockReload).toHaveBeenCalledTimes(1);
  });

  it('prevents white screen crash by catching unhandled errors', () => {
    // This test verifies the critical fix: ErrorBoundary prevents app crashes

    const consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    const { container } = render(
      <ErrorBoundary>
        <ThrowError shouldThrow={true} message="Critical unhandled error" />
      </ErrorBoundary>
    );

    // Verify error UI is shown instead of white screen
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(container.querySelector('.errorBoundary')).toBeInTheDocument();

    // Verify app didn't crash (children are replaced with error UI)
    expect(screen.queryByText('No error')).not.toBeInTheDocument();

    consoleErrorSpy.mockRestore();
  });

  it('handles errors in lazy-loaded components', async () => {
    // Simulate error in lazy-loaded component
    const LazyComponent = () => {
      throw new Error('Lazy component failed');
    };

    render(
      <ErrorBoundary>
        <LazyComponent />
      </ErrorBoundary>
    );

    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(trackException).toHaveBeenCalled();
  });

  it('provides user-friendly error message instead of technical details', () => {
    render(
      <ErrorBoundary>
        <ThrowError shouldThrow={true} message="TypeError: Cannot read property 'foo' of undefined" />
      </ErrorBoundary>
    );

    // Should show friendly message, not technical error
    expect(screen.getByText('Something went wrong')).toBeInTheDocument();
    expect(screen.getByText(/please try refreshing the page/i)).toBeInTheDocument();

    // Technical details should be in dev details section only, not main message
    const statusMessage = screen.getByText(/please try refreshing the page/i);
    expect(statusMessage.textContent).not.toContain('Cannot read property');
  });
});
