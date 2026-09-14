/**
 * The two derived status vocabularies Compass publishes, and why they are two.
 *
 * Both are derived by the server on every read and never stored, so a screen renders the word that
 * arrived rather than computing its own. Only the server knows the business date and the inclusive
 * end-date boundary, which is the comparison AC-42 names as the one independent implementations get
 * wrong (BR-11, FR-029).
 *
 * They live here rather than inside a feature because they cross features: a client's status appears
 * on the admin list, the Client Directory, the client view and the assignment picker, while an
 * assignment's appears on both history panels.
 */

/**
 * A **client's** derived status — three values since issue #274.
 *
 * - `Active` — holds at least one current assignment.
 * - `Inactive` — no assignment has ever existed. The state of every newly created client, and what an
 *   administrator sets up when an MSA or NDA is signed before any work begins.
 * - `Former` — we worked with them and no longer do: assignments exist, but none is current.
 *
 * Closed and **total**: never blank, never null, and never a fourth value. That totality is what lets
 * the Client Directory sort the column — it was never the *count* of values that mattered.
 */
export type ClientStatus = 'Active' | 'Inactive' | 'Former';

/**
 * One **assignment's** derived currency (BR-7) — binary, and deliberately a different type from
 * {@link ClientStatus}.
 *
 * An assignment cannot be `Former`: that describes a *client's history*, not one engagement. The
 * server enforces the same split with two enums, because before issue #274 a single shared naming
 * method meant two different things at eight call sites — and a client-level caller could have lost
 * `Former` silently. Keeping the types apart here means a row typed as an assignment cannot be
 * annotated with a value the server will never send it.
 */
export type AssignmentStatus = 'Active' | 'Inactive';
