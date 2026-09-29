using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cadence.Testing.Graph;

public class TestDependencyGraph
{
    private readonly string _repoRoot;
    private readonly string _cacheFile;

    public Dictionary<string, List<string>> ForwardGraph { get; private set; } = new();
    public Dictionary<string, List<string>> ReverseGraph { get; private set; } = new();
    public Dictionary<string, long> FileMtimes { get; private set; } = new();

    public TestDependencyGraph(string? repoRoot = null, string? cacheFile = null)
    {
        _repoRoot = repoRoot ?? FindRepoRoot(AppContext.BaseDirectory);
        _cacheFile = cacheFile ?? Path.Combine(_repoRoot, ".cadence", "test_graph.json");
    }

    public static string FindRepoRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Cadence.sln")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    public string NormalizeRelPath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            if (path.StartsWith(_repoRoot, StringComparison.OrdinalIgnoreCase))
            {
                string rel = path[_repoRoot.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return rel.Replace('\\', '/');
            }
            return path.Replace('\\', '/');
        }
        return path.Replace('\\', '/');
    }

    public List<string> ScanFiles()
    {
        var targetDirs = new[] { "apps", "packages", "scripts", "tests" };
        var found = new List<string>();

        foreach (var d in targetDirs)
        {
            string dirPath = Path.Combine(_repoRoot, d);
            if (Directory.Exists(dirPath))
            {
                foreach (var file in Directory.EnumerateFiles(dirPath, "*.cs", SearchOption.AllDirectories))
                {
                    string norm = file.Replace('\\', '/');
                    if (norm.Contains("/obj/") || norm.Contains("/bin/") || norm.Contains("/Migrations/"))
                    {
                        continue;
                    }
                    found.Add(file);
                }
            }
        }

        return found;
    }

    public static HashSet<string> ParseFileSymbols(string filePath)
    {
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            string text = File.ReadAllText(filePath);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(text);
            SyntaxNode root = tree.GetRoot();

            foreach (var node in root.DescendantNodes())
            {
                switch (node)
                {
                    case ClassDeclarationSyntax classDecl:
                        symbols.Add(classDecl.Identifier.Text);
                        break;
                    case RecordDeclarationSyntax recordDecl:
                        symbols.Add(recordDecl.Identifier.Text);
                        break;
                    case InterfaceDeclarationSyntax ifaceDecl:
                        symbols.Add(ifaceDecl.Identifier.Text);
                        break;
                    case StructDeclarationSyntax structDecl:
                        symbols.Add(structDecl.Identifier.Text);
                        break;
                    case UsingDirectiveSyntax usingDir:
                        if (usingDir.Name != null)
                        {
                            symbols.Add(usingDir.Name.ToString());
                        }
                        break;
                    case IdentifierNameSyntax identifierName:
                        symbols.Add(identifierName.Identifier.Text);
                        break;
                }
            }
        }
        catch
        {
            // Ignore parse errors on incomplete draft files
        }

        return symbols;
    }

    public void Build(bool force = false)
    {
        var files = ScanFiles();
        var fileSymbolMap = new Dictionary<string, HashSet<string>>();
        var definedTypeToFileMap = new Dictionary<string, string>();
        var forward = new Dictionary<string, HashSet<string>>();
        var reverse = new Dictionary<string, HashSet<string>>();
        var mtimes = new Dictionary<string, long>();

        foreach (var file in files)
        {
            string rel = NormalizeRelPath(file);
            var fi = new FileInfo(file);
            mtimes[rel] = fi.LastWriteTimeUtc.Ticks;

            var symbols = ParseFileSymbols(file);
            fileSymbolMap[rel] = symbols;

            // Register defined types in this file
            try
            {
                string text = File.ReadAllText(file);
                SyntaxTree tree = CSharpSyntaxTree.ParseText(text);
                SyntaxNode root = tree.GetRoot();
                foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                {
                    definedTypeToFileMap[typeDecl.Identifier.Text] = rel;
                }
            }
            catch
            {
                // Ignore
            }

            if (!forward.ContainsKey(rel)) forward[rel] = new HashSet<string>();
            if (!reverse.ContainsKey(rel)) reverse[rel] = new HashSet<string>();
        }

        // Build edges based on type usage
        foreach (var (rel, symbols) in fileSymbolMap)
        {
            foreach (var symbol in symbols)
            {
                if (definedTypeToFileMap.TryGetValue(symbol, out var targetFile) && targetFile != rel)
                {
                    forward[rel].Add(targetFile);

                    if (!reverse.ContainsKey(targetFile))
                    {
                        reverse[targetFile] = new HashSet<string>();
                    }
                    reverse[targetFile].Add(rel);
                }
            }
        }

        ForwardGraph = forward.ToDictionary(k => k.Key, v => v.Value.OrderBy(x => x).ToList());
        ReverseGraph = reverse.ToDictionary(k => k.Key, v => v.Value.OrderBy(x => x).ToList());
        FileMtimes = mtimes;

        SaveCache();
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            var payload = new CachePayload
            {
                ForwardGraph = ForwardGraph,
                ReverseGraph = ReverseGraph,
                FileMtimes = FileMtimes
            };
            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFile, json);
        }
        catch
        {
            // Ignore cache write failures
        }
    }

    public void LoadOrBuild()
    {
        if (!File.Exists(_cacheFile))
        {
            Build();
            return;
        }

        try
        {
            string json = File.ReadAllText(_cacheFile);
            var payload = JsonSerializer.Deserialize<CachePayload>(json);
            if (payload != null)
            {
                ForwardGraph = payload.ForwardGraph ?? new();
                ReverseGraph = payload.ReverseGraph ?? new();
                FileMtimes = payload.FileMtimes ?? new();
            }
            else
            {
                Build();
            }
        }
        catch
        {
            Build();
        }
    }

    public HashSet<string> GetTransitiveDependents(string relPath)
    {
        string norm = NormalizeRelPath(relPath);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(norm);

        while (queue.Count > 0)
        {
            string curr = queue.Dequeue();
            if (ReverseGraph.TryGetValue(curr, out var dependents))
            {
                foreach (var dep in dependents)
                {
                    if (visited.Add(dep))
                    {
                        queue.Enqueue(dep);
                    }
                }
            }
        }

        return visited;
    }

    public List<string> ResolveAffectedTests(IEnumerable<string> modifiedFiles)
    {
        if (ForwardGraph.Count == 0)
        {
            LoadOrBuild();
        }

        var affectedTests = new HashSet<string>(StringComparer.Ordinal);

        foreach (var modifiedFile in modifiedFiles)
        {
            string rel = NormalizeRelPath(modifiedFile);

            // 1. If modified file is already a test file
            if (IsTestFile(rel) && File.Exists(Path.Combine(_repoRoot, rel)))
            {
                affectedTests.Add(rel);
            }

            // 2. Transitive dependents
            var transitive = GetTransitiveDependents(rel);
            foreach (var dep in transitive)
            {
                if (IsTestFile(dep) && File.Exists(Path.Combine(_repoRoot, dep)))
                {
                    affectedTests.Add(dep);
                }
            }

            // 3. Fallback heuristic: map app/package to corresponding tests folder or suite
            if (affectedTests.Count == 0)
            {
                var parts = rel.Split('/');
                if (parts.Length > 1 && (parts[0] == "apps" || parts[0] == "packages"))
                {
                    string serviceName = parts[1];
                    string testDirRel = $"tests/{Capitalize(serviceName)}";
                    if (Directory.Exists(Path.Combine(_repoRoot, testDirRel)))
                    {
                        affectedTests.Add(testDirRel);
                    }
                    else
                    {
                        affectedTests.Add("tests/Cadence.UnitTests.csproj");
                    }
                }
            }
        }

        return affectedTests.OrderBy(x => x).ToList();
    }

    private static bool IsTestFile(string path)
    {
        string name = Path.GetFileName(path);
        return name.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("tests/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Capitalize(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        return char.ToUpperInvariant(str[0]) + str[1..];
    }

    private class CachePayload
    {
        public Dictionary<string, List<string>>? ForwardGraph { get; set; }
        public Dictionary<string, List<string>>? ReverseGraph { get; set; }
        public Dictionary<string, long>? FileMtimes { get; set; }
    }
}
