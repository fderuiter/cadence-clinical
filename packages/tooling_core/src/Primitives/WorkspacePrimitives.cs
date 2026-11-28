using System;
using System.IO;
using System.Text;

using Cadence.ToolingCore.Services;

namespace Cadence.ToolingCore.Primitives;

/// <summary>
/// Shared AOT-safe workspace root discovery and UTF-8 file or staged Git-index snapshot readers.
/// </summary>
internal static class WorkspacePrimitives
{
    public static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static string FindRepoRoot(string? startDirectory = null)
    {
        string?[] candidates = string.IsNullOrWhiteSpace(startDirectory)
            ? [Directory.GetCurrentDirectory(), AppContext.BaseDirectory]
            : [startDirectory, Directory.GetCurrentDirectory(), AppContext.BaseDirectory];

        foreach (string? start in candidates)
        {
            string? current = start;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "Cadence.sln")))
                {
                    return current;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (parent == current)
                {
                    break;
                }

                current = parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    public static ReadOnlySpan<byte> StripBom(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes[3..]
            : bytes;

    public static string StripBom(string content) =>
        content.Length > 0 && content[0] == '\uFEFF' ? content[1..] : content;

    public static bool TryDecodeStrictUtf8(ReadOnlySpan<byte> bytes, out string content)
    {
        try
        {
            content = StripBom(StrictUtf8.GetString(bytes));
            return true;
        }
        catch (DecoderFallbackException)
        {
            content = string.Empty;
            return false;
        }
    }

    public static bool TryReadBytes(
        string repoRoot,
        string relativePath,
        GitIndexReader? indexReader,
        out byte[] bytes,
        out string? error)
    {
        if (indexReader is not null)
        {
            if (indexReader.TryReadBytes(relativePath, out bytes))
            {
                error = null;
                return true;
            }

            error = $"Staged index blob '{relativePath}' could not be read.";
            return false;
        }

        string fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            bytes = [];
            error = $"File '{relativePath}' does not exist.";
            return false;
        }

        try
        {
            bytes = File.ReadAllBytes(fullPath);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            bytes = [];
            error = ex.Message;
            return false;
        }
    }

    public static string? TryReadUtf8Text(
        string fullPath,
        string relativePath,
        GitIndexReader? indexReader,
        bool rejectBinaryNullBytes = false,
        bool stripBom = false)
    {
        byte[] bytes;
        if (indexReader is not null)
        {
            if (!indexReader.TryReadBytes(relativePath, out bytes))
            {
                return null;
            }
        }
        else
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }

            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        if (rejectBinaryNullBytes)
        {
            int probeLen = Math.Min(bytes.Length, 8192);
            for (int i = 0; i < probeLen; i++)
            {
                if (bytes[i] == 0)
                {
                    return null;
                }
            }
        }

        try
        {
            string decoded = StrictUtf8.GetString(bytes);
            return stripBom ? StripBom(decoded) : decoded;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
