using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

using Cadence.Apps.Execution.Application.Common;

using FluentAssertions;

using Xunit;

namespace Cadence.UnitTests.Execution.Common;

public sealed class TabularParsingHelpersTests
{
    [Theory]
    [InlineData("a,b,c", new[] { "a", "b", "c" })]
    [InlineData("a, b , c ", new[] { "a", "b", "c" })]
    [InlineData("\"hello world\",123,456", new[] { "hello world", "123", "456" })]
    [InlineData("\"hello, world\",test,foo", new[] { "hello, world", "test", "foo" })]
    [InlineData("\"He said \"\"Hello\"\" to me\",yes", new[] { "He said \"Hello\" to me", "yes" })]
    [InlineData("\"a\"\"b\"\"c\",x", new[] { "a\"b\"c", "x" })]
    [InlineData("a,,c", new[] { "a", "", "c" })]
    [InlineData(",b,c", new[] { "", "b", "c" })]
    [InlineData("a,b,", new[] { "a", "b", "" })]
    [InlineData("", new[] { "" })]
    [InlineData("   ", new[] { "" })]
    public void SplitCsvLine_ParsesCorrectly(string line, string[] expected)
    {
        var result = TabularParsingHelpers.SplitCsvLine(line);
        result.Should().Equal(expected);
    }

    [Fact]
    public void SplitCsvLine_NullInput_ReturnsEmptyList()
    {
        var result = TabularParsingHelpers.SplitCsvLine(null!);
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("A", 0)]
    [InlineData("B", 1)]
    [InlineData("Z", 25)]
    [InlineData("AA", 26)]
    [InlineData("AB", 27)]
    [InlineData("AZ", 51)]
    [InlineData("BA", 52)]
    [InlineData("ZZ", 701)]
    [InlineData("AAA", 702)]
    [InlineData("A1", 0)]
    [InlineData("b2", 1)]
    [InlineData("C10", 2)]
    [InlineData("aa100", 26)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("123", 0)]
    public void ColumnNameToIndex_CalculatesExpectedIndex(string? cellRef, int expectedIndex)
    {
        int index = TabularParsingHelpers.ColumnNameToIndex(cellRef!);
        index.Should().Be(expectedIndex);
    }

    [Fact]
    public void GetValue_ValidIndex_ReturnsTrimmedString()
    {
        var list = new List<string> { " first ", "second\t", "third" };
        TabularParsingHelpers.GetValue(list, 0).Should().Be("first");
        TabularParsingHelpers.GetValue(list, 1).Should().Be("second");
        TabularParsingHelpers.GetValue(list, 2).Should().Be("third");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(100)]
    public void GetValue_OutOfBoundsIndex_ReturnsEmptyString(int index)
    {
        var list = new List<string> { "first", "second" };
        TabularParsingHelpers.GetValue(list, index).Should().BeEmpty();
    }

    [Fact]
    public void GetValue_NullListOrNullElement_ReturnsEmptyString()
    {
        List<string>? nullList = null;
        TabularParsingHelpers.GetValue(nullList, 0).Should().BeEmpty();

        var listWithNull = new List<string?> { "a", null, "c" };
        TabularParsingHelpers.GetValue(listWithNull!, 1).Should().BeEmpty();
    }

    [Fact]
    public void ExtractRowValues_SparseColumns_PadsMissingColumnsWithEmptyStrings()
    {
        XNamespace ns = TabularParsingHelpers.SpreadsheetNamespace;
        var sharedStrings = new List<string> { "HeaderA", "HeaderC" };

        var rowXml = new XElement(ns + "row",
            new XAttribute("r", "1"),
            new XElement(ns + "c", new XAttribute("r", "A1"), new XAttribute("t", "s"), new XElement(ns + "v", "0")),
            // Column B is missing
            new XElement(ns + "c", new XAttribute("r", "C1"), new XAttribute("t", "s"), new XElement(ns + "v", "1"))
        );

        var values = TabularParsingHelpers.ExtractRowValues(rowXml, sharedStrings);

        values.Should().HaveCount(3);
        values[0].Should().Be("HeaderA");
        values[1].Should().Be(string.Empty);
        values[2].Should().Be("HeaderC");
    }

