using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cadence.ToolingCore.Services;

public sealed partial class GitIndexReader
{
    private bool TryReadBlobByOid(string oid, out byte[] bytes)
    {
        if (_blobCache.TryGetValue(oid, out byte[]? cached))
        {
            bytes = cached;
            return true;
        }

        bytes = [];
        if (_disposed || _batchUnavailable || !EnsureBatchProcess())
        {
            return false;
        }

        try
        {
            Stream stdin = _batchProcess!.StandardInput.BaseStream;
            byte[] request = Encoding.ASCII.GetBytes(oid + "\n");
            stdin.Write(request, 0, request.Length);
            stdin.Flush();

            Stream stdout = _batchProcess.StandardOutput.BaseStream;
            string? header = ReadAsciiLine(stdout);
            if (string.IsNullOrEmpty(header))
            {
                ResetBatchProcess();
                return false;
            }

            // Format: "<oid> <type> <size>" or "<oid> missing"
            string[] parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 ||
                string.Equals(parts[1], "missing", StringComparison.Ordinal) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int size) ||
                size < 0)
            {
                return false;
            }

            byte[] payload = new byte[size];
            if (!ReadExact(stdout, payload) || stdout.ReadByte() != '\n')
            {
                ResetBatchProcess();
                return false;
            }

            if (size <= MaxCachedBlobBytes - _cachedBytes)
            {
                _blobCache[oid] = payload;
                _cachedBytes += size;
            }

            bytes = payload;
            return true;
        }
        catch
        {
            ResetBatchProcess();
            return false;
        }
    }

    private bool EnsureBatchProcess()
    {
        if (_batchProcess is { HasExited: false })
        {
            return true;
        }

        ResetBatchProcess();

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repoRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("cat-file");
        psi.ArgumentList.Add("--batch");

        try
        {
            _batchProcess = Process.Start(psi);
            if (_batchProcess is null)
            {
                _batchUnavailable = true;
                return false;
            }

            return true;
        }
        catch
        {
            _batchUnavailable = true;
            return false;
        }
    }

    private void ResetBatchProcess()
    {
        if (_batchProcess is null)
        {
            return;
        }

        try
        {
            if (!_batchProcess.HasExited)
            {
                _batchProcess.StandardInput.BaseStream.Close();
                if (!_batchProcess.WaitForExit(1000))
                {
                    _batchProcess.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // Best-effort cleanup of helper process.
        }
        finally
        {
            _batchProcess.Dispose();
            _batchProcess = null;
        }
    }

    private static string? ReadAsciiLine(Stream stream)
    {
        var buffer = new List<byte>(80);
        while (true)
        {
            int b = stream.ReadByte();
            if (b < 0)
            {
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            }

            if (b == '\n')
            {
                if (buffer.Count > 0 && buffer[^1] == '\r')
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }

                return Encoding.ASCII.GetString(buffer.ToArray());
            }

            buffer.Add((byte)b);
        }
    }

    private static bool ReadExact(Stream stream, byte[] destination)
    {
        int offset = 0;
        while (offset < destination.Length)
        {
            int read = stream.Read(destination, offset, destination.Length - offset);
            if (read <= 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static bool TryReadStageEntries(
        string repoRoot,
        Dictionary<string, IndexEntry> entries,
        HashSet<string> directories)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("ls-files");
        psi.ArgumentList.Add("--stage");
        psi.ArgumentList.Add("-z");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            using var ms = new MemoryStream();
            process.StandardOutput.BaseStream.CopyTo(ms);
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return false;
            }

            ReadOnlySpan<byte> data = ms.ToArray();
            int start = 0;
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                {
                    continue;
                }

                ParseStageRecord(data[start..i], entries, directories);
                start = i + 1;
            }

            if (start < data.Length)
            {
                ParseStageRecord(data[start..], entries, directories);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ParseStageRecord(
        ReadOnlySpan<byte> recordBytes,
        Dictionary<string, IndexEntry> entries,
        HashSet<string> directories)
    {
        if (recordBytes.IsEmpty)
        {
            return;
        }

        int tabIndex = recordBytes.IndexOf((byte)'\t');
        if (tabIndex <= 0 || tabIndex + 1 >= recordBytes.Length)
        {
            return;
        }

        string meta = Encoding.ASCII.GetString(recordBytes[..tabIndex]);
        string path = Encoding.UTF8.GetString(recordBytes[(tabIndex + 1)..]).Replace('\\', '/');
        string[] metaParts = meta.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (metaParts.Length < 3)
        {
            return;
        }

        string mode = metaParts[0];
        string oid = metaParts[1];
        string stage = metaParts[2];

        // Prefer stage 0 (merged/staged); only record unmerged stage 2 ("ours") when stage 0 is absent.
        if (string.Equals(stage, "0", StringComparison.Ordinal) ||
            (string.Equals(stage, "2", StringComparison.Ordinal) && !entries.ContainsKey(path)))
        {
            entries[path] = new IndexEntry(oid, mode);
            AddAncestorDirectories(path, directories);
        }
    }

    private static void AddAncestorDirectories(string relativePath, HashSet<string> directories)
    {
        int slash = relativePath.LastIndexOf('/');
        while (slash > 0)
        {
            string dir = relativePath[..slash];
            if (!directories.Add(dir))
            {
                break;
            }

            slash = dir.LastIndexOf('/');
        }
    }

    private static bool HasHeadCommit(string repoRoot)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--no-optional-locks");
        psi.ArgumentList.Add("rev-parse");
        psi.ArgumentList.Add("--verify");
        psi.ArgumentList.Add("-q");
        psi.ArgumentList.Add("HEAD");

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
