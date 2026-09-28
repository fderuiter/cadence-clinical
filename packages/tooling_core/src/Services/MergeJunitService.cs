using System;
using System.IO;
using System.Xml.Linq;

namespace Cadence.ToolingCore.Services;

public static class MergeJunitService
{
    public static int MergeJunitReports(string outputFile, string[] inputFiles)
    {
        Console.WriteLine($"Merging {inputFiles.Length} test report files into {outputFile}...");

        try
        {
            var combinedRoot = new XElement("testsuites");

            foreach (var file in inputFiles)
            {
                if (!File.Exists(file)) continue;
                try
                {
                    var doc = XDocument.Load(file);
                    if (doc.Root != null)
                    {
                        combinedRoot.Add(doc.Root);
                    }
                }
                catch
                {
                    // Ignore malformed report
                }
            }

            var mergedDoc = new XDocument(combinedRoot);
            mergedDoc.Save(outputFile);
            Console.WriteLine($"✔ Merged report saved to {outputFile}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to merge reports: {ex.Message}");
            return 1;
        }
    }
}
