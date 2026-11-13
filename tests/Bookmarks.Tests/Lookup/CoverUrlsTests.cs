using Bookmarks.Core.Lookup;

namespace Bookmarks.Tests.Lookup;

public class CoverUrlsTests
{
    // What Chrome on Android gives for press and hold on a Google image result → Share image.
    private const string GoogleShareLink =
        "https://www.google.com/search?q=the+favorite+kiera+cass&oq=&sourceid=chrome-mobile&ie=UTF-8"
        + "#sv=CAMScRoyKhBlLXBzSi16cWFRQ3RSMVNNMg5wc0otenFhUUN0UjFTTToOWU1yUV80UGNnRE95Mk0gBCoxChtfMUluMmFvXzJCZnpXcnVFUDQtZW55UThfMzQSEGUtcHNKLXpxYVFDdFIxU00YADABSgQIARACGAcg17OdzAJKCBACGAEgAigB";

    [Theory]
    [InlineData(GoogleShareLink)]
    [InlineData("https://www.google.com/search?q=dune&tbm=isch")]
    [InlineData("https://images.google.co.uk/search?q=dune")]
    [InlineData("https://share.google/abc123")]
    [InlineData("https://www.bing.com/images/search?q=dune")]
    public void IsSearchPage_spots_links_to_results_pages(string url)
    {
        Assert.True(CoverUrls.IsSearchPage(url));
        Assert.True(CoverUrls.IsSearchPage(CoverUrls.Normalize(url))); // nothing to take out of it
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://covers.openlibrary.org/b/id/12345-M.jpg")]
    [InlineData("https://m.media-amazon.com/images/I/81abc.jpg")]
    [InlineData("https://www.google.com/books/edition/_/abc")]
    [InlineData("https://encrypted-tbn0.gstatic.com/images?q=tbn:abc")]
    [InlineData("data:image/webp;base64,AAAA")]
    public void IsSearchPage_leaves_other_links_alone(string? url) => Assert.False(CoverUrls.IsSearchPage(url));

    [Theory]
    [InlineData("https://www.google.com/imgres?imgurl=https%3A%2F%2Fexample.com%2Fcover.jpg&imgrefurl=https%3A%2F%2Fexample.com%2F", "https://example.com/cover.jpg")]
    [InlineData("https://www.google.co.uk/url?sa=i&url=https%3A%2F%2Fexample.com%2Fa%20b.png&psig=x", "https://example.com/a b.png")]
    [InlineData("https://www.bing.com/images/search?view=detailV2&mediaurl=https%3a%2f%2fexample.com%2fc.webp&q=x", "https://example.com/c.webp")]
    public void Unwrap_takes_the_image_out_of_search_links(string url, string expected)
        => Assert.Equal(expected, CoverUrls.Unwrap(url));

    [Theory]
    [InlineData("https://example.com/cover.jpg?url=https%3A%2F%2Fother.com%2Fx.jpg")] // not a search engine
    [InlineData("https://www.google.com/imgres?imgurl=javascript%3Aalert(1)")] // not a web link
    [InlineData("https://www.google.com/imgres?tbnid=abc")] // no image in it
    public void Unwrap_leaves_other_links_alone(string url) => Assert.Equal(url, CoverUrls.Unwrap(url));

    [Fact]
    public void Normalize_unwraps_then_upgrades_to_https()
        => Assert.Equal("https://example.com/cover.jpg",
            CoverUrls.Normalize("  https://www.google.com/imgres?imgurl=http%3A%2F%2Fexample.com%2Fcover.jpg  "));

    [Theory]
    [InlineData("data:image/webp;base64,AAAA", true)]
    [InlineData("data:image/jpeg;base64,AAAA", true)]
    [InlineData("data:text/html;base64,AAAA", false)]
    [InlineData("https://example.com/cover.jpg", false)]
    [InlineData(null, false)]
    public void IsPasted_means_an_image_kept_with_the_book(string? url, bool expected)
        => Assert.Equal(expected, CoverUrls.IsPasted(url));

    [Fact]
    public void A_pasted_cover_is_shown_as_it_is() // CoverImage asks for a size of every cover
    {
        const string pasted = "data:image/webp;base64,UklGRg==";
        Assert.Equal(pasted, CoverUrls.WithSize(pasted, CoverSize.Small));
        Assert.False(CoverUrls.IsOpenLibrary(pasted));
    }
}
