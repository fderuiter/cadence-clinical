import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, relative } from "node:path";
import { describe, it, expect } from "vitest";
import { isScreenAvailable } from "../src/router/availability";

// #4529: a screen gets its own view test when it becomes available (moves onto the real API), not before.
// Hidden screens are demo code waiting for a backend; testing them now would pin behaviour that is replaced
// when they move over. This keeps the rule honest: every screen a user can open is mounted by some test.
const webRoot = join(__dirname, "..");
const testsRoot = join(webRoot, "tests");

function routeViews() {
  const router = readFileSync(join(webRoot, "src/router/index.js"), "utf8");
  const pattern =
    /name:\s*"([\w-]+)",\s*component:\s*\(\)\s*=>\s*import\("\.\.\/views\/(\w+)\.vue"\)/g;
  return [...router.matchAll(pattern)].map(([, name, view]) => ({
    name,
    view,
  }));
}

function testSources(dir = testsRoot) {
  return readdirSync(dir).flatMap((entry) => {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) {
      // Playwright suites run against a live stack and are not part of this unit suite.
      return entry === "e2e" ? [] : testSources(path);
    }
    return /\.(spec|test)\.(js|ts)$/.test(entry) &&
      entry !== "available_screen_tests.test.js"
      ? [{ path: relative(webRoot, path), text: readFileSync(path, "utf8") }]
      : [];
  });
}

describe("Available screens have view tests", () => {
  const routes = routeViews();

  it("finds the routed views", () => {
    expect(routes.map((r) => r.name)).toEqual(
      expect.arrayContaining(["login", "ecrf", "ctms"])
    );
  });

  it("imports every view a user can open in at least one unit test", () => {
    const sources = testSources();
    const untested = routes
      .filter((route) => isScreenAvailable(route.name))
      .filter(
        (route) =>
          !sources.some((source) =>
            source.text.includes(`views/${route.view}.vue`)
          )
      )
      .map((route) => `${route.name} (${route.view}.vue)`);

    expect(untested).toEqual([]);
  });
});
