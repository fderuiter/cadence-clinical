/**
 * The reason for change on the `X-Change-Reason` header (ADR-2495).
 *
 * Fetch only accepts header values whose characters are all at most U+00FF, so a reason with a typographic
 * dash or quote, or in a non-Latin script, would throw before the request is sent. A reason that is plain
 * printable ASCII travels unchanged; any other reason travels in the RFC 8187 form `UTF-8''` followed by its
 * percent-encoded UTF-8 bytes. The services decode it with `ChangeReasonHeader` (packages/security), so the
 * audit trail stores exactly what the user typed. Encode once, where the header is set.
 */
export const CHANGE_REASON_HEADER = "X-Change-Reason";

const PRINTABLE_ASCII = /^[\x20-\x7E]*$/;
const EXTENDED_PREFIX = /^utf-8'/i;

export function encodeChangeReason(reason: string): string {
  if (PRINTABLE_ASCII.test(reason) && !EXTENDED_PREFIX.test(reason)) {
    return reason;
  }

  // encodeURIComponent leaves ' ( ) * unescaped; RFC 8187 attr-char does not allow them.
  const encoded = encodeURIComponent(reason).replace(
    /['()*]/g,
    (c) => `%${c.charCodeAt(0).toString(16).toUpperCase()}`
  );
  return `UTF-8''${encoded}`;
}
