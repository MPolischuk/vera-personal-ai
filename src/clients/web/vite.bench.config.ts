// DEV-ONLY config for the avatar bench page (bench.html). Not used by build.
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import { defineConfig } from "vite";
export default defineConfig({
  plugins: [react(), tailwindcss()],
  optimizeDeps: { esbuildOptions: { target: "es2022" } },
  esbuild: { target: "es2022" },
  server: { port: 5199, strictPort: true },
});
