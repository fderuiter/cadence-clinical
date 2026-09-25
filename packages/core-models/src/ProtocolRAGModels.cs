#pragma warning disable S101 // Type names should comply with a configured naming convention

namespace Cadence.CoreModels;

/// <summary>
/// Normalized protocol excerpt chunk passed to RAG generation and verification.
/// Enforces 21 CFR Part 11 and CDISC GxP compliance for Grounded Protocol AI Assistance.
/// Requirements: PRD-TCK-005, PRD-SYS-051, ADR-2192
/// </summary>
/// <param name="ChunkId">Unique chunk identifier.</param>
/// <param name="ProtocolVersion">Protocol version (e.g., 'v2.1').</param>
/// <param name="SectionNumber">Protocol section number (e.g. '7.3').</param>
/// <param name="SectionTitle">Protocol section title.</param>
/// <param name="PageNumber">1-indexed physical page number in source PDF.</param>
/// <param name="ChunkText">Raw text of the protocol chunk.</param>
/// <param name="IsApproved">Whether this protocol chunk is from an approved version.</param>
public record ProtocolRAGContextChunk(
    string ChunkId,
    string ProtocolVersion,
    string? SectionNumber = null,
    string? SectionTitle = null,
    int PageNumber = 1,
    string ChunkText = "",
    bool IsApproved = true
);

/// <summary>
/// Structured representation of a parsed citation reference.
/// </summary>
/// <param name="ProtocolVersion">Referenced protocol version.</param>
/// <param name="SectionNumber">Referenced section number.</param>
/// <param name="SectionTitle">Resolved section title.</param>
/// <param name="PageNumber">Referenced page number.</param>
/// <param name="CitationMarker">Exact verbatim citation string, e.g., '[Protocol v2.1, Section 7.3, Page 42]'.</param>
/// <param name="IsApproved">True if referenced chunk belongs to an approved version.</param>
/// <param name="IsVerified">True if coordinates match an existing chunk in the Knowledge Hub.</param>
public record ParsedCitation(
    string ProtocolVersion,
    string? SectionNumber,
    string? SectionTitle,
    int PageNumber,
    string CitationMarker,
    bool IsApproved,
    bool IsVerified
);
