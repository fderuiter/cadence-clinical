using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Cadence.CoreModels;

namespace Cadence.Compliance;

/// <summary>
/// Native Protocol RAG Service providing prompt formatting and faithfulness evaluation methods.
/// Delegating to <see cref="ProtocolRagEvaluator"/>.
/// Requirements: PRD-TCK-005, PRD-SYS-051, ADR-2192
/// </summary>
public class ProtocolRagService
{
    /// <summary>
    /// Formats canonical verbatim citation marker per PRD-TCK-005.
    /// </summary>
    [SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Instance method API consistency across compliance services")]
    public string FormatCitationMarker(string protocolVersion, string? sectionNumber, int pageNumber) =>
        ProtocolRagEvaluator.FormatCitationMarker(protocolVersion, sectionNumber, pageNumber);

    /// <summary>
    /// Formats the Tier 2 Grounded RAG user prompt containing structured excerpt blocks.
    /// </summary>
    [SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Instance method API consistency across compliance services")]
    public string FormatGroundedRagPrompt(string query, IReadOnlyList<ProtocolRagContextChunk>? chunks) =>
        ProtocolRagEvaluator.FormatGroundedRagPrompt(query, chunks);

    /// <summary>
    /// Computes mathematical faithfulness score and validates citation grounding.
    /// </summary>
    [SuppressMessage("SonarAnalyzer.CSharp", "S2325:Methods should be static", Justification = "Instance method API consistency across compliance services")]
    public (double FaithfulnessScore, IReadOnlyList<ParsedCitation> Citations, bool IsGrounded) EvaluateFaithfulness(
        string query, string answer, IReadOnlyList<ProtocolRagContextChunk>? chunks) =>
        ProtocolRagEvaluator.EvaluateFaithfulness(query, answer, chunks);
}
