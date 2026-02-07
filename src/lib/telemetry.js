let appInsights = null;
let initPromise = null;
const pending = [];

function getConnectionString() {
  const conn = import.meta.env.VITE_APPINSIGHTS_CONNECTION_STRING;
  const key = import.meta.env.VITE_APPINSIGHTS_KEY;
  return (conn || key || "").trim();
}

async function ensureTelemetry() {
  if (appInsights) return appInsights;
  const connectionString = getConnectionString();
  if (!connectionString) return null;
  if (!initPromise) {
    initPromise = import("@microsoft/applicationinsights-web")
      .then(({ ApplicationInsights }) => {
        appInsights = new ApplicationInsights({
          config: {
            connectionString,
            enableAutoRouteTracking: false,
          },
        });
        appInsights.loadAppInsights();
        while (pending.length) {
          const fn = pending.shift();
          fn?.(appInsights);
        }
        return appInsights;
      })
      .catch(() => null);
  }
  return initPromise;
}

function enqueueOrRun(callback) {
  if (appInsights) {
    callback(appInsights);
    return;
  }
  pending.push(callback);
  void ensureTelemetry();
}

export function initTelemetry() {
  void ensureTelemetry();
  return appInsights;
}

export function trackPageView(name, uri) {
  if (!name) return;
  enqueueOrRun((ai) => ai.trackPageView({ name, uri }));
}

export function trackEvent(name, properties = {}, measurements = undefined) {
  if (!name) return;
  enqueueOrRun((ai) => ai.trackEvent({ name }, properties, measurements));
}
