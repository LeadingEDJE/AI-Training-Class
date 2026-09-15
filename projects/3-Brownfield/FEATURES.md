# Feature ideas for the brownfield lab

Pick one feature and use it twice: once in Part 1 with no context, once in Step 5 with everything you built in Part 2. Same feature, so the two attempts are comparable.

A good feature crosses at least two layers, has to fit an existing convention in the repo, and has a result you can see in the running app. Changing a string or a config value is too small. A rewrite is too big for the 20 to 30 minutes of execution time.

Sizes: **S** touches one layer plus a test. **M** touches two or three layers. **L** touches everything. Pick S if you're new to the stack, M for most people, L if you want a fight.

## eShopOnWeb

### Star ratings and reviews (L)

Let a signed-in customer rate a catalog item 1-5 stars and leave a short review. Show the average rating on the catalog card and the reviews somewhere the customer can read them for that item.

**Done looks like:** open `https://localhost:5001`, see a star average on a card and reviews rendered wherever the attendee put the detail view.

### Wishlist (M)

Let a signed-in customer save catalog items to a wishlist, view it, and move an item from it into the basket.

**Done looks like:** add an item to the wishlist from the catalog, open the wishlist page, click move-to-basket, and see it in `/Basket/Index`.

### Order history with admin status change (M)

Let a signed-in customer see past orders with a status (Pending, Shipped, Delivered). Let an admin change an order's status from the admin area.

**Done looks like:** log in as `demouser@microsoft.com`, see past orders with a status on a new page; in `/admin`, change a status and see it reflected back.

### Stock tracking with out-of-stock badge (M)

Track stock per catalog item. Show an "Out of Stock" badge and disable Add-to-Basket at zero, and reduce the count when an order is placed.

**Done looks like:** set an item's stock to 1 in the admin edit page, order it once, reload the catalog, and see that item show "Out of Stock" with the button disabled.

### Recently viewed items (M)

Track catalog items a signed-in customer has viewed and show a "Recently viewed" row of up to five on the catalog page.

**Done looks like:** log in, view a couple of items, return to the catalog, and see a "Recently viewed" row with exactly those items in order.

### Catalog search by name (S)

Add a search box to the storefront catalog page that filters the grid to items whose name matches what was typed.

**Done looks like:** open `https://localhost:5001`, type part of a known product name, submit, and see the grid narrow to matches.

### Public API price range filter (S)

Let a public API client request catalog items within a minimum and maximum price, in addition to the existing brand/type filters.

**Done looks like:** run PublicApi, call `GET /api/catalog-items?minPrice=X&maxPrice=Y` (Swagger UI is available), and see only items in that band.

## hexo

### `hexo stats` console command (L)

Give a blog author a `hexo stats` command that reports on their site: how many posts, total words written, how many posts per tag and per category, and the five longest posts. It should be formatted like the existing `hexo list` output.

**Done looks like:** from `example-site/`, run `npx hexo stats` and see a formatted report matching the posts under `example-site/source/_posts`.

### `related_posts` helper (M)

Under every post on the example site, show a "Related posts" list of up to three other posts that share the most tags with it. Theme authors should be able to call it from any template.

**Done looks like:** `npx hexo generate && npx hexo server -p 4111` from `example-site/`, open a post that shares tags with another seeded post, and see a related-posts list under it.

### `reading_time` helper (M)

Show an estimated reading time, like "4 min read", next to every post's date on the example site. The words-per-minute used for the estimate must be configurable per site.

**Done looks like:** set a custom words-per-minute value in `example-site/_config.yml`, regenerate, and see the estimate next to a post change accordingly.

### Site feed generator (M)

Give the example site a working RSS or Atom feed of its most recent posts. The theme already advertises a feed link in the page header; make that link point at a real feed that updates when posts change.

**Done looks like:** enable `feed:` in `example-site/_config.yml`, regenerate, and open `http://localhost:4111/atom.xml` (or the chosen path) directly to see valid feed XML.

### Site tag summary JSON endpoint (M)

Publish a machine-readable summary of the site's tags at a fixed URL, so an external script can fetch every tag, its slug, and how many posts use it, without scraping HTML.

**Done looks like:** `npx hexo generate && npx hexo server -p 4111`, open `http://localhost:4111/tags.json`, and see valid JSON with real per-tag counts.

### Custom embed tag plugin (S)

Let a writer embed a YouTube video in a post by typing `{% youtube <id> %}` in their Markdown. Demonstrate it in one example post that renders a working video.

**Done looks like:** add `{% youtube dQw4w9WgXcQ %}` to a post under `example-site/source/_posts/`, regenerate and serve, and see a working embedded video on that post's page.

### `hexo list post --tag` filter (S)

Let an author run `hexo list post --tag <name>` to see only the posts carrying that tag, in the same table format the command uses today.

**Done looks like:** from `example-site/`, run `npx hexo list post --tag <real-tag>` and see only matching posts, versus the full list without the flag.
