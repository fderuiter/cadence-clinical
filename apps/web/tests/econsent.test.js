import { describe, it, expect, vi, beforeEach } from "vitest";

const { mockFetch } = vi.hoisted(() => {
  const mockFetch = vi.fn();
  vi.stubGlobal("fetch", mockFetch);
  return { mockFetch };
});

import { createPinia, setActivePinia } from "pinia";
import { econsentService } from "../src/api/econsent";
import { jsonResponse, recordedRequest } from "./fetch_mock.js";

describe("eConsent API Client Unit Tests", () => {
  let pinia;

  beforeEach(() => {
    mockFetch.mockReset();
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("should list templates and append query string correctly", async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse([{ template_id: "tpl-1" }]));

    const res = await econsentService.listTemplates({
      study_id: "study-01",
      all_versions: true,
    });
    expect(res).toEqual([{ template_id: "tpl-1" }]);
    expect(mockFetch).toHaveBeenCalledTimes(1);

    const request = await recordedRequest(mockFetch, 0);
    expect(request.url).toBe(
      "http://localhost:8000/api/v1/econsent/templates?study_id=study-01&all_versions=true"
    );
    expect(request.method).toBe("GET");
  });

  it("should create template with body and custom X-Change-Reason header on mutation", async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ template_id: "tpl-1" }, { status: 201 })
    );

    const body = {
      study_id: "study-01",
      template_name: "ICF",
      protocol_version: "v1.0",
    };
    const res = await econsentService.createTemplate(body, {
      changeReason: "Initial creation",
    });
    expect(res).toEqual({ template_id: "tpl-1" });

    const request = await recordedRequest(mockFetch, 0);
    expect(request.url).toBe("http://localhost:8000/api/v1/econsent/templates");
    expect(request.method).toBe("POST");
    expect(request.headers.get("X-Change-Reason")).toBe("Initial creation");
    expect(request.body).toEqual(body);
  });

  it("should update template with body and custom X-Change-Reason", async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ template_id: "tpl-1", version_index: 2 })
    );

    const body = { study_id: "study-01", template_name: "ICF v2" };
    const res = await econsentService.updateTemplate("tpl-1", body, {
      changeReason: "Minor edit",
    });
    expect(res).toEqual({ template_id: "tpl-1", version_index: 2 });

    const request = await recordedRequest(mockFetch, 0);
    expect(request.url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1"
    );
    expect(request.method).toBe("PUT");
    expect(request.headers.get("X-Change-Reason")).toBe("Minor edit");
  });

  it("should compose template with version_index query", async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({ template_id: "tpl-1" }));

    const res = await econsentService.composeTemplate("tpl-1", {
      version_index: 2,
    });
    expect(res).toEqual({ template_id: "tpl-1" });

    const request = await recordedRequest(mockFetch, 0);
    expect(request.url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1/compose?version_index=2"
    );
    expect(request.method).toBe("GET");
  });

  it("should publish template with POST and changeReason", async () => {
    mockFetch.mockResolvedValueOnce(
      jsonResponse({ template_id: "tpl-1", is_published: true })
    );

    const res = await econsentService.publishTemplate("tpl-1", {
      changeReason: "Publishing ICF",
    });
    expect(res).toEqual({ template_id: "tpl-1", is_published: true });

    const request = await recordedRequest(mockFetch, 0);
    expect(request.url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1/publish"
    );
    expect(request.method).toBe("POST");
    expect(request.headers.get("X-Change-Reason")).toBe("Publishing ICF");
  });

  it("should update translation", async () => {
    mockFetch.mockResolvedValueOnce(jsonResponse({}));

    await econsentService.updateTranslation(
      "t-1",
      { source_id: "s-1" },
      { changeReason: "Edit trans" }
    );
    const request = await recordedRequest(mockFetch, 0);
    expect(request.url).toBe(
      "http://localhost:8000/api/v1/econsent/translations/t-1"
    );
    expect(request.method).toBe("PUT");
  });
});
