import createFetchClient, { type Middleware } from "openapi-fetch";
import createClient from "openapi-react-query";
import type { paths, components } from "../generated/schema.d.ts";

// Authentication is a server-managed HttpOnly cookie session (phase 41): the
// client injects no credential header. `credentials: "include"` (set on the
// fetch client below) sends the cookie; this middleware only surfaces 401s.
const sessionMiddleware: Middleware = {
  async onResponse({ response }) {
    if (response.status === 401 && typeof window !== "undefined") {
      // Dispatch session-expired event — SessionExpiredOverlay handles the UX.
      // Do NOT redirect here: the overlay provides a controlled login flow.
      window.dispatchEvent(new CustomEvent("session-expired"));
    }
    return response;
  },
};

const fetchClient = createFetchClient<paths>({
  baseUrl: (typeof window !== "undefined" && window.__API_BASE_URL__) || "",
  credentials: "include",
});
fetchClient.use(sessionMiddleware);

const $api = createClient(fetchClient);

export { $api, fetchClient };
export type { paths, components };
