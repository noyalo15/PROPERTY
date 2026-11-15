import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev: Vite on :5173 proxies /api and /hangfire to the ASP.NET Core API on :5080.
// Build: output goes to the API's wwwroot, so IIS serves the app and the API from one site.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { "/api": "http://localhost:5080", "/hangfire": "http://localhost:5080" },
  },
  build: { outDir: "../src/NavEstate.Api/wwwroot", emptyOutDir: true },
  resolve: { alias: { "@": "/src" } },
});
