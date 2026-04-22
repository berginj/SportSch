import { trackException } from "./telemetry";

/**
 * Logs errors with structured telemetry tracking.
 * In development: logs to console for debugging.
 * In all environments: sends to Application Insights for monitoring.
 *
 * @param {string} message - Human-readable error message
 * @param {Error|unknown} error - The error object or value
 * @param {object} context - Additional context properties for telemetry
 */
export function logError(message, error, context = {}) {
  const isDev = import.meta.env.DEV;

  // Always log to console in development for debugging
  if (isDev) {
    console.error(message, error);
  }

  // Always send to Application Insights for monitoring
  try {
    // Ensure error is an Error object
    const errorObj = error instanceof Error
      ? error
      : new Error(String(error || message));

    trackException(errorObj, {
      message,
      ...context,
      isDevelopment: isDev,
    });
  } catch (telemetryError) {
    // Fallback if telemetry fails - only log to console in dev
    if (isDev) {
      console.error("Failed to track exception:", telemetryError);
    }
  }
}

/**
 * Logs a warning with structured telemetry.
 * Similar to logError but for non-critical issues.
 *
 * @param {string} message - Human-readable warning message
 * @param {object} context - Additional context properties
 */
export function logWarning(message, context = {}) {
  const isDev = import.meta.env.DEV;

  if (isDev) {
    console.warn(message, context);
  }

  try {
    trackException(new Error(message), {
      severity: "warning",
      ...context,
      isDevelopment: isDev,
    });
  } catch (telemetryError) {
    if (isDev) {
      console.error("Failed to track warning:", telemetryError);
    }
  }
}
