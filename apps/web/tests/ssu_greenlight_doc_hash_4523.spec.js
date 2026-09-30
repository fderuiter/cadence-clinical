import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createHash } from "node:crypto";
import SsuGreenlightConsole from "../src/components/clinical/SsuGreenlightConsole.vue";
import { useToastStore } from "../src/stores/toasts";

const EMPTY_SHA256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

/**
 * #4523: an essential document is submitted with the SHA-256 of the selected file, never the hash of the
 * empty string, and a failed submission is reported instead of silently ignored.
 */
describe("SsuGreenlightConsole essential document hash (#4523)", () => {
  let pinia;
  let fetchMock;
  let submitResponse;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    submitResponse = () =>
      new Response(JSON.stringify({ id: "doc-1" }), { status: 201 });
    fetchMock = vi.fn((url, init) => {
      if (url === "/api/v1/ctms/startup/documents" && init?.method === "POST") {
        return Promise.resolve(submitResponse());
      }
      return Promise.resolve(new Response("[]", { status: 200 }));
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.spyOn(console, "error").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  async function mountConsole() {
    const wrapper = mount(SsuGreenlightConsole, {
      global: { plugins: [pinia] },
    });
    await flushPromises();
    wrapper.vm.showUploadDocModal = true;
    await wrapper.vm.$nextTick();
    return wrapper;
  }

  function submitCalls() {
    return fetchMock.mock.calls.filter(
      ([url, init]) =>
        url === "/api/v1/ctms/startup/documents" && init?.method === "POST"
    );
  }

  it("refuses to submit without a selected file", async () => {
    const wrapper = await mountConsole();

    await wrapper.vm.submitDoc();

    expect(submitCalls()).toHaveLength(0);
    expect(useToastStore().toasts[0]).toMatchObject({ tone: "error" });
    expect(wrapper.vm.showUploadDocModal).toBe(true);
  });

  it("sends the SHA-256 of the selected file's bytes", async () => {
    const bytes = "%PDF-1.7 signed FDA 1572";
    const wrapper = await mountConsole();
    const input = wrapper.find('[data-testid="ssu-doc-file"]');
    const file = new File([bytes], "FDA_1572_Site101.pdf", {
      type: "application/pdf",
    });
    Object.defineProperty(input.element, "files", { value: [file] });
    await input.trigger("change");

    await wrapper.vm.submitDoc();
    await flushPromises();

    const [[, init]] = submitCalls();
    const body = JSON.parse(init.body);
    expect(body.file_name).toBe("FDA_1572_Site101.pdf");
    expect(body.file_hash).toBe(
      createHash("sha256").update(bytes).digest("hex")
    );
    expect(body.file_hash).not.toBe(EMPTY_SHA256);
    expect(wrapper.vm.showUploadDocModal).toBe(false);
    expect(useToastStore().toasts).toHaveLength(0);
  });

  it.each([500, 502, 504])(
    "reports HTTP %i and keeps the submission dialog open",
    async (status) => {
      submitResponse = () =>
        new Response(JSON.stringify({ title: "Gateway failure" }), { status });
      const wrapper = await mountConsole();
      wrapper.vm.docForm.file = new File(["x"], "IRB.pdf");

      await wrapper.vm.submitDoc();
      await flushPromises();

      expect(submitCalls()).toHaveLength(1);
      expect(wrapper.vm.showUploadDocModal).toBe(true);
      const toasts = useToastStore().toasts;
      expect(toasts[0]).toMatchObject({ tone: "error" });
      expect(toasts[0].message).toContain(`HTTP ${status}`);
    }
  );
});
