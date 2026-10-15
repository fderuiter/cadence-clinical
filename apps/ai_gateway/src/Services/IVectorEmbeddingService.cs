using System;

namespace Cadence.Apps.AiGateway.Services;

/// <summary>
/// Service contract for generating 384-dimensional dense vector embeddings.
/// @req:PRD-SYS-001
/// </summary>
public interface IVectorEmbeddingService
{
    /// <summary>
    /// Generates a normalized 384-dimensional dense vector embedding for the given input text.
    /// </summary>
    float[] GenerateEmbedding(string text);

    /// <summary>
    /// Calculates cosine similarity between two 384-dimensional unit vectors.
    /// </summary>
    double CalculateCosineSimilarity(float[] vectorA, float[] vectorB);
}
