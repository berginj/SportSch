import { app, InvocationContext, Timer } from "@azure/functions";

import { DEFAULT_REFRESH_SCHEDULE, getConfig } from "../lib/config";
import { createLogger } from "../lib/log";
import { runRefresh } from "../lib/refresh";

const refreshSchedule = getConfig().refreshSchedule || DEFAULT_REFRESH_SCHEDULE;

app.timer("scheduledRefresh", {
  schedule: refreshSchedule,
  handler: scheduledRefreshHandler,
});

async function scheduledRefreshHandler(_timer: Timer, context: InvocationContext): Promise<void> {
  const logger = createLogger(context);
  await runRefresh(logger, "timer");
}
