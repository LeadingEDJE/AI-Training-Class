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

## LEAP (Compass)

### Bulk upload of new EDJErs and clients (L)

Let an administrator load a spreadsheet of new employees (EDJErs) or new clients instead of typing them in one at a time. Import every valid row, and hand back the rows that failed with the row number and the reason, so they can fix just those and re-upload.

**Done looks like:** open `http://localhost:5176/compass/admin/edjers`, upload a file with several good rows, one duplicate email and one bad state code; the good rows appear in the list, the two bad ones come back named by row number, and `curl -s "http://localhost:5009/api/audit-logs/browse?entityType=CompassEmployee"` shows one entry per imported EDJEr.

### Skills on EDJErs, filterable in the directory (L)

Track the technical skills each EDJEr has. Give an administrator a screen to manage the skill list and assign skills to people, and let anyone filter the Team Directory to EDJErs who have a given skill.

**Done looks like:** `make dev-all`, add a skill on the new admin screen, assign it to two EDJErs, then filter `http://localhost:5176/compass/team-directory` to that skill and see exactly those two.

### Renew a SOW in one step (M)

From an assignment's SOW list, let an Ops user renew an expiring statement of work — pick the new end date and have the follow-on SOW created for them, instead of retyping the whole form.

**Done looks like:** open an EDJEr from `http://localhost:5176/compass/team-directory`, open an assignment with a SOW, renew it, and see two SOWs with adjacent non-overlapping ranges — plus a readable error, not a 500, when you deliberately pick an overlapping start.

### Change history on the EDJEr admin screen (M)

On an EDJEr's admin page, show who changed that person's record, when, and what changed — using the history the app already records.

**Done looks like:** at `http://localhost:5176/compass/admin/edjers/1` change the person's state, reload, and see the change with actor and timestamp; then `curl -s -H "X-Dev-Roles: EDJEr,Compass Super Admin" "http://localhost:5009/api/audit-logs/entity?entityType=CompassEmployee&entityId=1"` and confirm a Compass-only admin still gets it.

### "No current work" filter on the Client Directory (M)

Let someone filter the Client Directory down to the clients that have nobody assigned right now, so sales can see who to call.

**Done looks like:** open `http://localhost:5176/compass/client-directory`, turn the filter on and see only clients with no current assignment, brand-new never-assigned clients included; `curl "http://localhost:5009/api/compass/client-directory?..."` returns the same set with the same count.

### Coach load report (M)

Add a report showing each coach, the people who report to them, and how many of those are on an active assignment over a date range the user picks — with a CSV download like the other reports have.

**Done looks like:** open `http://localhost:5176/compass/reports`, pick the new tab and a date range, see coach rows; click Export and get a CSV; `curl "http://localhost:5009/api/compass/reports/<new-route>?from=2025-01-01&to=2025-12-31"` returns the same rows.

### Team Directory time zone filter (S)

Let someone narrow the Team Directory to EDJErs in a particular time zone, combinable with the filters already there.

**Done looks like:** at `http://localhost:5176/compass/team-directory`, pick a time zone and see the list narrow; `curl "http://localhost:5009/api/compass/team-directory?timezone=America/Chicago&state=OH"` returns the same people with both filters applied.

### Team Directory CSV Export (S)

Add an Export CSV button to the Team Directory screen that downloads the currently filtered, currently sorted list of EDJErs as a CSV file, matching the export buttons already on the Reports screens.

**Done looks like:** Open Team Directory, apply any filter, click Export CSV, and see a downloaded file whose rows match the on-screen filtered table.

### Admin Notification Log Viewer (S)

A new Notifications screen under Admin lists recent coach notices (assignment-ending and SOW-extension emails) with recipient, status (Sent/Failed/Skipped), and timestamp, so an admin can see what actually went out without querying the database.

**Done looks like:** Open Admin, click the new Notifications tab, and see the list of `compass.assignment-ended.*` / `compass.sow-extension-added.*` log rows with real statuses.

### Bulk Reassign Coach (M)

On the Admin EDJErs list, an admin can select multiple EDJErs via checkboxes and reassign all of them to a different coach in one action, instead of opening each EDJEr's edit form individually.

**Done looks like:** Select 2+ EDJErs in Admin > EDJErs, choose a new coach from a bulk action bar, save, then reopen each EDJEr's detail page and see the new coach listed.

### Client Contract Renewal Badge (M)

The Client Directory and a client's own detail page show a Renewal Due badge when that client's MSA is approaching (or past) a renewal date an admin has set; clients with no renewal date set show nothing.

**Done looks like:** Set a near-term renewal date on a client in Admin > Clients, then see that client's row flagged "Renewal Due" on both the Client Directory and its own client view page.

### Saved Team Directory Views (M)

A user can save their current Team Directory search/filter/sort combination under a name and recall it later from a "My Views" dropdown, without re-entering every filter each time.

**Done looks like:** Apply filters on Team Directory, save them as "My Active Clients", reload the page, pick that saved view from the dropdown, and see the same filters re-applied.

