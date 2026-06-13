using System.Text;

namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 / D2 — a minimal in-house RFC-4180 subset reader (FR-014 forbids new
/// NuGet packages). Handles: comma delimiter; double-quote-wrapped fields with
/// <c>""</c> escaping; embedded commas/newlines inside quoted fields; CRLF and LF
/// line endings; a leading UTF-8 BOM; and ignores fully-blank lines (including a
/// trailing newline). Returns the header row plus the data rows.
/// </summary>
public static class CsvParser
{
    public sealed record CsvContent(
        IReadOnlyList<string> Header,
        IReadOnlyList<IReadOnlyList<string>> Rows);

    public static CsvContent Parse(string? text)
    {
        var records = ParseRecords(text ?? string.Empty);

        // Drop fully-blank records (e.g. trailing newline, stray empty lines).
        var nonBlank = records.Where(r => !IsBlankRecord(r)).ToList();
        if (nonBlank.Count == 0)
        {
            return new CsvContent(Array.Empty<string>(), Array.Empty<IReadOnlyList<string>>());
        }

        var header = nonBlank[0];
        var rows = nonBlank
            .Skip(1)
            .Select(r => (IReadOnlyList<string>)r)
            .ToList();
        return new CsvContent(header, rows);
    }

    private static List<List<string>> ParseRecords(string text)
    {
        // Strip a single leading UTF-8 BOM.
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var sawAnyToken = false; // any field delimiter or content on the current record

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    sawAnyToken = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    sawAnyToken = true;
                    break;
                case '\r':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    sawAnyToken = false;
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    sawAnyToken = false;
                    break;
                default:
                    field.Append(c);
                    sawAnyToken = true;
                    break;
            }
        }

        // Flush the final record if the text did not end on a newline.
        if (sawAnyToken || field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    private static bool IsBlankRecord(IReadOnlyList<string> record) =>
        record.All(string.IsNullOrWhiteSpace);
}
