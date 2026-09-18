import { spawn } from "node:child_process";
import { resolve } from "node:path";

// Isolated publish output prevents loading a developer's local.settings.json.
const output = resolve(".artifacts/e2e-api");
const build = spawn("dotnet", ["publish", "api/GameSwap_Functions.csproj", "-o", output, "--verbosity", "quiet"], { stdio: "inherit" });
build.on("exit", (code) => {
  if (code !== 0) { process.exit(code || 1); return; }
  const host = spawn(process.platform === "win32" ? "func.exe" : "func", ["start", "--port", "7072", "--script-root", output], {
    stdio: "inherit",
    env: {
      ...process.env,
      AZURE_FUNCTIONS_ENVIRONMENT: "Development",
      FUNCTIONS_WORKER_RUNTIME: "dotnet-isolated",
      GameSwapStorage: "UseDevelopmentStorage=true",
      AzureWebJobsStorage: "UseDevelopmentStorage=true",
      "AzureWebJobs.SendGameReminders.Disabled": "true",
      "AzureWebJobs.CleanupOrphanedRequests.Disabled": "true",
      "AzureWebJobs.CleanupOldNotifications.Disabled": "true",
      APPLICATIONINSIGHTS_CONNECTION_STRING: "",
      SENDGRID_API_KEY: "",
    },
  });
  host.on("error", (error) => { console.error(error.message); process.exitCode = 1; });
  host.on("exit", (hostCode) => { process.exitCode = hostCode || 0; });
  process.on("SIGTERM", () => host.kill());
  process.on("SIGINT", () => host.kill());
});
