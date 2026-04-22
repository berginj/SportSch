import { Component } from "react";
import StatusCard from "./StatusCard";
import { trackException } from "../lib/telemetry";

/**
 * Error boundary component to catch and display errors gracefully.
 * Prevents the entire app from crashing with a blank screen.
 */
export default class ErrorBoundary extends Component {
  constructor(props) {
    super(props);
    this.state = { hasError: false, error: null, errorInfo: null };
  }

  static getDerivedStateFromError(error) {
    return { hasError: true, error };
  }

  componentDidCatch(error, errorInfo) {
    // Log error details for debugging
    console.error("Error caught by ErrorBoundary:", error, errorInfo);

    // Track exception in Application Insights
    try {
      trackException(error, {
        component: errorInfo?.componentStack || "Unknown",
        errorBoundary: true,
      });
    } catch (telemetryError) {
      console.error("Failed to track exception:", telemetryError);
    }

    this.setState({ errorInfo });
  }

  handleReset = () => {
    this.setState({ hasError: false, error: null, errorInfo: null });
    // Attempt to recover by reloading the page
    window.location.reload();
  };

  render() {
    if (this.state.hasError) {
      const isDev = import.meta.env.DEV;

      return (
        <div className="errorBoundary">
          <StatusCard
            title="Something went wrong"
            message="An unexpected error occurred. Please try refreshing the page."
            tone="error"
          >
            <div style={{ marginTop: "1rem" }}>
              <button className="btn btn--primary" onClick={this.handleReset}>
                Reload Page
              </button>
            </div>

            {isDev && this.state.error && (
              <details style={{ marginTop: "1rem", fontSize: "0.875rem" }}>
                <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
                  Error Details (Dev Only)
                </summary>
                <pre
                  style={{
                    marginTop: "0.5rem",
                    padding: "0.5rem",
                    background: "#f5f5f5",
                    border: "1px solid #ddd",
                    borderRadius: "4px",
                    overflow: "auto",
                    maxHeight: "300px",
                  }}
                >
                  {this.state.error.toString()}
                  {this.state.errorInfo?.componentStack}
                </pre>
              </details>
            )}
          </StatusCard>
        </div>
      );
    }

    return this.props.children;
  }
}
