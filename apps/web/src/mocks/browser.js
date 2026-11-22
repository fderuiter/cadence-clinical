import { getResponse, isPassthroughResponse } from "msw";
import { setupWorker } from "msw/browser";
import { normalizeMswBasePath, waitForMswController } from "ui/testing";
import { getBearerToken, isGatewayRequest } from "../api/authorization.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { gatewayHandlers } from "./handlers.js";

export function normalizeBasePath(baseUrl = import.meta.env?.BASE_URL) {
  return normalizeMswBasePath(baseUrl, "/");
}

export function resolveWebServiceWorkerOptions(
  baseUrl = import.meta.env?.BASE_URL
) {
  const scope = normalizeBasePath(baseUrl);
  return {
    url: `${scope}mockServiceWorker.js`,
    options: { scope },
  };
}

let workerInstance = null;
if (typeof window !== "undefined" && import.meta.env?.MODE !== "test") {
  try {
    workerInstance = setupWorker(...gatewayHandlers);
  } catch {
    // Ignored in jsdom/node test environment
  }
}

export const worker = workerInstance;

let startPromise = null;

export function installInProcessMswFallback(target = window) {
  if (
    !target ||
    typeof target.fetch !== "function" ||
    target.fetch.__cadenceMswFallback
  ) {
    return;
  }
  const previousFetch = target.fetch.bind(target);
  const wrappedFetch = async (input, init) => {
    const baseHref =
      target.location?.href ||
      (typeof window !== "undefined" && window.location?.href) ||
      "https://cadence.example/";
    const rawUrl = input instanceof Request ? input.url : String(input);
    const resolvedUrl = new URL(rawUrl, baseHref).href;
    const baseReq =
      input instanceof Request
        ? new Request(resolvedUrl, input)
        : new Request(resolvedUrl, init);
    const headers = new Headers(init?.headers || baseReq.headers);
    if (
      isGatewayRequest(resolvedUrl, target.location) &&
      !headers.has("Authorization")
    ) {
      const token = await getBearerToken();
      if (token) {
        headers.set("Authorization", `Bearer ${token}`);
      }
    }
    const hasBody = baseReq.method !== "GET" && baseReq.method !== "HEAD";
    const bodyPayload =
      init?.body !== undefined
        ? init.body
        : hasBody
          ? await baseReq.clone().arrayBuffer()
          : undefined;
    const mswReq = new Request(resolvedUrl, {
      method: init?.method || baseReq.method,
      headers,
      body:
        bodyPayload instanceof ArrayBuffer && bodyPayload.byteLength === 0
          ? undefined
          : bodyPayload,
    });

    const mocked = await getResponse(gatewayHandlers, mswReq, {
      baseUrl: target.location?.origin,
    });
    if (!mocked || isPassthroughResponse(mocked)) {
      return previousFetch(input, init);
    }
    return mocked;
  };
  wrappedFetch.__cadenceMswFallback = true;
  target.fetch = wrappedFetch;

  if (
    typeof target.XMLHttpRequest === "function" &&
    !target.XMLHttpRequest.__cadenceMswFallback
  ) {
    const NativeXhr = target.XMLHttpRequest;
    class MswFallbackXmlHttpRequest extends NativeXhr {
      constructor() {
        super();
        this._mswMethod = "GET";
        this._mswUrl = "";
        this._mswAsync = true;
        this._mswOpenArgs = [];
        this._mswHeaders = new Headers();
        this._mswMockedHeaders = null;
      }

      open(method, url, async = true, ...rest) {
        this._mswMethod = String(method || "GET").toUpperCase();
        this._mswUrl = String(url || "");
        this._mswAsync = async !== false;
        this._mswOpenArgs = [method, url, async, ...rest];
        this._mswHeaders = new Headers();
        this._mswMockedHeaders = null;
        if (!this._mswAsync) {
          super.open(method, url, async, ...rest);
        }
      }

      setRequestHeader(name, value) {
        this._mswHeaders.append(name, value);
        if (!this._mswAsync) {
          super.setRequestHeader(name, value);
        }
      }

      getResponseHeader(name) {
        if (this._mswMockedHeaders) {
          return this._mswMockedHeaders.get(name);
        }
        return super.getResponseHeader(name);
      }

      getAllResponseHeaders() {
        if (this._mswMockedHeaders) {
          const lines = [];
          this._mswMockedHeaders.forEach((val, key) => {
            lines.push(`${key}: ${val}\r\n`);
          });
          return lines.join("");
        }
        return super.getAllResponseHeaders();
      }

      send(body) {
        if (!this._mswAsync) {
          super.send(body);
          return;
        }

        const baseHref =
          target.location?.href ||
          (typeof window !== "undefined" && window.location?.href) ||
          "https://cadence.example/";
        const resolvedUrl = new URL(this._mswUrl, baseHref).href;
        const hasBody =
          this._mswMethod !== "GET" &&
          this._mswMethod !== "HEAD" &&
          body !== undefined &&
          body !== null;
        const mswReq = new Request(resolvedUrl, {
          method: this._mswMethod,
          headers: this._mswHeaders,
          body: hasBody ? body : undefined,
        });

        getResponse(gatewayHandlers, mswReq, {
          baseUrl: target.location?.origin,
        })
          .then(async (mocked) => {
            if (!mocked || isPassthroughResponse(mocked)) {
              super.open(...this._mswOpenArgs);
              this._mswHeaders.forEach((val, key) => {
                super.setRequestHeader(key, val);
              });
              super.send(body);
              return;
            }

            this._mswMockedHeaders = mocked.headers;
            const respType = this.responseType || "";
            let responsePayload;
            let responseTextPayload = "";
            if (respType === "arraybuffer") {
              responsePayload = await mocked.arrayBuffer();
            } else if (respType === "blob") {
              responsePayload = await mocked.blob();
            } else if (respType === "json") {
              responsePayload = await mocked.json().catch(() => null);
            } else {
              responseTextPayload = await mocked.text();
              responsePayload = responseTextPayload;
            }

            Object.defineProperties(this, {
              readyState: { value: 4, configurable: true },
              status: { value: mocked.status, configurable: true },
              statusText: {
                value: mocked.statusText || "OK",
                configurable: true,
              },
              responseURL: { value: resolvedUrl, configurable: true },
              response: { value: responsePayload, configurable: true },
              ...(respType === "" || respType === "text"
                ? {
                    responseText: {
                      value: responseTextPayload,
                      configurable: true,
                    },
                  }
                : {}),
            });

            this.dispatchEvent(new Event("readystatechange"));
            this.dispatchEvent(new ProgressEvent("progress"));
            this.dispatchEvent(new ProgressEvent("load"));
            this.dispatchEvent(new ProgressEvent("loadend"));
          })
          .catch(() => {
            this.dispatchEvent(new ProgressEvent("error"));
            this.dispatchEvent(new ProgressEvent("loadend"));
          });
      }
    }
    MswFallbackXmlHttpRequest.__cadenceMswFallback = true;
    target.XMLHttpRequest = MswFallbackXmlHttpRequest;
  }
}

