import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";
import { defineConfig } from "vitest/config";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

function hoistPushWorkerImport() {
  return {
    name: "hoist-push-sw-import",
    apply: "build" as const,
    enforce: "post" as const,
    closeBundle: {
      order: "post" as const,
      sequential: true,
      handler() {
      const swPath = path.resolve(__dirname, "dist/sw.js");
      if (!fs.existsSync(swPath)) {
        return;
      }

      const source = fs.readFileSync(swPath, "utf8");
      const nested = 'importScripts("push-sw.js"),';
      if (!source.includes(nested)) {
        return;
      }

      // The PWA plugin bundles the worker, so Workbox leaves importScripts inside
      // the module factory. That call runs too late and throws. Keep it on the
      // initial evaluation so the push handlers and the precache both run.
      const hoisted = `importScripts("push-sw.js");\n${source.replace(nested, "")}`;
      fs.writeFileSync(swPath, hoisted);
    },
    },
  };
}

function httpsCerts() {
  const keyPath = path.resolve(__dirname, "certs/localhost-key.pem");
  const certPath = path.resolve(__dirname, "certs/localhost.pem");
  if (!fs.existsSync(keyPath) || !fs.existsSync(certPath)) {
    return undefined;
  }
  return {
    key: fs.readFileSync(keyPath),
    cert: fs.readFileSync(certPath),
  };
}

export default defineConfig(({ mode }) => {
  const useHttps = mode === "https";
  const https = useHttps ? httpsCerts() : undefined;

  if (useHttps && !https) {
    console.warn(
      "[vite] mode=https but certs/localhost*.pem missing. Run: dotnet dev-certs https --trust && export PEM into certs/ (see .env.example).",
    );
  }

  return {
    plugins: [
      react(),
      tailwindcss(),
      VitePWA({
        registerType: "autoUpdate",
        injectRegister: "auto",
        includeAssets: ["favicon.svg", "icon-192.png", "icon-512.png"],
        manifest: {
          name: "Asistente personal",
          short_name: "Asistente personal",
          lang: "es",
          display: "standalone",
          start_url: "/",
          scope: "/",
          // vite-plugin-pwa merges Vue defaults; the design leaves these unset.
          description: undefined,
          background_color: undefined,
          theme_color: undefined,
          icons: [
            { src: "icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
            { src: "icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
          ],
        },
        workbox: {
          globPatterns: ["**/*.{js,css,html}"],
          globIgnores: ["**/node_modules/**/*", "sw.js", "workbox-*.js", "**/avatars/**"],
          navigateFallback: "index.html",
          maximumFileSizeToCacheInBytes: 3 * 1024 * 1024,
          importScripts: ["push-sw.js"],
        },
      }),
      hoistPushWorkerImport(),
    ],
    // wlipsync-single (via three-vrm-lip-sync) uses top-level await for WASM.
    build: {
      target: "es2022",
    },
    server: {
      https,
      port: 5173,
    },
    test: {
      environment: "node",
    },
  };
});
