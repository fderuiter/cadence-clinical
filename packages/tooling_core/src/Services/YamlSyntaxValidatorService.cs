using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Cadence.Topology;

namespace Cadence.ToolingCore.Services;

/// <summary>
/// Native-AOT-safe YAML 1.2 structural and syntax validator.
/// Checks indentation, tabs, quoted scalars and escapes, flow collections, block scalars,
/// mapping separators, and duplicate mapping keys without reflection or external packages.
/// </summary>
public static partial class YamlSyntaxValidatorService
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [GeneratedRegex(@"^[|>](?:[+-][1-9]?|[1-9][+-]?)?(?:\s+#.*|\s*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidBlockScalarHeaderRegex();

    public static int ValidateYaml(
        string[]? targetFiles = null,
        string? repoRoot = null,
        bool preCommit = false,
        GitIndexReader? indexReader = null)
    {
        repoRoot ??= FindRepoRoot();
        if (targetFiles is { Length: 0 })
        {
            return 0;
        }

        var reader = GitIndexReader.ResolvePreCommitReader(repoRoot, preCommit, indexReader, out var ownedReader);
        using (ownedReader)
        {
            IReadOnlyList<string> yamlFiles;
            if (preCommit)
            {
                var staged = targetFiles is not null
                    ? (IEnumerable<string>)targetFiles
                    : PreCommitOpenApiService.GetStagedFiles(repoRoot);
                yamlFiles = ResolveTargetFiles(repoRoot, staged, reader);
                if (yamlFiles.Count == 0)
                {
                    return 0;
                }
            }
            else if (targetFiles is not null)
            {
                yamlFiles = ResolveTargetFiles(repoRoot, targetFiles, reader: null);
                if (yamlFiles.Count == 0)
                {
                    return 0;
                }
            }
            else
            {
                yamlFiles = DiscoverAllYamlFiles(repoRoot);
            }

            var errors = new List<string>();
            foreach (string file in yamlFiles)
            {
                string displayPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                IReadOnlyList<string> fileErrors;
                if (reader is not null)
                {
                    fileErrors = reader.TryReadBytes(displayPath, out byte[] stagedBytes)
                        ? ValidateBytes(stagedBytes, displayPath)
                        : [];
                }
                else
                {
                    fileErrors = ValidateFile(file, displayPath);
                }

                foreach (string error in fileErrors)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count > 0)
            {
                Console.WriteLine("[YAML] Syntax validation failed:");
                foreach (string error in errors)
                {
                    Console.WriteLine($"  - {error}");
                }

                return 1;
            }

            return 0;
        }
    }

    public static IReadOnlyList<string> ValidateFile(string filePath, string? displayPath = null)
    {
        displayPath ??= filePath;
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(filePath);
        }
        catch (IOException ex)
        {
            return [$"{displayPath}:1: Could not read file ({ex.Message})."];
        }

        return ValidateBytes(bytes, displayPath);
    }

    public static IReadOnlyList<string> ValidateBytes(byte[] bytes, string displayPath = "<yaml>")
    {
        string content;
        try
        {
            content = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return [$"{displayPath}:1: File is not valid UTF-8."];
        }

        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }

        return ValidateContent(content, displayPath);
    }

    public static IReadOnlyList<string> ValidateContent(string content, string displayPath = "<yaml>")
    {
        var errors = new List<string>();
        string[] lines = content.ReplaceLineEndings("\n").Split('\n');

        var stack = new Stack<BlockFrame>();
        var flowStack = new Stack<(char Open, int Line)>();
        char activeQuote = '\0';
        int quoteStartLine = 0;

        bool inBlockScalar = false;
        int blockScalarMinIndent = 0;
        int? blockScalarDetectedIndent = null;

        bool allowChildIndent = true;
        bool lastMappingKeyHadEmptyValue = false;
        int lastMappingKeyIndent = -1;

        for (int idx = 0; idx < lines.Length; idx++)
        {
            int lineNo = idx + 1;
            string line = lines[idx];

            // 1. Inside a multiline quoted scalar
            if (activeQuote != '\0')
            {
                if (IsDocumentMarkerAtColumnZero(line) && activeQuote != '\0')
                {
                    errors.Add($"{displayPath}:{quoteStartLine}: Unclosed {(activeQuote == '"' ? "double" : "single")}-quoted string before document marker on line {lineNo}.");
                    return errors;
                }

                if (!ScanQuotedContinuation(line, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors))
                {
                    return errors;
                }

                continue;
            }

            // 2. Inside a block scalar (| or >)
            if (inBlockScalar)
            {
                if (line.Trim().Length == 0)
                {
                    if (blockScalarDetectedIndent is int detected && line.Length > detected && line.Contains('\t', StringComparison.Ordinal))
                    {
                        errors.Add($"{displayPath}:{lineNo}: Tab character in blank line indentation inside block scalar.");
                        return errors;
                    }

                    continue;
                }

                int spaceIndent = CountLeadingSpaces(line);
                if (spaceIndent >= blockScalarMinIndent && !IsDocumentMarkerAtColumnZero(line))
                {
                    blockScalarDetectedIndent ??= spaceIndent;
                    if (spaceIndent >= blockScalarDetectedIndent.Value)
                    {
                        continue;
                    }

                    // Non-empty line with fewer spaces than the block scalar's first content line,
                    // yet still deeper than the parent: invalid under-indented block scalar line!
                    if (spaceIndent > blockScalarMinIndent - 1)
                    {
                        errors.Add($"{displayPath}:{lineNo}: Inconsistent indentation inside block scalar (expected at least {blockScalarDetectedIndent.Value} spaces, found {spaceIndent}).");
                        return errors;
                    }
                }

                inBlockScalar = false;
                blockScalarDetectedIndent = null;
            }

            // 3. Inside a multi-line flow collection ([...] or {...})
            if (flowStack.Count > 0)
            {
                int leadSpaces = CountLeadingSpaces(line);
                if (leadSpaces < line.Length && line[leadSpaces] == '\t' && line.TrimStart().Length > 0 && !line.TrimStart().StartsWith('#'))
                {
                    errors.Add($"{displayPath}:{lineNo}: Tabs are not allowed for YAML indentation.");
                    return errors;
                }

                if (!ScanExpression(line[leadSpaces..], lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _))
                {
                    return errors;
                }

                if (flowStack.Count == 0)
                {
                    allowChildIndent = false;
                    lastMappingKeyHadEmptyValue = false;
                }

                continue;
            }

            // 4. Blank or comment-only line
            string trimmedStart = line.TrimStart(' ', '\t');
            if (trimmedStart.Length == 0 || trimmedStart.StartsWith('#'))
            {
                continue;
            }

            // 5. Tab indentation check on content lines
            int indent = CountLeadingSpaces(line);
            if (indent < line.Length && line[indent] == '\t')
            {
                errors.Add($"{displayPath}:{lineNo}: Tabs are not allowed for YAML indentation.");
                return errors;
            }

            // 6. Directives & Document markers at column 0
            if (indent == 0 && line.StartsWith('%'))
            {
                if (stack.Count > 0)
                {
                    errors.Add($"{displayPath}:{lineNo}: YAML directive '%' is only valid before '---' document start.");
                    return errors;
                }

                continue;
            }

            if (indent == 0 && IsDocumentStartMarker(line))
            {
                stack.Clear();
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = false;
                lastMappingKeyIndent = -1;
                string restAfterMarker = line[3..].TrimStart(' ', '\t');
                if (restAfterMarker.Length > 0 && !restAfterMarker.StartsWith('#'))
                {
                    if (StartsBlockScalar(restAfterMarker, displayPath, lineNo, errors, out int explicitIndent))
                    {
                        inBlockScalar = true;
                        blockScalarMinIndent = explicitIndent > 0 ? explicitIndent : 1;
                        blockScalarDetectedIndent = explicitIndent > 0 ? explicitIndent : null;
                    }
                    else if (!ScanExpression(restAfterMarker, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _))
                    {
                        return errors;
                    }
                }

                continue;
            }

            if (indent == 0 && IsDocumentEndMarker(line))
            {
                string restAfterEnd = line[3..].TrimStart(' ', '\t');
                if (restAfterEnd.Length > 0 && !restAfterEnd.StartsWith('#'))
                {
                    errors.Add($"{displayPath}:{lineNo}: Unexpected content after '...' document end marker.");
                    return errors;
                }

                stack.Clear();
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = false;
                lastMappingKeyIndent = -1;
                continue;
            }

            // 7. Process block line at `indent`
            if (!ProcessBlockSegment(
                    line,
                    indent,
                    lineNo,
                    displayPath,
                    stack,
                    flowStack,
                    ref activeQuote,
                    ref quoteStartLine,
                    ref inBlockScalar,
                    ref blockScalarMinIndent,
                    ref blockScalarDetectedIndent,
                    ref allowChildIndent,
                    ref lastMappingKeyHadEmptyValue,
                    ref lastMappingKeyIndent,
                    errors))
            {
                return errors;
            }
        }

        if (activeQuote != '\0')
        {
            errors.Add($"{displayPath}:{quoteStartLine}: Unclosed {(activeQuote == '"' ? "double" : "single")}-quoted string.");
        }
        else if (flowStack.Count > 0)
        {
            var (open, openLine) = flowStack.Peek();
            errors.Add($"{displayPath}:{openLine}: Unclosed flow collection '{open}'.");
        }

        return errors;
    }

    private static bool ProcessBlockSegment(
        string fullLine,
        int indent,
        int lineNo,
        string displayPath,
        Stack<BlockFrame> stack,
        Stack<(char Open, int Line)> flowStack,
        ref char activeQuote,
        ref int quoteStartLine,
        ref bool inBlockScalar,
        ref int blockScalarMinIndent,
        ref int? blockScalarDetectedIndent,
        ref bool allowChildIndent,
        ref bool lastMappingKeyHadEmptyValue,
        ref int lastMappingKeyIndent,
        List<string> errors)
    {
        string segment = fullLine[indent..];

        bool poppedDeeper = false;
        while (stack.Count > 0 && stack.Peek().Indent > indent)
        {
            stack.Pop();
            poppedDeeper = true;
        }

        bool isSeqItem = segment == "-" || (segment.Length > 1 && segment[0] == '-' && (segment[1] == ' ' || segment[1] == '\t'));
        bool isExplicitKey = segment == "?" || (segment.Length > 1 && segment[0] == '?' && (segment[1] == ' ' || segment[1] == '\t'));

        if (isSeqItem)
        {
            if (stack.Count > 0 && stack.Peek().Indent == indent)
            {
                var top = stack.Peek();
                if (top.Kind == BlockKind.Mapping && !(lastMappingKeyHadEmptyValue && lastMappingKeyIndent == indent))
                {
                    errors.Add($"{displayPath}:{lineNo}: Sequence item '-' cannot share indentation column {indent + 1} with mapping entries.");
                    return false;
                }
            }
            else if (poppedDeeper && stack.Count > 0 && stack.Peek().Indent < indent)
            {
                // Dedented to an intermediate column that matches no active block frame
                errors.Add($"{displayPath}:{lineNo}: Bad indentation at column {indent + 1} (does not match any outer block level).");
                return false;
            }
            else if (!poppedDeeper && stack.Count > 0 && stack.Peek().Indent < indent && !allowChildIndent)
            {
                errors.Add($"{displayPath}:{lineNo}: Unexpected nested sequence item at column {indent + 1}.");
                return false;
            }

            if (stack.Count == 0 || stack.Peek().Indent < indent)
            {
                stack.Push(new BlockFrame(indent, BlockKind.Sequence, null));
            }

            lastMappingKeyHadEmptyValue = false;
            lastMappingKeyIndent = -1;

            string afterDash = segment.Length > 1 ? segment[1..] : "";
            int spacesAfterDash = CountLeadingSpaces(afterDash);
            if (spacesAfterDash < afterDash.Length && afterDash[spacesAfterDash] == '\t')
            {
                // Tabs as separation space after '-' are allowed in YAML 1.2 if followed by content, consume whitespace
                int ws = 0;
                while (ws < afterDash.Length && (afterDash[ws] == ' ' || afterDash[ws] == '\t'))
                {
                    ws++;
                }

                spacesAfterDash = ws;
            }

            string itemText = afterDash[spacesAfterDash..];
            if (itemText.Length == 0 || itemText.StartsWith('#'))
            {
                allowChildIndent = true;
                return true;
            }

            int itemIndent = indent + 1 + spacesAfterDash;
            allowChildIndent = true;
            return ProcessBlockSegment(
                new string(' ', itemIndent) + itemText,
                itemIndent,
                lineNo,
                displayPath,
                stack,
                flowStack,
                ref activeQuote,
                ref quoteStartLine,
                ref inBlockScalar,
                ref blockScalarMinIndent,
                ref blockScalarDetectedIndent,
                ref allowChildIndent,
                ref lastMappingKeyHadEmptyValue,
                ref lastMappingKeyIndent,
                errors);
        }

        if (isExplicitKey)
        {
            if (stack.Count == 0 || stack.Peek().Indent < indent)
            {
                stack.Push(new BlockFrame(indent, BlockKind.Mapping, new HashSet<string>(StringComparer.Ordinal)));
            }

            allowChildIndent = true;
            lastMappingKeyHadEmptyValue = true;
            lastMappingKeyIndent = indent;
            string afterQ = segment.Length > 1 ? segment[1..].TrimStart(' ', '\t') : "";
            if (afterQ.Length > 0 && !afterQ.StartsWith('#'))
            {
                return ScanExpression(afterQ, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _);
            }

            return true;
        }

        // Check if segment is a mapping entry `key: ...`
        if (TrySplitBlockMapping(segment, lineNo, displayPath, errors, out string? keyToken, out string? valuePart, out bool fatalError))
        {
            if (fatalError)
            {
                return false;
            }

            if (stack.Count > 0 && stack.Peek().Indent == indent)
            {
                var top = stack.Peek();
                if (top.Kind != BlockKind.Mapping)
                {
                    errors.Add($"{displayPath}:{lineNo}: Mapping entry '{keyToken}:' cannot share indentation column {indent + 1} with sequence items.");
                    return false;
                }
            }
            else if (poppedDeeper)
            {
                errors.Add($"{displayPath}:{lineNo}: Bad indentation at column {indent + 1} (does not match any outer mapping level).");
                return false;
            }
            else if (stack.Count > 0 && stack.Peek().Indent < indent && !allowChildIndent)
            {
                errors.Add($"{displayPath}:{lineNo}: Unexpected indented mapping entry '{keyToken}:' at column {indent + 1}.");
                return false;
            }

            if (stack.Count == 0 || stack.Peek().Indent < indent)
            {
                stack.Push(new BlockFrame(indent, BlockKind.Mapping, new HashSet<string>(StringComparer.Ordinal)));
            }

            var mappingFrame = stack.Peek();
            string normalizedKey = NormalizeKey(keyToken!);
            if (normalizedKey.Length == 0)
            {
                errors.Add($"{displayPath}:{lineNo}: Empty mapping key before ':'.");
                return false;
            }

            if (IsReservedPlainIndicator(keyToken!))
            {
                errors.Add($"{displayPath}:{lineNo}: Plain scalar key '{keyToken}' cannot start with reserved indicator '{keyToken![0]}'.");
                return false;
            }

            if (normalizedKey != "<<" && mappingFrame.Keys is not null && !mappingFrame.Keys.Add(normalizedKey))
            {
                errors.Add($"{displayPath}:{lineNo}: Duplicate mapping key '{normalizedKey}'.");
                return false;
            }

            string trimmedValue = (valuePart ?? "").TrimStart(' ', '\t');
            if (trimmedValue.Length == 0 || trimmedValue.StartsWith('#'))
            {
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = true;
                lastMappingKeyIndent = indent;
                return true;
            }

            lastMappingKeyHadEmptyValue = false;
            lastMappingKeyIndent = -1;

            // Strip optional anchor/tag prefix (`&anchor`, `!tag`) before block scalar check
            string valueAfterProps = StripLeadingNodeProperties(trimmedValue);
            if (valueAfterProps.Length == 0 || valueAfterProps.StartsWith('#'))
            {
                allowChildIndent = true;
                lastMappingKeyHadEmptyValue = true;
                lastMappingKeyIndent = indent;
                return true;
            }

            if (valueAfterProps[0] is '|' or '>')
            {
                if (!StartsBlockScalar(valueAfterProps, displayPath, lineNo, errors, out int explicitIndent))
                {
                    return false;
                }

                inBlockScalar = true;
                blockScalarMinIndent = explicitIndent > 0 ? indent + explicitIndent : indent + 1;
                blockScalarDetectedIndent = explicitIndent > 0 ? indent + explicitIndent : null;
                allowChildIndent = false;
                return true;
            }

            if (IsReservedPlainIndicator(valueAfterProps))
            {
                errors.Add($"{displayPath}:{lineNo}: Plain scalar value cannot start with reserved indicator '{valueAfterProps[0]}'.");
                return false;
            }

            if (!ScanExpression(valueAfterProps, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out bool hadNestedUnquotedMappingColon))
            {
                return false;
            }

            if (hadNestedUnquotedMappingColon)
            {
                errors.Add($"{displayPath}:{lineNo}: Nested mapping colon ':' is not allowed in an unquoted mapping value on the same line.");
                return false;
            }

            // Plain scalar values in YAML can wrap across indented continuation lines!
            bool startsQuotedOrFlow = valueAfterProps[0] is '"' or '\'' or '[' or '{';
            allowChildIndent = !startsQuotedOrFlow || activeQuote != '\0' || flowStack.Count > 0;
            return true;
        }

        if (fatalError)
        {
            return false;
        }

        // Plain/quoted/flow scalar line (either root scalar or multiline plain scalar continuation)
        if (IsReservedPlainIndicator(segment))
        {
            errors.Add($"{displayPath}:{lineNo}: Plain scalar cannot start with reserved indicator '{segment[0]}'.");
            return false;
        }

        if (stack.Count > 0)
        {
            var top = stack.Peek();
            if (indent <= top.Indent && top.Kind != BlockKind.Scalar)
            {
                errors.Add($"{displayPath}:{lineNo}: Expected mapping key or sequence item at column {indent + 1}.");
                return false;
            }

            if (indent > top.Indent && !allowChildIndent)
            {
                errors.Add($"{displayPath}:{lineNo}: Unexpected indented scalar at column {indent + 1}.");
                return false;
            }
        }
        else
        {
            stack.Push(new BlockFrame(indent, BlockKind.Scalar, null));
        }

        if (segment[0] is '|' or '>')
        {
            if (!StartsBlockScalar(segment, displayPath, lineNo, errors, out int explicitIndent))
            {
                return false;
            }

            inBlockScalar = true;
            blockScalarMinIndent = explicitIndent > 0 ? indent + explicitIndent : indent + 1;
            blockScalarDetectedIndent = explicitIndent > 0 ? indent + explicitIndent : null;
            allowChildIndent = false;
            return true;
        }

        if (!ScanExpression(segment, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _))
        {
            return false;
        }

        return true;
    }

    private static bool TrySplitBlockMapping(
        string segment,
        int lineNo,
        string displayPath,
        List<string> errors,
        out string? keyToken,
        out string? valuePart,
        out bool fatalError)
    {
        keyToken = null;
        valuePart = null;
        fatalError = false;

        // Quoted key: "..." : ... or '...' : ...
        if (segment[0] is '"' or '\'')
        {
            char q = segment[0];
            int quoteIndex = 1;
            while (quoteIndex < segment.Length)
            {
                if (q == '"' && segment[quoteIndex] == '\\')
                {
                    if (!ValidateDoubleQuoteEscape(segment, ref quoteIndex, lineNo, displayPath, errors))
                    {
                        fatalError = true;
                        return false;
                    }

                    quoteIndex++;
                    continue;
                }

                if (q == '\'' && segment[quoteIndex] == '\'' && quoteIndex + 1 < segment.Length && segment[quoteIndex + 1] == '\'')
                {
                    quoteIndex += 2;
                    continue;
                }

                if (segment[quoteIndex] == q)
                {
                    int afterQuote = quoteIndex + 1;
                    while (afterQuote < segment.Length && (segment[afterQuote] == ' ' || segment[afterQuote] == '\t'))
                    {
                        afterQuote++;
                    }

                    if (afterQuote < segment.Length && segment[afterQuote] == ':' &&
                        (afterQuote + 1 == segment.Length || segment[afterQuote + 1] == ' ' || segment[afterQuote + 1] == '\t'))
                    {
                        keyToken = segment[..(quoteIndex + 1)];
                        valuePart = segment[(afterQuote + 1)..];
                        return true;
                    }

                    return false;
                }

                quoteIndex++;
            }

            // Multiline quoted scalar (not a single-line mapping key)
            return false;
        }

        // Unquoted key: scan at flow depth 0 for ':' followed by EOL or space/tab
        int flowDepth = 0;
        char inlineQuote = '\0';
        int i = 0;
        while (i < segment.Length)
        {
            char c = segment[i];
            if (inlineQuote != '\0')
            {
                if (inlineQuote == '"' && c == '\\')
                {
                    i += 2;
                    continue;
                }

                if (inlineQuote == '\'' && c == '\'' && i + 1 < segment.Length && segment[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == inlineQuote)
                {
                    inlineQuote = '\0';
                }

                i++;
                continue;
            }

            if (c is '"' or '\'' && flowDepth > 0)
            {
                inlineQuote = c;
                i++;
                continue;
            }

            if (c is '[' or '{')
            {
                // If the line starts with `[` or `{`, it's a flow collection unless closed and followed by `: `
                flowDepth++;
                i++;
                continue;
            }

            if (c is ']' or '}' && flowDepth > 0)
            {
                flowDepth--;
                i++;
                continue;
            }

            if (flowDepth == 0 && c == '#' && (i == 0 || segment[i - 1] is ' ' or '\t'))
            {
                break;
            }

            if (flowDepth == 0 && c == ':' && (i + 1 == segment.Length || segment[i + 1] is ' ' or '\t'))
            {
                keyToken = segment[..i].Trim();
                valuePart = segment[(i + 1)..];
                return true;
            }

            i++;
        }

        return false;
    }

    private static bool ScanQuotedContinuation(
        string line,
        int lineNo,
        string displayPath,
        ref char activeQuote,
        ref int quoteStartLine,
        Stack<(char Open, int Line)> flowStack,
        List<string> errors)
    {
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (activeQuote == '"' && c == '\\')
            {
                if (!ValidateDoubleQuoteEscape(line, ref i, lineNo, displayPath, errors))
                {
                    return false;
                }

                i++;
                continue;
            }

            if (activeQuote == '\'' && c == '\'' && i + 1 < line.Length && line[i + 1] == '\'')
            {
                i += 2;
                continue;
            }

            if (c == activeQuote)
            {
                activeQuote = '\0';
                string trailing = line[(i + 1)..];
                return ValidateAfterClosedQuote(trailing, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors);
            }

            i++;
        }

        return true;
    }

    private static bool ScanExpression(
        string text,
        int lineNo,
        string displayPath,
        ref char activeQuote,
        ref int quoteStartLine,
        Stack<(char Open, int Line)> flowStack,
        List<string> errors,
        out bool hadTopLevelMappingColon)
    {
        hadTopLevelMappingColon = false;
        bool startsWithQuote = text.Length > 0 && text[0] is '"' or '\'' && flowStack.Count == 0;

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (activeQuote != '\0')
            {
                if (activeQuote == '"' && c == '\\')
                {
                    if (!ValidateDoubleQuoteEscape(text, ref i, lineNo, displayPath, errors))
                    {
                        return false;
                    }

                    i++;
                    continue;
                }

                if (activeQuote == '\'' && c == '\'' && i + 1 < text.Length && text[i + 1] == '\'')
                {
                    i += 2;
                    continue;
                }

                if (c == activeQuote)
                {
                    activeQuote = '\0';
                    if (startsWithQuote && flowStack.Count == 0)
                    {
                        return ValidateAfterClosedQuote(text[(i + 1)..], lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors);
                    }
                }

                i++;
                continue;
            }

            if (c == '#' && (i == 0 || text[i - 1] is ' ' or '\t'))
            {
                return true;
            }

            if (c is '"' or '\'')
            {
                // In YAML plain scalars, an apostrophe inside a word (e.g. `don't` or `'foo'` inside a plain string) is plain text
                // ONLY when `'`/`"` did not start the token (after start, space, `[`, `{`, `,`, `:`).
                bool canOpenQuote = i == 0 || text[i - 1] is ' ' or '\t' or '[' or '{' or ',' or ':';
                if (canOpenQuote)
                {
                    activeQuote = c;
                    quoteStartLine = lineNo;
                }

                i++;
                continue;
            }

            if (c is '[' or '{')
            {
                // In plain scalars (when flowStack is empty), `[` or `{` only starts a flow collection at token start
                // OR inside `${{ ... }}` expressions! Either way, matching brackets balance cleanly.
                if (flowStack.Count > 0 || i == 0 || (i > 0 && text[i - 1] == '$'))
                {
                    flowStack.Push((c, lineNo));
                }

                i++;
                continue;
            }

            if (c is ']' or '}')
            {
                if (flowStack.Count > 0)
                {
                    var (open, _) = flowStack.Peek();
                    char expected = open == '[' ? ']' : '}';
                    if (c != expected)
                    {
                        errors.Add($"{displayPath}:{lineNo}: Mismatched flow bracket '{c}' (expected '{expected}' to close '{open}').");
                        return false;
                    }

                    flowStack.Pop();
                }
                else if (i == 0)
                {
                    errors.Add($"{displayPath}:{lineNo}: Unexpected closing flow bracket '{c}'.");
                    return false;
                }

                i++;
                continue;
            }

            if (flowStack.Count == 0 && c == ':' && (i + 1 == text.Length || text[i + 1] is ' ' or '\t'))
            {
                hadTopLevelMappingColon = true;
            }

            i++;
        }

        return true;
    }

    private static bool ValidateAfterClosedQuote(
        string trailing,
        int lineNo,
        string displayPath,
        ref char activeQuote,
        ref int quoteStartLine,
        Stack<(char Open, int Line)> flowStack,
        List<string> errors)
    {
        if (flowStack.Count > 0)
        {
            return ScanExpression(trailing, lineNo, displayPath, ref activeQuote, ref quoteStartLine, flowStack, errors, out _);
        }

        string trimmed = trailing.TrimStart(' ', '\t');
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return true;
        }

        errors.Add($"{displayPath}:{lineNo}: Unexpected trailing content '{trimmed}' after quoted scalar.");
        return false;
    }

    private static bool ValidateDoubleQuoteEscape(string text, ref int backslashIndex, int lineNo, string displayPath, List<string> errors)
    {
        if (backslashIndex + 1 >= text.Length)
        {
            // Backslash at end of line escapes the newline in YAML double-quoted strings.
            return true;
        }

        char next = text[backslashIndex + 1];
        int hexDigits = next switch
        {
            'x' => 2,
            'u' => 4,
            'U' => 8,
            _ => 0,
        };

        if (hexDigits > 0)
        {
            if (backslashIndex + 1 + hexDigits >= text.Length)
            {
                errors.Add($"{displayPath}:{lineNo}: Incomplete '\\{next}' Unicode escape sequence in double-quoted string.");
                return false;
            }

            for (int h = 0; h < hexDigits; h++)
            {
                if (!Uri.IsHexDigit(text[backslashIndex + 2 + h]))
                {
                    errors.Add($"{displayPath}:{lineNo}: Invalid hex digit in '\\{next}' escape sequence in double-quoted string.");
                    return false;
                }
            }

            backslashIndex += 1 + hexDigits;
            return true;
        }

        bool validSimple = next is '0' or 'a' or 'b' or 't' or '\t' or 'n' or 'v' or 'f' or 'r' or 'e' or ' ' or '"' or '/' or '\\' or 'N' or '_' or 'L' or 'P';
        if (!validSimple)
        {
            errors.Add($"{displayPath}:{lineNo}: Invalid escape sequence '\\{next}' in double-quoted string.");
            return false;
        }

        backslashIndex += 1;
        return true;
    }

    private static bool StartsBlockScalar(string value, string displayPath, int lineNo, List<string> errors, out int explicitIndent)
    {
        explicitIndent = 0;
        if (!ValidBlockScalarHeaderRegex().IsMatch(value))
        {
            errors.Add($"{displayPath}:{lineNo}: Invalid block scalar header '{value}'.");
            return false;
        }

        for (int i = 1; i < value.Length && !char.IsWhiteSpace(value[i]) && value[i] != '#'; i++)
        {
            if (value[i] is >= '1' and <= '9')
            {
                explicitIndent = value[i] - '0';
            }
        }

        return true;
    }

    private static string StripLeadingNodeProperties(string value)
    {
        string current = value;
        while (current.Length > 0 && (current[0] == '&' || current[0] == '!'))
        {
            int space = 0;
            while (space < current.Length && current[space] is not ' ' and not '\t')
            {
                space++;
            }

            current = current[space..].TrimStart(' ', '\t');
        }

        return current;
    }

    private static bool IsReservedPlainIndicator(string token) =>
        token.Length > 0 && token[0] is '@' or '`';

    private static string NormalizeKey(string rawKey)
    {
        string trimmed = rawKey.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static bool IsDocumentStartMarker(string line) =>
        line.StartsWith("---", StringComparison.Ordinal) &&
        (line.Length == 3 || line[3] is ' ' or '\t');

    private static bool IsDocumentEndMarker(string line) =>
        line.StartsWith("...", StringComparison.Ordinal) &&
        (line.Length == 3 || line[3] is ' ' or '\t');

    private static bool IsDocumentMarkerAtColumnZero(string line) =>
        IsDocumentStartMarker(line) || IsDocumentEndMarker(line);

    private static int CountLeadingSpaces(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] == ' ')
        {
            i++;
        }

        return i;
    }

    private static List<string> ResolveTargetFiles(
        string repoRoot,
        IEnumerable<string> candidates,
        GitIndexReader? reader)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string raw in candidates)
        {
            string? rel = GitIndexReader.TryNormalizeRelativePath(repoRoot, raw);
            if (string.IsNullOrEmpty(rel))
            {
                continue;
            }

            string fullPath = Path.Combine(repoRoot, rel);
            bool exists = reader is not null ? reader.FileExists(rel) : File.Exists(fullPath);
            if (!exists)
            {
                continue;
            }

            if (IsYamlFile(rel) && !IsExcludedDirectory(rel))
            {
                result.Add(fullPath);
            }
        }

        return [.. result];
    }

    private static List<string> DiscoverAllYamlFiles(string repoRoot)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(repoRoot, "*.*", options))
        {
            string rel = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
            if (IsYamlFile(rel) && !IsExcludedDirectory(rel))
            {
                files.Add(path);
            }
        }

        return [.. files];
    }

    private static bool IsYamlFile(string relativePath) =>
        relativePath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
        relativePath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    private static bool IsExcludedDirectory(string relativePath) =>
        relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("node_modules/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(".venv/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/.venv/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith(".cadence/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
        relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);

    private static string FindRepoRoot() => RepositoryRoot.Find();

    private enum BlockKind
    {
        Mapping,
        Sequence,
        Scalar,
    }

    private sealed record BlockFrame(int Indent, BlockKind Kind, HashSet<string>? Keys);
}