export async function ensureServiceWorkerController(timeoutMs = 1500) {
  if (
    typeof navigator === "undefined" ||
    !("serviceWorker" in navigator) ||
    typeof navigator.serviceWorker?.addEventListener !== "function" ||
    navigator.serviceWorker.controller
  ) {
    return;
  }
  await new Promise((resolve) => {
    let timer = null;
    const done = () => {
      if (timer !== null) {
        clearTimeout(timer);
      }
      navigator.serviceWorker?.removeEventListener?.("controllerchange", done);
      resolve();
    };
    navigator.serviceWorker.addEventListener("controllerchange", done, {
      once: true,
    });
    if (navigator.serviceWorker.controller) {
      done();
      return;
    }
    timer = setTimeout(done, timeoutMs);
  });
}

export async function startMswWorker(customOptions = {}) {
  const env = customOptions.env ?? import.meta.env;
  if (
    typeof window !== "undefined" &&
    !customOptions.worker &&
    env?.VITE_E2E_IN_PROCESS_MSW === "true"
  ) {
    installInProcessMswFallback(window);
    return { scope: normalizeBasePath(customOptions.baseUrl) };
  }

  const activeWorker = customOptions.worker ?? worker;
  if (typeof window === "undefined" || !activeWorker) return null;
  if (startPromise && !customOptions.worker) return startPromise;

  const serviceWorker = resolveWebServiceWorkerOptions(customOptions.baseUrl);
  const onUnhandledFrame = isDemoBuild(customOptions.env) ? "error" : "warn";

  const runWebWorkerStart = async () => {
    try {
      const registration = await activeWorker.start({
        onUnhandledFrame,
        quiet: Boolean(customOptions.quiet),
        serviceWorker,
      });
      await waitForMswController();
      await ensureServiceWorkerController();
      console.log(
        "[MSW] Mock Service Worker started for Web App API Gateway simulation."
      );
      return registration;
    } catch (err) {
      startPromise = null;
      if (import.meta.env?.MODE !== "test") {
        console.warn("[MSW] Web app service worker startup warning:", err);
      }
      return null;
    }
  };

  if (customOptions.worker) return runWebWorkerStart();
  startPromise = runWebWorkerStart();
  return startPromise;
}
