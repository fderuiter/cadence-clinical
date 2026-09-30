import fs from "fs";
import { describe, it, expect } from "vitest";
import {
  GENERATED_SERVICES,
  outputPath,
  renderServiceTypes,
} from "../../scripts/generate-api-types.js";

// The typed clients compile against src/api/generated/*.ts, so those files must match docs/openapi/.
describe("generated API types", () => {
  it.each(GENERATED_SERVICES)(
    "src/api/generated/%s.ts matches its OpenAPI document (run `pnpm --filter web generate:api`)",
    async (service) => {
      const checkedIn = fs.readFileSync(outputPath(service), "utf8");
      expect(checkedIn).toBe(await renderServiceTypes(service));
    },
    30000
  );
});
