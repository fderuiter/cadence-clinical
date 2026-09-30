// Shared helpers for tests that stub the global fetch.
//
// The API client is built on openapi-fetch, which calls fetch(request) with a single Request object
// and reads a real Response (headers, status, body). Mocks must therefore resolve to real Response
// objects, and assertions must read the Request rather than a (url, options) pair.

/** A JSON Response, the way the gateway returns one. */
export function jsonResponse(body, init = {}) {
  const status = init.status ?? 200;
  const hasBody = body !== undefined && status !== 204;
  return new Response(hasBody ? JSON.stringify(body) : null, {
    status,
    statusText: init.statusText,
    headers: { "Content-Type": "application/json", ...(init.headers || {}) },
  });
}

/**
 * The request a fetch mock received on call `index`, normalised whether the caller passed a Request
 * (openapi-fetch) or a (url, init) pair (plain fetch).
 */
export async function recordedRequest(mockFetch, index = 0) {
  const [input, init] = mockFetch.mock.calls[index];
  const request = input instanceof Request ? input : new Request(input, init);
  const text = await request.clone().text();
  let body = null;
  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = text;
    }
  }
  return {
    url: request.url,
    method: request.method,
    headers: request.headers,
    body,
  };
}
