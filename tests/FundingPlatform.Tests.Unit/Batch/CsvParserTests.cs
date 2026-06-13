using FundingPlatform.Application.Admin.Users.Batch;

namespace FundingPlatform.Tests.Unit.Batch;

public class CsvParserTests
{
    [Test]
    public void Parse_SplitsHeaderAndDataRows()
    {
        var content = CsvParser.Parse("a,b,c\n1,2,3\n4,5,6");

        Assert.That(content.Header, Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(content.Rows.Count, Is.EqualTo(2));
        Assert.That(content.Rows[0], Is.EqualTo(new[] { "1", "2", "3" }));
        Assert.That(content.Rows[1], Is.EqualTo(new[] { "4", "5", "6" }));
    }

    [Test]
    public void Parse_StripsLeadingBom_OnFirstHeaderCell()
    {
        var content = CsvParser.Parse("﻿Grupo,Proceso\nNorte,Migración inicial");

        Assert.That(content.Header[0], Is.EqualTo("Grupo"));
        Assert.That(content.Header[1], Is.EqualTo("Proceso"));
    }

    [Test]
    public void Parse_QuotedField_WithEmbeddedComma_IsOneField()
    {
        var content = CsvParser.Parse("name,note\n\"Rojas, Ana\",hi");

        Assert.That(content.Rows[0], Is.EqualTo(new[] { "Rojas, Ana", "hi" }));
    }

    [Test]
    public void Parse_QuotedField_WithEmbeddedNewline_IsOneField()
    {
        var content = CsvParser.Parse("a,b\n\"line1\nline2\",x");

        Assert.That(content.Rows.Count, Is.EqualTo(1));
        Assert.That(content.Rows[0][0], Is.EqualTo("line1\nline2"));
        Assert.That(content.Rows[0][1], Is.EqualTo("x"));
    }

    [Test]
    public void Parse_QuoteEscape_DoubledQuotesBecomeSingle()
    {
        var content = CsvParser.Parse("a\n\"She said \"\"hi\"\"\"");

        Assert.That(content.Rows[0][0], Is.EqualTo("She said \"hi\""));
    }

    [Test]
    public void Parse_CrlfAndLf_AreEquivalent()
    {
        var crlf = CsvParser.Parse("a,b\r\n1,2\r\n3,4");
        var lf = CsvParser.Parse("a,b\n1,2\n3,4");

        Assert.That(crlf.Rows, Is.EqualTo(lf.Rows));
        Assert.That(crlf.Header, Is.EqualTo(lf.Header));
    }

    [Test]
    public void Parse_TrailingBlankLine_IsIgnored()
    {
        var content = CsvParser.Parse("a,b\n1,2\n");

        Assert.That(content.Rows.Count, Is.EqualTo(1));
        Assert.That(content.Rows[0], Is.EqualTo(new[] { "1", "2" }));
    }

    [Test]
    public void Parse_InteriorBlankLine_IsDropped()
    {
        var content = CsvParser.Parse("a,b\n1,2\n\n3,4\n");

        Assert.That(content.Rows.Count, Is.EqualTo(2));
        Assert.That(content.Rows[1], Is.EqualTo(new[] { "3", "4" }));
    }

    [Test]
    public void Parse_EmptyText_ReturnsEmptyHeaderAndRows()
    {
        var content = CsvParser.Parse("");

        Assert.That(content.Header, Is.Empty);
        Assert.That(content.Rows, Is.Empty);
    }
}
