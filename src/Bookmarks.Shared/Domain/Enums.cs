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

public enum SpineStyle
{
    Classic = 0,
    Banded = 1,
    Minimal = 2,
    Ornate = 3,
}
