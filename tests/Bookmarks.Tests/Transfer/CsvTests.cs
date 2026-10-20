using Bookmarks.Core.Transfer;

namespace Bookmarks.Tests.Transfer;

public class CsvTests
{
    [Fact]
    public void Parses_quoted_fields_escaped_quotes_and_line_breaks()
    {
        var text = "﻿a,b,c\r\n\"x, y\",\"say \"\"hi\"\"\",\"two\nlines\"\n1,,3\n\n";

        var records = Csv.Parse(text);

        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { "a", "b", "c" }, records[0]);
        Assert.Equal(new[] { "x, y", "say \"hi\"", "two\nlines" }, records[1]);
        Assert.Equal(new[] { "1", "", "3" }, records[2]);
    }

    [Fact]
    public void Keeps_goodreads_isbn_wrappers_as_text()
    {
        var records = Csv.Parse("ISBN,ISBN13\n=\"0441172717\",\"=\"\"9780441172719\"\"\"");

        Assert.Equal(new[] { "=\"0441172717\"", "=\"9780441172719\"" }, records[1]);
    }

    [Fact]
    public void Last_record_without_a_line_break_is_read()
    {
        var records = Csv.Parse("a,b\n1,\"2\"");

        Assert.Equal(new[] { "1", "2" }, records[1]);
    }

    [Fact]
    public void Format_quotes_only_when_needed_and_round_trips()
    {
        string?[] fields = ["plain", "with, comma", "say \"hi\"", "line\nbreak", null, "=\"978\""];

        var line = Csv.FormatRecord(fields);

        Assert.Equal("plain,\"with, comma\",\"say \"\"hi\"\"\",\"line\nbreak\",,\"=\"\"978\"\"\"", line);
        Assert.Equal(new[] { "plain", "with, comma", "say \"hi\"", "line\nbreak", "", "=\"978\"" }, Csv.Parse(line)[0]);
    }
}