    [Fact]
    public void ExtractRowValues_InlineStringAndDirectValues_ExtractedProperly()
    {
        XNamespace ns = TabularParsingHelpers.SpreadsheetNamespace;
        var sharedStrings = new List<string>();

        var rowXml = new XElement(ns + "row",
            new XAttribute("r", "1"),
            new XElement(ns + "c",
                new XAttribute("r", "A1"),
                new XAttribute("t", "inlineStr"),
                new XElement(ns + "is", new XElement(ns + "t", "Inline Value"))
            ),
            new XElement(ns + "c",
                new XAttribute("r", "B1"),
                new XElement(ns + "v", "42.5")
            )
        );

        var values = TabularParsingHelpers.ExtractRowValues(rowXml, sharedStrings);

        values.Should().HaveCount(2);
        values[0].Should().Be("Inline Value");
        values[1].Should().Be("42.5");
    }

    [Fact]
    public void ExtractRowValues_NullRow_ReturnsEmptyList()
    {
        var values = TabularParsingHelpers.ExtractRowValues(null!, new List<string>());
        values.Should().BeEmpty();
    }

    [Fact]
    public void LoadSharedStringsAndWorksheetRows_ValidZip_ReturnsExpectedData()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var stringsEntry = archive.CreateEntry("xl/sharedStrings.xml");
            using (var writer = new StreamWriter(stringsEntry.Open(), Encoding.UTF8))
            {
                writer.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="2">
                        <si><t>Alpha</t></si>
                        <si><t>Beta</t></si>
                    </sst>
                    """);
            }

            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using (var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8))
            {
                writer.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                        <sheetData>
                            <row r="1">
                                <c r="A1" t="s"><v>0</v></c>
                                <c r="B1" t="s"><v>1</v></c>
                            </row>
                            <row r="2">
                                <c r="A2"><v>100</v></c>
                                <c r="B2"><v>200</v></c>
                            </row>
                        </sheetData>
                    </worksheet>
                    """);
            }
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var sharedStrings = TabularParsingHelpers.LoadSharedStrings(readArchive);
        sharedStrings.Should().Equal("Alpha", "Beta");

        var rows = TabularParsingHelpers.LoadWorksheetRows(readArchive);
        rows.Should().HaveCount(2);

        var row1Values = TabularParsingHelpers.ExtractRowValues(rows[0], sharedStrings);
        row1Values.Should().Equal("Alpha", "Beta");

        var row2Values = TabularParsingHelpers.ExtractRowValues(rows[1], sharedStrings);
        row2Values.Should().Equal("100", "200");
    }

    [Fact]
    public void LoadSharedStrings_MissingFile_ReturnsEmptyList()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("xl/workbook.xml");
        }
        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var sharedStrings = TabularParsingHelpers.LoadSharedStrings(readArchive);
        sharedStrings.Should().BeEmpty();
    }

    [Fact]
    public void LoadWorksheetRows_MissingWorksheet_ThrowsInvalidOperationException()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("xl/sharedStrings.xml");
        }
        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var act = () => TabularParsingHelpers.LoadWorksheetRows(readArchive);
        act.Should().Throw<InvalidOperationException>().WithMessage("*missing worksheet data*");
    }

    [Fact]
    public void LoadWorksheetRows_EmptySheetData_ReturnsEmptyList()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8);
            writer.Write("""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                    <sheetData/>
                </worksheet>
                """);
        }
        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var rows = TabularParsingHelpers.LoadWorksheetRows(readArchive);
        rows.Should().BeEmpty();
    }
}
