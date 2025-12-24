import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
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

