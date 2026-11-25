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
    [InlineData("XFD", 16_383)]
    [InlineData("A1", 0)]
    [InlineData("b2", 1)]
    [InlineData("C10", 2)]
    [InlineData("aa100", 26)]
    [InlineData("XFD1048576", 16_383)]
    public void ColumnNameToIndex_CalculatesExpectedIndex(string cellRef, int expectedIndex)
    {
        int index = TabularParsingHelpers.ColumnNameToIndex(cellRef);
        index.Should().Be(expectedIndex);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("123")]
    [InlineData("A0")]
    [InlineData("$A$1")]
    [InlineData("1A")]
    [InlineData("A1B2")]
    [InlineData("XFE")]
    [InlineData("XFE1")]
    [InlineData("AAAA1")]
    [InlineData("BZZZZ1")]
    [InlineData("ZZZZZZZZZZ1")]
    public void ColumnNameToIndex_InvalidCoordinatesOrExceedingCeiling_ThrowsInvalidOperationException(string? cellRef)
    {
        var act = () => TabularParsingHelpers.ColumnNameToIndex(cellRef!);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ColumnNameToIndex_WithCustomMaxColumnIndex_RejectsAboveConfiguredCap()
    {
        TabularParsingHelpers.ColumnNameToIndex("BL1", maxColumnIndex: 63).Should().Be(63);

        var act = () => TabularParsingHelpers.ColumnNameToIndex("BM1", maxColumnIndex: 63);
        act.Should().Throw<InvalidOperationException>().WithMessage("*63*");
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

    [Fact]
    public void ExtractRowValues_ExtremeOrMissingCellCoordinates_ThrowsInvalidOperationException()
    {
        XNamespace ns = TabularParsingHelpers.SpreadsheetNamespace;

        var extremeRow = new XElement(ns + "row",
            new XAttribute("r", "1"),
            new XElement(ns + "c",
                new XAttribute("r", "BZZZZ1"),
                new XAttribute("t", "inlineStr"),
                new XElement(ns + "is", new XElement(ns + "t", "MARKER_COL"))));

        var missingCoordinateRow = new XElement(ns + "row",
            new XAttribute("r", "1"),
            new XElement(ns + "c", new XElement(ns + "v", "1")));

        var duplicateCoordinateRow = new XElement(ns + "row",
            new XAttribute("r", "1"),
            new XElement(ns + "c", new XAttribute("r", "B1"), new XElement(ns + "v", "1")),
            new XElement(ns + "c", new XAttribute("r", "A1"), new XElement(ns + "v", "2")));

        var cappedRow = new XElement(ns + "row",
            new XAttribute("r", "1"),
            new XElement(ns + "c", new XAttribute("r", "E1"), new XElement(ns + "v", "1")));

        FluentActions.Invoking(() => TabularParsingHelpers.ExtractRowValues(extremeRow, []))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => TabularParsingHelpers.ExtractRowValues(missingCoordinateRow, []))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => TabularParsingHelpers.ExtractRowValues(duplicateCoordinateRow, []))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => TabularParsingHelpers.ExtractRowValues(cappedRow, [], maxColumnIndex: 3))
            .Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void LoadWorksheetRows_ExceedingCompressionRatio_ThrowsInvalidOperationException()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize);
            using var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8);
            writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            writer.Write(new string(' ', 250_000));
            writer.Write("</sheetData></worksheet>");
        }

        stream.Position = 0;
        using var readArchive = TabularParsingHelpers.OpenArchive(stream);

        FluentActions.Invoking(() => TabularParsingHelpers.LoadWorksheetRows(readArchive))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*100:1 decompression ratio*");
    }

    [Fact]
    public void LoadWorksheetRows_ExceedingEntryCountOrUncompressedBytesOrRowCap_ThrowsInvalidOperationException()
    {
        using var manyEntriesStream = new MemoryStream();
        using (var archive = new ZipArchive(manyEntriesStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < 6; i++)
            {
                archive.CreateEntry($"xl/extra_{i}.xml");
            }
        }

        manyEntriesStream.Position = 0;
        var smallEntryQuota = TabularArchiveQuotas.Default with { MaxEntryCount = 5 };
        FluentActions.Invoking(() => TabularParsingHelpers.OpenArchive(manyEntriesStream, smallEntryQuota))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*entry count*");

        using var rowOverflowStream = new MemoryStream();
        using (var archive = new ZipArchive(rowOverflowStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8);
            writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (int r = 1; r <= 5; r++)
            {
                writer.Write($"<row r=\"{r}\"><c r=\"A{r}\"><v>{r}</v></c></row>");
            }

            writer.Write("</sheetData></worksheet>");
        }

        rowOverflowStream.Position = 0;
        using var rowArchive = TabularParsingHelpers.OpenArchive(rowOverflowStream);
        FluentActions.Invoking(() => TabularParsingHelpers.LoadWorksheetRows(rowArchive, maxRows: 3))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*maximum limit of 3 row elements*");

        rowOverflowStream.Position = 0;
        using var byteArchive = TabularParsingHelpers.OpenArchive(rowOverflowStream);
        var tinyByteQuota = TabularArchiveQuotas.Default with { MaxEntryUncompressedBytes = 64 };
        FluentActions.Invoking(() => TabularParsingHelpers.LoadWorksheetRows(byteArchive, null, tinyByteQuota))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*per-entry limit*");
    }

    [Fact]
    public void LoadWorksheetRows_WithDtdOrExceededCharacterCeilingOrCorruptZip_ThrowsInvalidOperationException()
    {
        using var dtdStream = new MemoryStream();
        using (var archive = new ZipArchive(dtdStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8);
            writer.Write("<?xml version=\"1.0\"?><!DOCTYPE worksheet [<!ENTITY x \"boom\">]><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData/></worksheet>");
        }

        dtdStream.Position = 0;
        using var dtdArchive = TabularParsingHelpers.OpenArchive(dtdStream);
        FluentActions.Invoking(() => TabularParsingHelpers.LoadWorksheetRows(dtdArchive))
            .Should().Throw<InvalidOperationException>();

        using var charCeilingStream = new MemoryStream();
        using (var archive = new ZipArchive(charCeilingStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var sheetEntry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.NoCompression);
            using var writer = new StreamWriter(sheetEntry.Open(), Encoding.UTF8);
            writer.Write("<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>ABCDEFGHIJKLMNOPQRSTUVWXYZ</t></is></c></row></sheetData></worksheet>");
        }

        charCeilingStream.Position = 0;
        using var charArchive = TabularParsingHelpers.OpenArchive(charCeilingStream);
        var charQuota = TabularArchiveQuotas.Default with { MaxCharactersInDocument = 64 };
        FluentActions.Invoking(() => TabularParsingHelpers.LoadWorksheetRows(charArchive, null, charQuota))
            .Should().Throw<InvalidOperationException>();

        using var corruptZipStream = new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0xFF, 0x00, 0x11, 0x22]);
        FluentActions.Invoking(() => TabularParsingHelpers.OpenArchive(corruptZipStream))
            .Should().Throw<InvalidOperationException>();
    }
}
