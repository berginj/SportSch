import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import App from "./App.jsx";
import AgsaSitePage from "./pages/AgsaSitePage.jsx";
import { initTelemetry } from "./lib/telemetry";

initTelemetry();

function Root() {
  if (typeof window !== "undefined" && !window.location.pathname.startsWith("/app")) {
    return <AgsaSitePage />;
  }
  return <App />;
}

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
