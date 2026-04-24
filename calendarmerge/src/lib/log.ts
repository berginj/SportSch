import { InvocationContext } from "@azure/functions";

type LogLevel = "info" | "warn" | "error";

export interface Logger {
  info(message: string, details?: Record<string, unknown>): void;
  warn(message: string, details?: Record<string, unknown>): void;
  error(message: string, details?: Record<string, unknown>): void;
}

export function createLogger(context?: InvocationContext): Logger {
  const sink = context?.log ?? console.log;

  function write(level: LogLevel, message: string, details?: Record<string, unknown>): void {
    const payload = JSON.stringify({
      timestamp: new Date().toISOString(),
      level,
      message,
      ...(details ?? {}),
    });

    sink(payload);
  }

  return {
    info: (message, details) => write("info", message, details),
    warn: (message, details) => write("warn", message, details),
    error: (message, details) => write("error", message, details),
  };
}
