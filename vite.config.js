import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (id.includes("node_modules/@microsoft/applicationinsights-web")) {
            return "vendor-appinsights";
          }
          if (id.includes("node_modules/react") || id.includes("node_modules/react-dom")) {
            return "vendor-react";
          }
          return undefined;
        },
      },
    },
  },
  server: {
    proxy: {
      // Your Azure Functions local host
      "/api": {
        target: "http://localhost:7072",
        changeOrigin: true,
        secure: false
      }
    }
  }
});

