namespace Bookmarks.Shared.Domain;

public enum OwnershipStatus
{
    Owned = 0,
    Wishlist = 1,
    LentOut = 2,
}

public enum BookFormat
{
    Hardcover = 0,
    Paperback = 1,
    MassMarketPaperback = 2,
    LeatherBound = 3,
    Other = 99,
}

public enum BookCondition
{
    New = 0,
    LikeNew = 1,
    Good = 2,
    Fair = 3,
    Poor = 4,
}

public enum ReadingOutcome
{
    InProgress = 0,
    Finished = 1,
    Abandoned = 2,
}

public enum BookSource
{
    Manual = 0,
    OpenLibrary = 1,
    GoogleBooks = 2,
}

/// <summary>How thick the paper is, which with the page count sets how thick the spine looks (Phase 6e).</summary>
public enum PaperThickness
{
    Standard = 0,

    /// <summary>Heavier paper: illustrated books, some hardcovers.</summary>
    Thick = 1,

    /// <summary>Art paper, photo books, board books.</summary>
    VeryThick = 2,

    /// <summary>Lightweight paper: some mass-market paperbacks, omnibus editions.</summary>
    Thin = 3,

    /// <summary>Bible / India paper.</summary>
    VeryThin = 4,
}

public enum SpineStyle
{
    Classic = 0,
    Banded = 1,
    Minimal = 2,
    Ornate = 3,

    // Designs made from a cover's colours (Phase 7e); each uses SpineAppearance.AccentColor.

    /// <summary>Accent blocks at head and tail, the title on the band between (like the old Penguin paperbacks).</summary>
    Penguin = 4,

    /// <summary>Stepped Art Deco bands at head and tail; gilt lettering on dark spines.</summary>
    Deco = 5,

    /// <summary>A bold band and a fine rule across the lower spine.</summary>
    Stripe = 6,

    /// <summary>The accent colour fading into the spine colour from the top.</summary>
    TwoTone = 7,
}
