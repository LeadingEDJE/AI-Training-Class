// LEAP shell — LOCAL DEV config only.
//
// Deployed environments NEVER serve this file: nginx's exact-match
// `location = /config.js` synthesizes runtime values (from envsubst of the
// Helm web.env vars) and shadows this static file entirely. It exists so the
// static-file dev experience (and the Playwright shell project) has sane
// defaults without an nginx layer.
window.__API_BASE_URL__ = "";
window.__IS_EPHEMERAL_PREVIEW__ = "";
window.__EPHEMERAL_LABEL__ = "";
