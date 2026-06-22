namespace Tiro.Cli;

/// <summary>
/// Encodes/decodes embedding vectors to/from the little-endian float32 BLOB
/// format stored in semantic_embeddings.vector_blob. Deliberately has no
/// database or network dependency so it can be tested in isolation. See
/// docs/WP4C_NATIVE_SEMANTIC_ENGINE_DESIGN.md.
/// </summary>
public static class SemanticVectorCodec
{
    public static byte[] Encode(IReadOnlyList<float> vector)
    {
        if (vector.Count == 0)
        {
            throw new ArgumentException("Vector must not be empty.", nameof(vector));
        }

        var buffer = new byte[vector.Count * sizeof(float)];
        for (var i = 0; i < vector.Count; i++)
        {
            var bytes = BitConverter.GetBytes(vector[i]);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            Buffer.BlockCopy(bytes, 0, buffer, i * sizeof(float), sizeof(float));
        }

        return buffer;
    }

    public static float[] Decode(byte[] blob, int expectedDimensions)
    {
        if (blob.Length == 0)
        {
            throw new InvalidOperationException("Embedding vector BLOB is empty.");
        }

        if (blob.Length % sizeof(float) != 0)
        {
            throw new InvalidOperationException(
                $"Embedding vector BLOB length {blob.Length} is not a multiple of {sizeof(float)} bytes; data is corrupt.");
        }

        var dimensions = blob.Length / sizeof(float);
        if (dimensions != expectedDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding vector dimension mismatch: BLOB encodes {dimensions} floats but {expectedDimensions} were expected.");
        }

        var vector = new float[dimensions];
        for (var i = 0; i < dimensions; i++)
        {
            var bytes = new byte[sizeof(float)];
            Buffer.BlockCopy(blob, i * sizeof(float), bytes, 0, sizeof(float));
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            vector[i] = BitConverter.ToSingle(bytes, 0);
        }

        return vector;
    }

    /// <summary>
    /// Cosine similarity in [-1, 1] (callers treat negative values as below
    /// any reasonable threshold). Returns 0 for a zero-magnitude vector
    /// rather than dividing by zero — a zero vector should never match
    /// anything.
    /// </summary>
    public static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count != b.Count)
        {
            throw new InvalidOperationException($"Cannot compare vectors of differing dimension ({a.Count} vs {b.Count}).");
        }

        double dot = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;
        for (var i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
