# Sign-in page branding: "open book on a desk" (alternative design)

The sign-in card is an open book lying on a desk, seen from above. The left page is decoration (faux text,
a chapter heading, a ribbon). The form is written on the right page. Around the book are other books
(open and closed), paper and leather bookmarks, a reading lamp, a wooden page holder, reading glasses,
a pencil and a coffee.

On narrow screens (under 940 px) only the right-hand page fits. The book still reads as open: the gutter
curves into the spine and the left page runs off the edge of the screen. A striped bookmark (blue, gold and
red, with a tassel) sticks out from under the right edge of the book. It's drawn on `.ext-middle`, the layer
behind the card.

The first design (bookshelf wall, dark card) is in `../signin/`. Upload one set or the other, not both.

## Files
| File | Where it goes |
|---|---|
| `out/background.jpg` (1920×1080, 150 KB) | Basics → Background image (page background color `#3E2716`) |
| `out/banner-logo-ink.png` (245×36, dark text for the paper page) | Sign-in form → Banner logo |
| `../signin/out/square-logo.png`, `../signin/out/favicon.png` | Same as the first design |
| `out/bookmarks-signin-book.css` | Layout → Custom CSS |
| `desk.svg` | Source of the background. Render it at 1920×1080 in a browser, then save as JPEG, quality 85 |

The upload steps are the same as in `../signin/README.md`, with these differences:
- Background color: `#3E2716`.
- Banner logo: `banner-logo-ink.png`.
- CSS file: `bookmarks-signin-book.css`.

## Things to check on the real page
- **The book's width.** The CSS widens Microsoft's card to 880 px and pads the form onto the right half.
  - If Microsoft's own styles cap the card's width, the form will sit off-centre over the gutter. In that case, raise the `max-width` or drop the book to one page, as phones already get.
  - If the page shows a scrollbar or the book is clipped on a laptop, reduce `width` to 820 px and the left padding to 466 px. That's half the width plus 56 px. The left-page decorations are positioned in pixels from the left, so they stay put.
- **Text contrast.** All text is dark ink on cream paper. Anything Microsoft doesn't expose as an `.ext-*` class keeps its default dark colour, which also reads fine on paper. So this design is safer than the dark card.
