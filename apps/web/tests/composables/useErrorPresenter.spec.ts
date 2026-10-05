import { describe, it, expect, vi } from "vitest";
import { useErrorPresenter } from "@/composables/useErrorPresenter";

describe("useErrorPresenter composable", () => {
  const { presentError, formatErrorMessage, sanitizeError } =
    useErrorPresenter();

  it("replaces internal microservice names with clean clinical domain service names", () => {
    const rawError =
      "Amendment diff could not be loaded from the Designer service: HTTP 500";
    const presented = presentError(rawError);
    expect(presented).not.toContain("Designer service");
    expect(presented).toContain("protocol design service");
  });

  it("masks internal HTTP URL topologies and stack traces", () => {
    const stackError = new Error(
      "500 Internal Server Error at http://designer-service:8080/api/v1/diff\n   at Cadence.Apps.Designer.Controllers.DiffController.Get()"
    );
    const presented = presentError(stackError, "Failed to load diff.");
    expect(presented).not.toContain("http://designer-service:8080");
    expect(presented).not.toContain("Cadence.Apps.Designer");
  });

  it("handles Axios/API error objects with detail response", () => {
    const apiError = {
      response: {
        data: {
          detail: "Validation failed at stage gate in Designer service.",
        },
      },
    };
    const presented = formatErrorMessage(apiError);
    expect(presented).not.toContain("Designer service");
    expect(presented).toContain("protocol design service");
  });
});
