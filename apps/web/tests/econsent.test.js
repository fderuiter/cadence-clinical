import { describe, it, expect, vi, beforeEach } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { econsentService } from "../src/api/econsent";

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe("eConsent API Client Unit Tests", () => {
  let pinia;

  beforeEach(() => {
    mockFetch.mockReset();
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("should list templates and append query string correctly", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => [{ template_id: "tpl-1" }],
    });

    const res = await econsentService.listTemplates({
      study_id: "study-01",
      all_versions: true,
    });
    expect(res).toEqual([{ template_id: "tpl-1" }]);
    expect(mockFetch).toHaveBeenCalledTimes(1);

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/templates?study_id=study-01&all_versions=true"
    );
    expect(options.method).toBe("GET");
  });

  it("should create template with body and custom X-Change-Reason header on mutation", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 201,
      json: async () => ({ template_id: "tpl-1" }),
    });

    const body = {
      study_id: "study-01",
      template_name: "ICF",
      protocol_version: "v1.0",
    };
    const res = await econsentService.createTemplate(body, {
      changeReason: "Initial creation",
    });
    expect(res).toEqual({ template_id: "tpl-1" });

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe("http://localhost:8000/api/v1/econsent/templates");
    expect(options.method).toBe("POST");
    expect(options.headers["X-Change-Reason"]).toBe("Initial creation");
    expect(JSON.parse(options.body)).toEqual(body);
  });

  it("should update template with body and custom X-Change-Reason", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ template_id: "tpl-1", version_index: 2 }),
    });

    const body = { study_id: "study-01", template_name: "ICF v2" };
    const res = await econsentService.updateTemplate("tpl-1", body, {
      changeReason: "Minor edit",
    });
    expect(res).toEqual({ template_id: "tpl-1", version_index: 2 });

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe("http://localhost:8000/api/v1/econsent/templates/tpl-1");
    expect(options.method).toBe("PUT");
    expect(options.headers["X-Change-Reason"]).toBe("Minor edit");
  });

  it("should get template with version_index query", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ template_id: "tpl-1", version_index: 1 }),
    });

    const res = await econsentService.getTemplate("tpl-1", {
      version_index: 1,
    });
    expect(res).toEqual({ template_id: "tpl-1", version_index: 1 });

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1?version_index=1"
    );
    expect(options.method).toBe("GET");
  });

  it("should compose template with version_index query", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ template_id: "tpl-1" }),
    });

    const res = await econsentService.composeTemplate("tpl-1", {
      version_index: 2,
    });
    expect(res).toEqual({ template_id: "tpl-1" });

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1/compose?version_index=2"
    );
    expect(options.method).toBe("GET");
  });

  it("should publish template with POST and changeReason", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ template_id: "tpl-1", is_published: true }),
    });

    const res = await econsentService.publishTemplate("tpl-1", {
      changeReason: "Publishing ICF",
    });
    expect(res).toEqual({ template_id: "tpl-1", is_published: true });

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1/publish"
    );
    expect(options.method).toBe("POST");
    expect(options.headers["X-Change-Reason"]).toBe("Publishing ICF");
  });

  it("should create clause and pass changeReason", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 201,
      json: async () => ({ clause_id: "c-1" }),
    });

    const res = await econsentService.createClause(
      { title: "C1" },
      { changeReason: "Initial clause" }
    );
    expect(res).toEqual({ clause_id: "c-1" });

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe("http://localhost:8000/api/v1/econsent/clauses");
    expect(options.method).toBe("POST");
    expect(options.headers["X-Change-Reason"]).toBe("Initial clause");
  });

  it("should update clause", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({ clause_id: "c-1" }),
    });

    const res = await econsentService.updateClause(
      "c-1",
      { title: "C1 updated" },
      { changeReason: "Clause edit" }
    );
    expect(res).toEqual({ clause_id: "c-1" });

    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe("http://localhost:8000/api/v1/econsent/clauses/c-1");
    expect(options.method).toBe("PUT");
  });

  it("should list clauses", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => [],
    });

    await econsentService.listClauses({ study_id: "study-01" });
    const [url] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/clauses?study_id=study-01"
    );
  });

  it("should get clause", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({}),
    });

    await econsentService.getClause("c-1", { version_index: 3 });
    const [url] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/clauses/c-1?version_index=3"
    );
  });

  it("should define comprehension check", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 201,
      json: async () => ({}),
    });

    await econsentService.defineComprehensionCheck(
      "tpl-1",
      1,
      { questions: [] },
      { changeReason: "Defining checks" }
    );
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1/versions/1/comprehension-checks"
    );
    expect(options.method).toBe("POST");
    expect(options.headers["X-Change-Reason"]).toBe("Defining checks");
  });

  it("should get comprehension check", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({}),
    });

    await econsentService.getComprehensionCheck("tpl-1", 1);
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1/versions/1/comprehension-checks"
    );
    expect(options.method).toBe("GET");
  });

  it("should handle translations endpoints", async () => {
    mockFetch.mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({}),
    });

    await econsentService.createTranslation(
      { source_id: "s-1" },
      { changeReason: "Add trans" }
    );
    expect(mockFetch).toHaveBeenLastCalledWith(
      "http://localhost:8000/api/v1/econsent/translations",
      expect.objectContaining({ method: "POST" })
    );

    await econsentService.updateTranslation(
      "t-1",
      { source_id: "s-1" },
      { changeReason: "Edit trans" }
    );
    expect(mockFetch).toHaveBeenLastCalledWith(
      "http://localhost:8000/api/v1/econsent/translations/t-1",
      expect.objectContaining({ method: "PUT" })
    );

    await econsentService.listTranslations({
      source_id: "s-1",
      language_code: "es",
    });
    expect(mockFetch).toHaveBeenLastCalledWith(
      "http://localhost:8000/api/v1/econsent/translations?source_id=s-1&language_code=es",
      expect.objectContaining({ method: "GET" })
    );

    await econsentService.getTranslation("t-1", { version_index: 2 });
    expect(mockFetch).toHaveBeenLastCalledWith(
      "http://localhost:8000/api/v1/econsent/translations/t-1?version_index=2",
      expect.objectContaining({ method: "GET" })
    );

    await econsentService.transitionTranslation(
      "t-1",
      { status: "APPROVED" },
      { changeReason: "Approve trans" }
    );
    expect(mockFetch).toHaveBeenLastCalledWith(
      "http://localhost:8000/api/v1/econsent/translations/t-1/transition",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ status: "APPROVED" }),
      })
    );
  });

  it("should get approved composed content for participant", async () => {
    mockFetch.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: async () => ({}),
    });

    await econsentService.getApprovedContent("tpl-1", {
      language_code: "nl",
      version_index: 1,
    });
    const [url, options] = mockFetch.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8000/api/v1/econsent/templates/tpl-1/approved-content?language_code=nl&version_index=1"
    );
    expect(options.method).toBe("GET");
  });
});
