/*
 * LEAP landing shell — vanilla JS, no dependencies, no build step.
 *
 * SANCTIONED PLAIN-FETCH EXCEPTION: the project's `apiFetch` helper lives in
 * web/src (a React workspace module) and is unavailable to this static shell.
 * The shell probes /api/me same-origin with credentials:'include', which
 * preserves the exact HttpOnly cookie semantics apiFetch exists to guarantee.
 * This mirrors the documented /health plain-fetch exception.
 *
 * AUTH GATE IS UX, NOT AUTHORIZATION (T-46-03, accepted): the gate only
 * decides what to show. Every tile is a plain link; all data stays behind the
 * API's authorization policies. Bypassing the gate grants nothing.
 */
(function () {
  "use strict";

  // The 401 redirect target is a FIXED literal — never derived from request or
  // query data (open-redirect hygiene, T-46-02).
  var LOGIN_REDIRECT = "/auth/login?returnUrl=%2F";

  function show(id) {
    var el = document.getElementById(id);
    if (el) {
      el.hidden = false;
    }
  }

  function hide(id) {
    var el = document.getElementById(id);
    if (el) {
      el.hidden = true;
    }
  }

  // Builds one tile. Interactive tiles are anchors; a tile marked `disabled` is a
  // non-interactive <div> with aria-disabled. All text is set via textContent.
  // Phase 49 (49-08): Compass now ships a real SPA at /compass/, so no tile currently uses the
  // disabled/coming-soon path. It is kept — generic, and the next module will want it.
  //
  // Feature 008 (US1) added `icon`, `variant` and `cta` from `#s-home`. Every one of them is read
  // from the STATIC TILE CATALOG in renderTiles and never from the /api/me response. That is not a
  // stylistic preference: the launcher "evaluates no permissions" (LEAP FR-8), and a variant chosen
  // from the signed-in user would make this styling mechanism a permission-evaluation channel by the
  // back door. Platform Constraints exempt the tile catalog explicitly — it "is not user state".
  //
  // The tile is returned already wrapped in its <li>. Before this the anchors were appended straight
  // to the <ul>, which is invalid HTML and drops the implicit list semantics, so a screen reader
  // never announced "list, 3 items".
  function makeTile(config) {
    var tile;
    if (config.disabled) {
      tile = document.createElement("div");
      tile.className = "tile tile-disabled";
      tile.setAttribute("aria-disabled", "true");
    } else {
      tile = document.createElement("a");
      tile.className = "tile";
      tile.setAttribute("href", config.href);
      // EXPLICIT tabindex, and it is not redundant (#119/#120). WebKit does not put a plain
      // <a href> in the Tab cycle by default -- only form controls are tab stops there unless
      // the OS-level "Full Keyboard Access" preference is on, which is not the default. Every
      // tile on this page is a plain link (FR-005, pinned by #117), so without this the LAUNCHER
      // IS ENTIRELY UNREACHABLE BY KEYBOARD IN SAFARI: measured at zero of three tiles reached
      // after 25 Tab presses, against three of three in Chromium and Firefox.
      //
      // Same defect and same fix as CompassNav in issue #416, which shipped and survived weeks
      // because no WebKit project existed to fail. There is one now
      // (`shell-webkit`), and accessibility.spec.ts's Tab walk is what holds this.
      tile.setAttribute("tabindex", "0");
      if (config.newTab) {
        tile.setAttribute("target", "_blank");
        tile.setAttribute("rel", "noopener noreferrer");
      }
    }
    if (config.variant) {
      tile.className += " tile-" + config.variant;
    }

    if (config.icon) {
      var icon = document.createElement("span");
      icon.className = "tile-icon";
      // aria-hidden: the icon repeats the title beside it, so announcing it is pure noise.
      icon.setAttribute("aria-hidden", "true");
      icon.textContent = config.icon;
      tile.appendChild(icon);
    }

    var title = document.createElement("span");
    title.className = "tile-title";
    title.textContent = config.title;
    tile.appendChild(title);

    var desc = document.createElement("span");
    desc.className = "tile-desc";
    desc.textContent = config.description;
    tile.appendChild(desc);

    if (config.comingSoon) {
      var pill = document.createElement("span");
      pill.className = "coming-soon-pill";
      pill.textContent = "Coming soon";
      tile.appendChild(pill);
    }

    if (config.cta) {
      var go = document.createElement("span");
      go.className = "tile-go";
      // Also aria-hidden. The whole tile is one link already named by its title; a second reading of
      // "Open Compass" would announce the same destination twice.
      go.setAttribute("aria-hidden", "true");
      go.textContent = config.cta;
      tile.appendChild(go);
    }

    var item = document.createElement("li");
    item.appendChild(tile);
    return item;
  }

  function renderTiles() {
    var list = document.getElementById("tile-list");
    if (!list) {
      return;
    }
    list.textContent = "";

    // Title and icon come from `#s-home` (constitution Principle X rule 3 — the mockup wins on
    // presentation). Description is the owner-specified verbatim copy from issue #271 — do not
    // paraphrase it.
    var compassTile = makeTile({
      title: "EDJE Compass",
      description:
        "Team directory, client assignments, SOW & contract tracking, sales dashboard and reports.",
      href: "/compass/",
      icon: "\uD83E\uDDED",
      variant: "compass",
      cta: "Open Compass \u2192",
    });

    list.appendChild(compassTile);
  }

  // Capitalises the first letter and CHANGES NOTHING ELSE. Lowercasing the remainder to normalise
  // it would mangle the names most in need of their own spelling — "McDonald" -> "Mcdonald",
  // "DeAngelo" -> "Deangelo".
  function capitalize(token) {
    return token.charAt(0).toUpperCase() + token.slice(1);
  }

  // The greeting's FIRST NAME (owner request 2026-08-26, superseding the email address issue #271
  // put here). Prefers MeResponse.displayName ("Ada Lovelace" -> "Ada") and falls back to the
  // email local part ("ada.lovelace@..." -> "Ada"). Both are capitalised: an address is lowercase
  // by convention, and an IdP display name is only as tidy as whoever typed it.
  function firstName(me) {
    var displayTokens = nameTokens(me && me.displayName ? me.displayName : "");
    if (displayTokens.length > 0) {
      return capitalize(displayTokens[0]);
    }

    var emailTokens = nameTokens(me && me.email ? me.email : "");
    if (emailTokens.length === 0) {
      return "";
    }
    return capitalize(emailTokens[0]);
  }

  function renderGreeting(me) {
    var greeting = document.getElementById("greeting");
    if (!greeting) {
      return;
    }
    var who = firstName(me);
    greeting.textContent = "";

    // The mockup styles the name differently from the rest of the greeting, which a single
    // textContent cannot express — hence the child element. The name is still rendered from the
    // in-memory /api/me response and persisted NOWHERE (LEAP NFR-4): no storage, no cache.
    if (!who) {
      greeting.textContent = "Welcome";
      return;
    }
    greeting.appendChild(document.createTextNode("Welcome back, "));
    var nameEl = document.createElement("span");
    nameEl.className = "greeting-name";
    nameEl.textContent = who;
    greeting.appendChild(nameEl);
    greeting.appendChild(document.createTextNode("."));
  }

  // The name-ish tokens in a label, whether it is a real display name or an email address. Ported
  // from web/compass/src/lib/nav-identity.ts (getInitials) rather than imported: this is a static,
  // dependency-free shell with no build step to pull a workspace module through.
  function nameTokens(label) {
    var trimmed = label.trim();
    var atIndex = trimmed.indexOf("@");
    if (atIndex >= 0) {
      return trimmed
        .slice(0, atIndex)
        .split(/[._-]+/)
        .filter(Boolean);
    }
    return trimmed.split(/\s+/).filter(Boolean);
  }

  function getInitials(label) {
    var tokens = nameTokens(label);
    if (tokens.length === 0) {
      return "";
    }
    if (tokens.length === 1) {
      return tokens[0].slice(0, 2).toUpperCase();
    }
    return (tokens[0][0] + tokens[tokens.length - 1][0]).toUpperCase();
  }

  // Top bar avatar (issue #271): styled after CompassNav's userchip, showing initials in place of
  // a Google profile photo — MeResponse carries no picture URL. Falls back to email when
  // displayName is blank, same as CompassNav's own fallback.
  //
  // The VISIBLE initials stay two-letter (first + last, "AL") — that is the mockup's glyph. Only
  // the accessible name is trimmed to the first name (owner request 2026-08-26), matching the
  // greeting rather than reading out a full address to a screen reader.
  function renderAvatar(me) {
    var avatar = document.getElementById("leap-avatar");
    if (!avatar) {
      return;
    }
    var label = me && me.displayName ? me.displayName.trim() : "";
    if (!label && me && me.email) {
      label = me.email;
    }
    if (!label) {
      return;
    }
    avatar.textContent = getInitials(label);
    avatar.setAttribute("aria-label", firstName(me) || label);
    avatar.hidden = false;
  }

  function renderBadge() {
    if (window.__IS_EPHEMERAL_PREVIEW__ !== "true") {
      return;
    }
    var label = document.getElementById("ephemeral-badge-label");
    if (label) {
      label.textContent = window.__EPHEMERAL_LABEL__ || "Ephemeral preview";
    }
    show("ephemeral-badge");
  }

  function renderAuthed(me) {
    renderBadge();
    renderGreeting(me);
    renderAvatar(me);
    renderTiles();
    hide("loading-view");
    hide("error-view");
    show("authed-view");
  }

  function renderError() {
    hide("loading-view");
    hide("authed-view");
    show("error-view");
  }

  // Zero-caching service worker registration (D-21). Guarded by a feature
  // check and wrapped so a failed registration never breaks the shell.
  function registerServiceWorker() {
    if (!("serviceWorker" in navigator)) {
      return;
    }
    try {
      navigator.serviceWorker.register("/sw.js").catch(function () {
        /* registration failure is non-fatal — the shell works without it */
      });
    } catch (err) {
      /* older browsers: ignore */
    }
  }

  function runAuthGate() {
    fetch("/api/me", { credentials: "include" })
      .then(function (response) {
        if (response.status === 401) {
          window.location.assign(LOGIN_REDIRECT);
          return null;
        }
        if (!response.ok) {
          renderError();
          return null;
        }
        return response.json().then(function (me) {
          renderAuthed(me);
        });
      })
      .catch(function () {
        renderError();
      });
  }

  // ==========================================================================
  // Developer tools (2026-08-26)
  //
  // THE ONE IDENTITY-DEPENDENT SURFACE IN THIS FILE, and a deliberate,
  // owner-approved departure from LEAP FR-8 ("the launcher evaluates no
  // permissions"). Everything above still renders identically for every
  // signed-in user; tests/e2e/shell/no-permission-surface.spec.ts holds that
  // line for the greeting, the avatar and the tiles, and carries the one
  // documented exception for this block.
  //
  // The shell reads NO role string. It asks the API whether developer tools
  // exist here and whether this caller may use them, and renders the answer:
  //
  //   404  the environment has no developer tools -> nothing is shown at all
  //   403  they exist, this caller may not use them -> button, disabled
  //   200  enabled
  //
  // That is why there is no __DEVELOPER_TOOLS_ENABLED__ in config.js. A second
  // flag in the web container would have to be kept in step with the API's own
  // DeveloperTools:Enabled through nginx, two Dockerfiles and the chart, and a
  // disagreement between them is invisible until someone presses the button.
  //
  // THE KONAMI CODE IS NOT A SECURITY CONTROL. Anyone can press it and anyone
  // can skip it and POST to the endpoint directly. It hides the button from
  // people who are not looking for it; the API's environment gate and its
  // Compass Super Admin policy are what actually stop the clear.
  // ==========================================================================

  var DEVTOOLS_BASE = "/api/compass/developer-tools";
  var DEVTOOLS_AVAILABILITY_URL = DEVTOOLS_BASE + "/availability";
  var DEVTOOLS_CLEAR_URL = DEVTOOLS_BASE + "/clear-compass-data";

  var KONAMI_SEQUENCE = [
    "arrowup",
    "arrowup",
    "arrowdown",
    "arrowdown",
    "arrowleft",
    "arrowright",
    "arrowleft",
    "arrowright",
    "b",
    "a",
  ];

  // The last N keys pressed, oldest first. A ROLLING BUFFER rather than a
  // match-position counter: the counter version cannot recover from a repeated
  // prefix. The code opens "up up", so typing "up up up down down left right
  // left right b a" — one stray keypress, then the whole code correctly — left
  // the counter stranded and silently refused to open. A buffer holding the
  // last ten keys matches that run on its final key, because the last ten keys
  // ARE the code. Every restart case falls out of that for free.
  var konamiBuffer = [];
  var devToolsRevealed = false;
  var elementFocusedBeforeModal = null;

  function devToolsElement(id) {
    return document.getElementById(id);
  }

  // Sets the status line under the dialog. `kind` is "success", "error", or
  // omitted to clear it entirely.
  function setDevToolsStatus(message, kind) {
    var status = devToolsElement("devtools-status");
    if (!status) {
      return;
    }
    status.className = "devtools-status";
    if (!message) {
      status.textContent = "";
      status.hidden = true;
      return;
    }
    status.textContent = message;
    status.className = "devtools-status is-" + kind;
    status.hidden = false;
  }

  // Returns the dialog to its first step. Called on open AND on close so a
  // cancelled confirmation never survives to greet the next person who opens
  // the dialog already asking whether they are sure.
  function resetDevToolsDialog() {
    var actions = devToolsElement("devtools-actions");
    var confirm = devToolsElement("devtools-confirm");
    var confirmButton = devToolsElement("devtools-confirm-clear");
    if (actions) {
      actions.hidden = false;
    }
    if (confirm) {
      confirm.hidden = true;
    }
    if (confirmButton) {
      confirmButton.disabled = false;
      confirmButton.textContent = "Yes, clear it";
    }
  }

  function openDevToolsModal() {
    var modal = devToolsElement("devtools-modal");
    if (!modal) {
      return;
    }
    elementFocusedBeforeModal = document.activeElement;
    resetDevToolsDialog();
    setDevToolsStatus("");
    modal.hidden = false;

    var close = devToolsElement("devtools-close");
    if (close) {
      close.focus();
    }
  }

  function closeDevToolsModal() {
    var modal = devToolsElement("devtools-modal");
    if (!modal || modal.hidden) {
      return;
    }
    modal.hidden = true;
    resetDevToolsDialog();

    // Returning focus to the trigger is what makes the dialog usable by
    // keyboard: without it, focus falls back to <body> and the next Tab starts
    // from the top of the document.
    if (elementFocusedBeforeModal && elementFocusedBeforeModal.focus) {
      elementFocusedBeforeModal.focus();
    }
    elementFocusedBeforeModal = null;
  }

  function showConfirmStep() {
    var actions = devToolsElement("devtools-actions");
    var confirm = devToolsElement("devtools-confirm");
    setDevToolsStatus("");
    if (actions) {
      actions.hidden = true;
    }
    if (confirm) {
      confirm.hidden = false;
    }
    var confirmButton = devToolsElement("devtools-confirm-clear");
    if (confirmButton) {
      confirmButton.focus();
    }
  }

  function clearCompassData() {
    var confirmButton = devToolsElement("devtools-confirm-clear");
    if (confirmButton) {
      // Disabled for the duration: a double-click would otherwise fire a second
      // truncate against tables the first one already emptied, and the second
      // response would report zero rows and read as a failed clear.
      confirmButton.disabled = true;
      confirmButton.textContent = "Clearing…";
    }
    setDevToolsStatus("");

    fetch(DEVTOOLS_CLEAR_URL, { method: "POST", credentials: "include" })
      .then(function (response) {
        resetDevToolsDialog();
        if (response.ok) {
          setDevToolsStatus("Compass data cleared!", "success");
          return;
        }
        // The status code is included deliberately: 403 and 404 mean genuinely
        // different things here (lost the role vs. the environment does not
        // have this surface) and a bare "it failed" sends the reader nowhere.
        setDevToolsStatus(
          "Could not clear Compass data (HTTP " + response.status + ").",
          "error",
        );
      })
      .catch(function () {
        resetDevToolsDialog();
        setDevToolsStatus("Could not reach the server.", "error");
      });
  }

  // Renders the button. `enabled === false` is the 403 case: the requirement is
  // that an unauthorised user still SEES the button, so they learn the surface
  // exists and that they are not the one who may use it.
  function showDevToolsButton(enabled) {
    var bar = devToolsElement("devtools-bar");
    var button = devToolsElement("devtools-open");
    if (!bar || !button) {
      return;
    }
    button.disabled = !enabled;
    if (!enabled) {
      button.title = "Requires the Compass Super Admin role";
    }
    bar.hidden = false;
  }

  function revealDeveloperTools() {
    if (devToolsRevealed) {
      return;
    }
    devToolsRevealed = true;

    fetch(DEVTOOLS_AVAILABILITY_URL, { credentials: "include" })
      .then(function (response) {
        if (response.status === 200) {
          showDevToolsButton(true);
          return;
        }
        if (response.status === 403) {
          showDevToolsButton(false);
          return;
        }
        // 404 (no developer tools in this environment) and anything else:
        // reveal nothing. Allow another attempt, since an unreachable server is
        // not an answer about whether the surface exists.
        devToolsRevealed = false;
      })
      .catch(function () {
        devToolsRevealed = false;
      });
  }

  function handleKonamiKey(event) {
    if (devToolsRevealed || !event.key) {
      return;
    }
    konamiBuffer.push(event.key.toLowerCase());
    if (konamiBuffer.length > KONAMI_SEQUENCE.length) {
      konamiBuffer.shift();
    }
    // Joined rather than compared element by element: both are correct, and one
    // of them is obviously correct.
    if (konamiBuffer.join(" ") === KONAMI_SEQUENCE.join(" ")) {
      konamiBuffer = [];
      revealDeveloperTools();
    }
  }

  function wireDeveloperTools() {
    document.addEventListener("keydown", handleKonamiKey);

    var open = devToolsElement("devtools-open");
    if (open) {
      open.addEventListener("click", openDevToolsModal);
    }

    var close = devToolsElement("devtools-close");
    if (close) {
      close.addEventListener("click", closeDevToolsModal);
    }

    var clear = devToolsElement("devtools-clear-compass");
    if (clear) {
      clear.addEventListener("click", showConfirmStep);
    }

    var cancel = devToolsElement("devtools-cancel");
    if (cancel) {
      cancel.addEventListener("click", function () {
        resetDevToolsDialog();
        var clearButton = devToolsElement("devtools-clear-compass");
        if (clearButton) {
          clearButton.focus();
        }
      });
    }

    var confirmClear = devToolsElement("devtools-confirm-clear");
    if (confirmClear) {
      confirmClear.addEventListener("click", clearCompassData);
    }

    // Clicking the backdrop closes; clicking inside the dialog must not. The
    // target check is the whole difference.
    var modal = devToolsElement("devtools-modal");
    if (modal) {
      modal.addEventListener("click", function (event) {
        if (event.target === modal) {
          closeDevToolsModal();
        }
      });
    }

    document.addEventListener("keydown", function (event) {
      if (event.key === "Escape") {
        closeDevToolsModal();
      }
    });
  }

  document.addEventListener("DOMContentLoaded", function () {
    runAuthGate();
    registerServiceWorker();
    wireDeveloperTools();
  });
})();
