# Running eShopOnWeb locally

## 1. What this app is

This is Microsoft's eShopOnWeb reference sample: a monolithic ASP.NET Core
e-commerce app with a product catalog, basket, checkout, and an admin area for
managing the catalog, plus a separate PublicApi project exposing the same data
over HTTP/Swagger. It uses Clean Architecture (ApplicationCore, Infrastructure,
Web) and a Blazor WebAssembly admin UI. Microsoft archived the upstream repo in
January 2025, so this copy is frozen and self-contained (no upstream updates).

## 2. Prerequisites

- **.NET 8 SDK** — required. The app targets `net8.0` and fails at runtime on
  a machine with only the .NET 10 runtime. It installs side by side with any
  newer SDK you already have.
  - Windows: `winget install Microsoft.DotNet.SDK.8`, then open a new terminal.
  - macOS: `brew install --cask dotnet-sdk@8`, or the installer from
    https://dotnet.microsoft.com/download/dotnet/8.0 (pick the SDK, not the runtime).
  - Linux (Debian/Ubuntu): `sudo apt-get install -y dotnet-sdk-8.0`. Fedora:
    `sudo dnf install dotnet-sdk-8.0`. If your distro's package is missing,
    use the install script at https://learn.microsoft.com/dotnet/core/install/linux-scripted-manual
    with `--channel 8.0`.
  - Check: `dotnet --list-sdks` must show an `8.0.x` line.
  - Also check: `dotnet --list-runtimes` must show `Microsoft.AspNetCore.App 8.0.x`.
- A trusted local dev cert (both apps run HTTPS):
  `dotnet dev-certs https --trust`
  - Windows/macOS: accept the trust prompt(s) that appear.
  - Linux: `--trust` isn't supported by the SDK; export and trust the cert in
    your distro's store, or just click through the one-time browser warning
    for `https://localhost:5001` and `https://localhost:5099`.
- Any modern browser, one terminal per process below (two total).

## 3. Build and test

From the `eShopOnWeb` folder (it has two `.sln` files, so always name one):

```
dotnet build eShopOnWeb.sln
dotnet test eShopOnWeb.sln
```

Observed: build succeeds with 0 errors (~9s warm-cache; first run also
restores NuGet packages, add 1-3 minutes). Tests: **74 passed, 0 failed**
(UnitTests 44, IntegrationTests 3, FunctionalTests 12,
PublicApiIntegrationTests 15), ~10s.

## 4. Run

Two terminals, both from the `eShopOnWeb` folder. Start PublicApi first.

```
cd src/PublicApi
dotnet run --launch-profile PublicApi
```
Wait for `Now listening on:` — PublicApi at `https://localhost:5099` (Swagger
at `https://localhost:5099/swagger`, http fallback `http://localhost:5098`).

```
cd src/Web
dotnet run --launch-profile Web
```
Web (the storefront) at `https://localhost:5001` (http fallback
`http://localhost:5000`). Confirmed against both projects'
`Properties/launchSettings.json`. PowerShell on Windows runs the identical
commands with forward slashes. Stop either with Ctrl+C.

## 5. A tour

1. Open `https://localhost:5001/` — the catalog grid.
2. Use the **Brand** and **Type** dropdowns above the grid to filter (they
   auto-submit a GET with `BrandFilterApplied`/`TypesFilterApplied`).
3. Click a product, then **Add to basket**.
4. Log in top-right: `demouser@microsoft.com` / `Pass@word1`. Your basket
   carries over (the login handler transfers the anonymous basket to your
   account).
5. Go to Basket, then **Checkout**, confirm the order.
6. Click **My orders** — URL is `/order/my-orders` (routes are lowercased and
   hyphenated). Click **Detail** on an order (`/order/detail/1`).
7. Open `https://localhost:5099/swagger` and try `GET /api/catalog-items`
   (e.g. `pageSize=2&pageIndex=0`) — returns catalog JSON straight from
   PublicApi.
8. Open `https://localhost:5001/admin`. It loads fine (HTTP 200) for anyone —
   the page is a Blazor WebAssembly shell; the real `[Authorize]` check runs
   client-side after it loads, and `demouser` (not an admin) gets bounced to
   the login page. Log out and back in as `admin@microsoft.com` /
   `Pass@word1` to actually use it. Note: BlazorAdmin edits go through
   PublicApi's own in-memory catalog, which is a separate process from Web's —
   an edit here will not appear on the storefront pages in step 1/2. That's a
   quirk of running two processes each with `UseInMemoryDatabase`, not a bug.

## 6. Common problems

- **`dotnet build` fails with MSB1011** ("more than one project or solution
  file"): this folder has `eShopOnWeb.sln` and `Everything.sln`. Always pass
  `eShopOnWeb.sln` explicitly.
- **`dotnet: command not found` or SDK errors**: no .NET 8 SDK installed, or
  only .NET 10 is on PATH; re-check `dotnet --list-sdks`.
- **App starts but pages 500 or throw at startup**: you're running under the
  .NET 10 runtime only. Check `dotnet --list-runtimes` for
  `Microsoft.AspNetCore.App 8.0.x`; install the .NET 8 SDK if it's missing.
- **Browser still warns about the certificate** after `dotnet dev-certs https
  --trust`: restart the browser, or just accept the one-time warning per
  origin (`5001` and `5099` are separate origins, each needs its own
  click-through) — Firefox and Linux Chrome commonly need this.
- **Port already in use** (`5000`/`5001`/`5098`/`5099`): a previous
  `dotnet run` is still running; find and stop it, or edit the ports in that
  project's `launchSettings.json`.
- **`/admin` looks broken or never shows content**: PublicApi must be running
  before you load `/admin` — BlazorAdmin's login/auth check calls PublicApi
  directly (`apiBase: https://localhost:5099/api/` in
  `src/BlazorAdmin/wwwroot/appsettings.json`); if PublicApi isn't up, the
  auth check just fails silently client-side.
