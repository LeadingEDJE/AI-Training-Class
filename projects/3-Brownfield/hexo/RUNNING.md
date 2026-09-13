# Running this locally

## 1. What this is

Hexo is a static-site generator: a library plus a CLI that turns Markdown posts
into a plain HTML/CSS/JS blog. This `hexo` folder is the Hexo **source itself**,
not a website: `lib/` is the TypeScript source, and `npm run build` compiles it
to `dist/`, which is what actually runs. The `hexo` command itself does not
come from this package — it comes from the separate `hexo-cli` package (a
dependency of this one, invoked by `bin/hexo`), which finds and drives
whichever Hexo library is installed in the current site. `example-site/`
inside this folder is a small starter blog whose `package.json` depends on
`"hexo": "file:.."`, so it runs your locally-built `dist/` — which is why
section 3 must be done first.

## 2. Prerequisites

Tested on Node 22, 24, and 25 — `npm test` reaches the same 1287 passing /
5 pending / 5 failing result on all three (Node 20.19+ also satisfies the
`engines` field but wasn't re-verified in this pass).

```
node --version
```

Should print `v20.19.0` or higher. If it doesn't, install a newer Node first.

### Installing Node.js

No Node.js and no version manager? Pick your platform:

- **macOS:** `brew install node@22` then `brew link --overwrite node@22` (if `brew` is missing, the installer at https://nodejs.org is the shortest path).
- **Windows:** `winget install OpenJS.NodeJS.LTS` (or the `.msi` from https://nodejs.org). Open a new terminal afterward.
- **Linux (Debian/Ubuntu):** `apt`'s Node is often too old — `curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -` then `sudo apt-get install -y nodejs`. Fedora: `sudo dnf install nodejs22`.

Then check: `node --version` and `npm --version`.

## 3. Build and test the hexo source

Run these from the `hexo` folder (not `example-site`):

```
npm ci
npm run build
npm test
```

- `npm ci` installs dependencies (a few seconds warm, up to a minute or two
  on a cold laptop). You may see `EBADENGINE` warnings from third-party
  packages — those are harmless noise; only worry if `node --version` is
  below 20.19 or the install actually errors out.
- `npm run build` runs `tsc -b`, compiling `lib/` to `dist/` (seconds; near
  instant on a second run since it's incremental). This step is required —
  `dist/` is gitignored, so a fresh clone has none until you build it.
- `npm test` runs the mocha suite: **1287 passing, 5 pending, 5 failing**
  (~15-20 seconds). The 5 failures are pre-existing upstream, not something
  you broke: 4 are in `test/scripts/processors/asset.ts` and only fail
  because of test ordering, 1 (`generate - future posts`) is date-related.

## 4. Run the example site

```
cd example-site
npm install
npx hexo generate
npx hexo server -p 4111
```

Open http://localhost:4111 in a browser. Stop the server with `Ctrl+C`.

Because `example-site` depends on the hexo folder via `file:..`, it always
uses whatever is currently in `dist/`. If you edit anything under `lib/`, run
`npm run build` in the `hexo` folder again before `hexo generate` picks it up
— editing `lib/` alone changes nothing until rebuilt. (Verified: editing a
string in `meta_generator.ts`, rebuilding, and regenerating changed the output
HTML; reverting and rebuilding removed it.)

The running server picks up new/edited **posts** immediately, but a
`_config.yml` change needs a server restart to take effect.

## 5. A tour

The example site is seeded as "Northwind Engineering," a fictional
company's internal blog: 12 posts spread from 2023 to 2026, 3 categories,
and 5 tags, so pagination, tag pages, archives, and reading time all have
real data to show.

1. With the server running, visit http://localhost:4111 — the front page
   is paginated (`per_page: 5`), so open page 2 to see the rest.
2. Open the archives (`http://localhost:4111/archives/`) and a tag page
   (e.g. `http://localhost:4111/tags/postgres/`) — several posts share
   tags, which is what related-posts/tag-cloud features key off of.
3. Create a post: `npm run new -- "My first post"`. This creates
   `source/_posts/My-first-post.md`. Edit it, add some text, and refresh the
   site — the running server watches `source/`, so no regenerate is needed;
   your post appears on the home page.
4. Add a tag: put `tags: [workshop]` in the post's front matter, save, and
   visit `http://localhost:4111/tags/workshop/` — your post is listed there.
5. Run `npm run clean` — deletes `public/` and `db.json` (generated output
   and cache). Run `npm run generate` again before the site works.
6. Open `_config.yml` and change the `title:` field, then `npm run generate`
   and restart the server — the page `<title>` and header change.
7. The active theme lives in `example-site/themes/landscape/`. A post's
   HTML is rendered by `themes/landscape/layout/post.ejs`, and the
   partials it uses are in `themes/landscape/layout/_partial/`. Edit a
   template there and the running server picks it up.

Clean up before you start building your own feature: delete any test posts
you added, revert `_config.yml` if you changed it, and run `npm run clean`.

## 6. Common problems

- **Port 4000 already in use.** That's Hexo's default; we use `4111` here to
  dodge collisions. Pass any free port with `-p`.
- **`node --version` is below 20.19.** Install a newer Node. An `EBADENGINE`
  warning alone is harmless (see section 3) — only a failed install matters.
- **You edited `lib/` and nothing changed.** Run `npm run build` in the
  `hexo` folder, then re-run `hexo generate` in `example-site`.
- **`npm install` was run in the wrong folder.** `hexo` and `example-site`
  have separate `package.json`/`node_modules` — installing one does not
  install the other's dependencies.
- **`hexo: command not found`.** It's not installed globally. Use
  `npx hexo ...` from inside `example-site`, as this guide does.
