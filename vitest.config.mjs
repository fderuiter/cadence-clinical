import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: ["docs/.vitepress/**/*.test.mjs"],
    exclude: [
      "**/node_modules/**",
      "**/dist/**",
      "docs/.vitepress/{.temp,cache,dist}/**",
    ],
  },
});
