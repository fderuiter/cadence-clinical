import path from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "vite";
import { describe, expect, it } from "vitest";
import { SUBJECT_PORTAL_NON_DEMO_STUBBED_VIEWS } from "../../../scripts/vite-strip-demo-fixtures.mjs";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const PORTAL_ROOT = path.resolve(__dirname, "..");
const VITE_CONFIG_PATH = path.resolve(PORTAL_ROOT, "vite.config.js");

const DEMO_TAB_ICON_MODULES = [
  "clipboard-list",
  "trending-up",
  "mail",
  "file-signature",
  "file-pen-line",
  "scroll-text",
];

describe("subject-portal non-demo production bundle stripping", () => {
  it("excludes all six DEMO_AVAILABLE_SCREENS views and five demo tab icons while keeping WalletView and Wallet icon", async () => {
    const previousDemoMode = process.env.VITE_DEMO_MODE;
    delete process.env.VITE_DEMO_MODE;

    try {
      const result = await build({
        root: PORTAL_ROOT,
        configFile: VITE_CONFIG_PATH,
        mode: "production",
        logLevel: "silent",
        build: {
          write: false,
          minify: false,
        },
      });

      const outputs = Array.isArray(result) ? result : [result];
      const chunks = outputs.flatMap((output) =>
        "output" in output
          ? output.output.filter((item) => item.type === "chunk")
          : []
      );
      const allModuleIds = chunks.flatMap((chunk) =>
        Object.keys(chunk.modules).map((id) => id.replace(/\\/g, "/"))
      );
      const entryModuleIds = chunks
        .filter((chunk) => chunk.isEntry)
        .flatMap((chunk) =>
          Object.keys(chunk.modules).map((id) => id.replace(/\\/g, "/"))
        );

      for (const demoView of SUBJECT_PORTAL_NON_DEMO_STUBBED_VIEWS) {
        expect(
          allModuleIds.filter((id) => id.includes(`/src/views/${demoView}`))
        ).toEqual([]);
      }

      // Note: packages/ui/src/icons/routeIcons.js (lazy-loaded by ClinicalIcon) holds shared UI icons;
      // the subject-portal entry bundle (where src/router/routes.js lives) must tree-shake all 5 demo tab icons.
      for (const iconSlug of DEMO_TAB_ICON_MODULES) {
        expect(
          entryModuleIds.filter((id) =>
            new RegExp(`lucide-vue-next/.*/${iconSlug}\\.(?:m?js|cjs)`).test(id)
          )
        ).toEqual([]);
      }
      expect(
        allModuleIds.filter((id) =>
          /lucide-vue-next\/.*\/scroll-text\.(?:m?js|cjs)/.test(id)
        )
      ).toEqual([]);

      expect(
        allModuleIds.some((id) => id.includes("/src/views/WalletView.vue"))
      ).toBe(true);
      expect(
        allModuleIds.some((id) => id.includes("/src/views/UnavailableView.vue"))
      ).toBe(true);
      expect(
        entryModuleIds.some((id) =>
          /lucide-vue-next\/.*\/wallet\.(?:m?js|cjs)/.test(id)
        )
      ).toBe(true);
    } finally {
      if (previousDemoMode === undefined) {
        delete process.env.VITE_DEMO_MODE;
      } else {
        process.env.VITE_DEMO_MODE = previousDemoMode;
      }
    }
  }, 30_000);
});
