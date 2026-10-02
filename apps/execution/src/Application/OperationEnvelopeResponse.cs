namespace Cadence.Apps.Execution.Application;

/// <summary>
/// Typed replacement for the legacy's untyped success envelopes (`01_spec.md` §1.2): site activation
/// (<c>POST .../sites/{site_id}/activate</c>) and the eTMF completeness webhook
/// (<c>POST .../webhooks/etmf</c>) both returned a bare <c>dict[str, Any]</c> in the legacy code with no
/// Pydantic response model backing it — this port defines one typed record for both call sites instead
/// of reproducing the untyped shape. <c>Status</c>/<c>Message</c> are already single-word properties, so
/// the shared snake_case naming policy (`Program.cs`, MIG-SUBJ-008) maps them to <c>status</c>/
/// <c>message</c> without needing any per-property override.
/// @req:PRD-SUB-001
/// </summary>
public sealed record OperationEnvelopeResponse(string Status, string Message, object? Data = null);
