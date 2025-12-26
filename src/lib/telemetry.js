import { ApplicationInsights } from "@microsoft/applicationinsights-web";

let appInsights = null;

export function initTelemetry() {
  if (appInsights) return appInsights;
  const conn = import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING;
  const key = import.meta.env.VITE_APPINSIGHTS_KEY;
  const connectionString = (conn || key || "").trim();
  if (!connectionString) return null;

  appInsights = new ApplicationInsights({
    config: {
      connectionString,
      enableAutoRouteTracking: false,
    },
  });
  appInsights.loadAppInsights();
  return appInsights;
}

export function trackPageView(name, uri) {
  const ai = initTelemetry();
  if (!ai) return;
  ai.trackPageView({ name, uri });
}
