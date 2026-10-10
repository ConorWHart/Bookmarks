using Bookmarks.Core.Lookup;

namespace Bookmarks.Tests.Lookup;

public class IsbnTests
{
    [Theory]
    [InlineData("9780547928227", "9780547928227")]
    [InlineData("978-0-547-92822-7", "9780547928227")]
    [InlineData(" 978 0547 928227 ", "9780547928227")]
    [InlineData("ISBN-13: 978-0-547-92822-7", "9780547928227")]
    [InlineData("isbn 054792822X", "9780547928227")]
    [InlineData("0-547-92822-x", "9780547928227")]
    [InlineData("0765326353", "9780765326355")]
    [InlineData("9791032305690", "9791032305690")]
    public void TryNormalize_accepts_valid_forms(string input, string expected)
    {
        Assert.True(Isbn.TryNormalize(input, out var isbn13));
        Assert.Equal(expected, isbn13);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9780547928228")] // bad check digit
    [InlineData("0547928221")] // bad check digit
    [InlineData("9770547928227")] // not a book EAN (977 = serials)
    [InlineData("97805479282271")] // too long
    [InlineData("978054792822")] // too short
    [InlineData("97805479X8227")] // X only allowed last in ISBN-10
    [InlineData("the hobbit")]
    public void TryNormalize_rejects_invalid_input(string? input)
        => Assert.False(Isbn.TryNormalize(input, out _));

    [Theory]
    [InlineData("9780547928227", "054792822X")]
    [InlineData("9780765326355", "0765326353")]
    public void To10_round_trips(string isbn13, string isbn10)
    {
        Assert.Equal(isbn10, Isbn.To10(isbn13));
        Assert.Equal(isbn13, Isbn.From10(isbn10));
    }

    [Fact]
    public void To10_is_null_for_979_numbers()
        => Assert.Null(Isbn.To10("9791032305690"));
}
