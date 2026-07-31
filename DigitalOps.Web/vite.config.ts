import react from "@vitejs/plugin-react";
import { loadEnv } from "vite";
import { defineConfig } from "vitest/config";

export default defineConfig(({ mode }) => {
  const environment = loadEnv(mode, process.cwd(), "");

  return {
    plugins: [react()],
    server: {
      proxy: {
        "/api": {
          target:
            environment.VITE_DEV_API_TARGET ?? "https://localhost:7162",
          changeOrigin: true,
          secure: false,
        },
      },
    },
    build: {
      rolldownOptions: {
        output: {
          codeSplitting: {
            minSize: 20_000,
            maxSize: 450_000,
            groups: [
              {
                name: "vendor",
                test: /node_modules/,
              },
            ],
          },
        },
      },
    },
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: "./src/test/setup.ts",
      css: true,
      restoreMocks: true,
    },
  };
});
