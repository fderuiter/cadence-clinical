using System;

namespace Cadence.Apps.AiGateway.Services;

/// <summary>
/// Implementation of <see cref="IVectorEmbeddingService"/> generating 384-dimensional dense vector embeddings.
/// @req:PRD-SYS-001
/// </summary>
public sealed class VectorEmbeddingService : IVectorEmbeddingService
{
    public const int VectorDimension = 384;

    public float[] GenerateEmbedding(string text)
    {
        float[] vector = new float[VectorDimension];
        if (string.IsNullOrWhiteSpace(text))
        {
            return vector;
        }

        string normalized = text.Trim().ToLowerInvariant();

        // 1. Character n-gram feature hashing into 384 dimensions
        for (int n = 1; n <= 3; n++)
        {
            for (int i = 0; i <= normalized.Length - n; i++)
            {
                string ngram = normalized.Substring(i, n);
                int hash = GetStableHashCode(ngram);
                int index = Math.Abs(hash) % VectorDimension;
                float weight = 1.0f;
                if (n == 1)
                {
                    weight = 0.5f;
                }
                else if (n == 3)
                {
                    weight = 1.5f;
                }
                vector[index] += weight * (hash >= 0 ? 1.0f : -1.0f);
            }
        }

        // 2. Word-level semantic feature projection
        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            int wordHash = GetStableHashCode(word);
            for (int k = 0; k < 8; k++)
            {
                int index = Math.Abs(wordHash + k * 1000003) % VectorDimension;
                vector[index] += 2.0f;
            }
        }

        // 3. Normalize L2 length to 1.0
        double normSq = 0.0;
        for (int i = 0; i < VectorDimension; i++)
        {
            normSq += vector[i] * vector[i];
        }

        if (normSq > 1e-12)
        {
            float norm = (float)Math.Sqrt(normSq);
            for (int i = 0; i < VectorDimension; i++)
            {
                vector[i] /= norm;
            }
        }

        return vector;
    }

    public double CalculateCosineSimilarity(float[] vectorA, float[] vectorB)
    {
        ArgumentNullException.ThrowIfNull(vectorA);
        ArgumentNullException.ThrowIfNull(vectorB);

        int len = Math.Min(vectorA.Length, vectorB.Length);
        double dot = 0.0;
        for (int i = 0; i < len; i++)
        {
            dot += vectorA[i] * vectorB[i];
        }

        // Since vectors are unit-length normalized, dot product is cosine similarity
        return Math.Clamp(dot, 0.0, 1.0);
    }

    private static int GetStableHashCode(string str)
    {
        unchecked
        {
            int hash = 5381;
            foreach (char c in str)
            {
                hash = ((hash << 5) + hash) ^ c;
            }
            return hash;
        }
    }
}
