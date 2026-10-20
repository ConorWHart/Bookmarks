using System.Text;

namespace Bookmarks.Core.Transfer;

/// <summary>RFC 4180 CSV: comma-separated, fields optionally in double quotes, "" for a quote, line breaks inside quotes.</summary>
public static class Csv
{
    /// <summary>
    /// Every record as its fields. Accepts \r\n, \n or \r line endings and a leading byte-order mark; skips blank lines.
    /// A quote inside an unquoted field is kept as text (Goodreads' <c>="0441172717"</c> ISBNs).
    /// </summary>
    public static IReadOnlyList<string[]> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;     // inside a quoted field
        var wasQuoted = false;  // the current field started with a quote
        var i = text.Length > 0 && text[0] == '﻿' ? 1 : 0;

        void EndField()
        {
            fields.Add(field.ToString());
            field.Clear();
            wasQuoted = false;
        }

        void EndRecord()
        {
            EndField();
            if (!(fields.Count == 1 && fields[0].Length == 0))
            {
                records.Add(fields.ToArray());
            }

            fields.Clear();
        }

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
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
                        quoted = false;
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
                case '"' when field.Length == 0 && !wasQuoted:
                    quoted = true;
                    wasQuoted = true;
                    break;
                case ',':
                    EndField();
                    break;
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    EndRecord();
                    break;
                case '\n':
                    EndRecord();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0 || wasQuoted)
        {
            EndRecord();
        }

        return records;
    }

    /// <summary>One CSV line (no line break) from the fields, quoting where needed.</summary>
    public static string FormatRecord(IEnumerable<string?> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return string.Join(",", fields.Select(Quote));
    }

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.IndexOfAny(['"', ',', '\r', '\n']) >= 0 || value[0] == ' ' || value[^1] == ' '
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
