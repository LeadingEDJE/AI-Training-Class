using System.Reflection;
using System.Text.RegularExpressions;
using LeadingEDJE.Leap.Api.Platform.Authorization;
using LeadingEDJE.Leap.Api.Platform.Services.OpenApi;
using LeadingEDJE.Leap.Api.Tests.Compass;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace LeadingEDJE.Leap.Api.Tests.Architecture;

/// <summary>
/// The US5/#55 coverage gate: every Compass HTTP surface carries a server-side authority check, and a
/// surface added later without one fails this suite.
/// </summary>
/// <remarks>
/// <para>
/// This file is the phase's deliverable, not its assertions. Compass currently exposes exactly
/// ONE endpoint and it is a read. "Every write surface carries a check" is therefore trivially true
/// today and would stay green through the entire addition of an ungated write in Stream 2. The
/// enumeration is what survives that; the individual status-code tests in
/// <c>CompassAuthorizationTests</c> are not.
/// </para>
/// <para>
/// Discovery is from the built application's <see cref="EndpointDataSource"/>, not from source
/// text. A regex over <c>api/Modules/Compass/Endpoints/</c> would miss a route mapped from
/// anywhere else and would report a policy that exists in the source but never reaches the pipeline.
/// Reading the composed endpoint graph asks the question the runtime answers.
/// </para>
/// <para>
/// ⚠️ Every assertion here is paired with a non-vacuity guard. A path regex matching zero files
/// does not fail — it PASSES — and eight gates in this repository were found silently fail-open for
/// exactly that reason (a ninth in feature 003). An enumeration is the most vacuity-prone shape there
/// is: discover nothing, iterate nothing, pass. So the discovered count is asserted non-zero before
/// anything is concluded from it, and <see cref="TheDetector_Fires_WhenAnEndpointHasNoAuthorityCheck"/>
/// proves the predicate can return false at all.
/// </para>
/// <para>
/// Reads are covered too, deliberately. The task text names write surfaces, but the gate checks
/// every Compass endpoint regardless of method. Two reasons: Compass's only surface today is a read,
/// so a write-only gate would have nothing to bite on and could not be proven to fail (T046 asks
/// exactly that — remove the check from <c>CompassEmployeeEndpoints</c> and watch this fail); and a
/// leaked read is a real finding here, since sequential employee ids are enumerable.
/// </para>
/// </remarks>
public class CompassAuthorizationCoverageTests
{
    /// <summary>Route prefix owned by the Compass module.</summary>
    private const string CompassRoutePrefix = "/api/compass";

    /// <summary>
    /// The number of Compass surfaces expected today. A tripwire, not a specification.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bumping this is the intended way to notice you added a surface. If it fails, add the new route
    /// to <c>CompassAuthorizationTests</c> too — the status-code suite does not discover routes on its
    /// own and will otherwise keep proving things about the old set.
    /// </para>
    /// <para>
    /// 1 → 7 (feature 004 US1, issue #58). Lookup administration added six: GET/POST/PUT for
    /// employee types and for invoice frequency types, all behind
    /// <c>RolePolicy.CompassSuperAdmin</c> via <c>CompassAdminRouteGroup</c>. This gate fired on the
    /// merge exactly as its header predicts — Compass's first writes arrived and the count moved.
    /// </para>
    /// <para>
    /// The paired instruction needed no work: <c>CompassAuthorizationTests</c>' write assertions
    /// (<c>CompassAdmin_IsRefusedEveryCompassWrite</c>,
    /// <c>BaseEdjErOnly_IsRefusedEveryCompassWrite</c>) sweep the endpoint graph rather than naming
    /// routes, so the six new surfaces are covered the moment they exist. That is the property this
    /// file's header argues for, now demonstrated rather than asserted.
    /// </para>
    /// <para>
    /// 7 → 11 (feature 005, epic #47). The Compass application read surface added four: the two
    /// directories and their two detail views. They sit OUTSIDE <see cref="BoundaryRoutePrefix"/> and
    /// carry authentication rather than a Compass policy — see
    /// <see cref="EveryApplicationReadSurface_RequiresAuthentication"/> for why that is the right floor
    /// for them and not a relaxation.
    /// </para>
    /// <para>
    /// 11 → 15 (feature 006 US1, issue #62). The assignment write surface added four:
    /// <c>GET /</c>, <c>GET /{id}</c>, <c>POST /</c> and <c>PUT /{id}</c> under
    /// <c>/api/compass/assignments</c>, all behind <see cref="RolePolicy.CompassOps"/>. This is
    /// Compass's first surface where the READS share the write group's policy rather than the
    /// read-surface's bare-authentication floor — see contracts/assignment-write-surface.md §1
    /// (research R-7).
    /// </para>
    /// <para>
    /// 11 → 15 (feature 004 US2, issue #59), independently. EDJEr configuration added four: GET
    /// collection, GET by id, POST and PUT on <c>/api/compass/v1/admin/edjers</c>, all behind
    /// <c>RolePolicy.CompassSuperAdmin</c> via the same route group as the lookups.
    /// </para>
    /// <para>
    /// Both of the last two increments were authored as 11 → 15, independently, on branches that did
    /// not see each other — 006 US1 and 004 US2 were in flight at the same time. Merging them
    /// produced exactly the collision this constant exists to force: neither branch's number was wrong
    /// on its own, and neither was right afterwards. That is the tripwire doing its job at the moment it
    /// is hardest to notice a gap, so resist the urge to make it a computed value.
    /// </para>
    /// <para>
    /// 15 → 21 (feature 004 US3, issue #60). Client configuration added six: GET collection, GET
    /// by id, POST and PUT on <c>/api/compass/v1/admin/clients</c>, plus POST and PUT on the nested
    /// <c>/{clientId}/billable-time-categories</c> routes. The nested pair is the reason this count
    /// is worth keeping. They are mapped on the same <c>CompassAdminRouteGroup</c> as their parent,
    /// so they inherit <c>RolePolicy.CompassSuperAdmin</c> — but a nested route is the easiest kind to
    /// map outside a gated group by accident, and one that was would be authorised by nothing while
    /// every other test in the feature still passed. Moving this number is what makes someone look.
    /// </para>
    /// <para>
    /// 21 → 25 (merging 006 US1 into 004 US2/US3, 2026-08-14). Both prior increments assumed a
    /// starting count of 11 and were correct in isolation; merged together the true total is
    /// <c>11 + 4 (assignments) + 4 (edjers) + 6 (clients) = 25</c>, not 15 and not 21. This is the
    /// exact collision the "resist the urge to make it computed" note above predicted.
    /// </para>
    /// <para>
    /// 25 → 27 (feature 006 US2, issue #63, 2026-08-14). The assignment picker surface added
    /// two reads: <c>GET /pickers/clients</c> and <c>GET /pickers/edjers</c> under
    /// <c>/api/compass/assignments</c>, behind the same <see cref="RolePolicy.CompassOps"/> as the
    /// rest of the write group (contract §1) — needed now, not deferred, because the "new assignment"
    /// entry points on the EDJEr/Client records (feature 006's entry-point pivot) consume them
    /// directly.
    /// </para>
    /// <para>
    /// 27 → 27 (still feature 006, 2026-08-17) — a policy change, not a count change.
    /// <c>GET /{id}</c> under <c>/api/compass/assignments</c> moved OFF the write group's
    /// <c>CompassOps</c> onto its own group under the new <see cref="RolePolicy.CompassElevated"/>
    /// (AC-16/FR-025: Admin and Sales can view an assignment, same as they can the client-view's
    /// View-SOW affordance — this feature's original write-group mount had accidentally also gated the
    /// read for the two roles that were never meant to be excluded from it). No route was added or
    /// removed, so the count is unchanged; <see cref="CompassPolicies"/> below gained a fifth entry
    /// instead.
    /// <para>
    /// 27 → 30 (feature 010, after merging main). Contract periods gained
    /// <c>POST /api/compass/v1/admin/sows</c> plus list and by-id reads. The reads exist because
    /// VERIFICATION needs them, not because a screen does: SC-002 requires every migrated
    /// relationship checked at 100% rather than sampled, and a create-only surface makes that
    /// impossible in principle. The create route additionally guards a VALIDATION BYPASS —
    /// <c>SowType.LegacyMigrated</c> is exempt from both partial database rules and is restricted to
    /// the migration principal — so its authorization coverage matters more than most.
    /// </para>
    /// <para>
    /// Feature 010 added NO assignment routes. It originally carried its own
    /// <c>/api/compass/v1/admin/client-assignments</c> create-and-read surface; merging main showed
    /// feature 006 had shipped an equivalent one at <c>/api/compass/assignments</c> with an identical
    /// request shape, so 010's was deleted rather than kept alongside it. The migration posts to 006's
    /// routes — which is what "a migrated record passes the same validation as one a person creates"
    /// was supposed to mean all along.
    /// </para>
    /// </para>
    /// <para>
    /// 27 → 29 (feature 007 US1, issue #75). The Sales Dashboard added two:
    /// <c>GET /api/compass/dashboard</c> and <c>GET /api/compass/dashboard/breakdown/{category}</c>,
    /// both gated by the new <see cref="RolePolicy.CompassReporting"/> policy — added to
    /// <see cref="CompassPolicies"/> below in the same change, or
    /// <c>NoCompassSurface_IsGatedByATimesheetOrOotoPolicy</c> would misreport them as gated by a
    /// non-Compass policy.
    /// </para>
    /// <para>
    /// The 23-vs-27 collision this line just survived is the one the note above predicted.
    /// Feature 007 measured 21 → 23 and feature 006 measured 21 → 27, each correct against the main
    /// it branched from; neither is right merged. 29 is 27 + the two dashboard routes, re-derived
    /// against the merged tree rather than by picking a side. Do not make it computed — a count that
    /// derives itself from the routes cannot notice a route that should not be there.
    /// </para>
    /// <para>
    /// 29 → 30 (feature 006 US6, issue #64). The assignment override selector needs the list of
    /// invoice-frequency cadences, and the only route serving them was
    /// <c>GET /api/compass/v1/admin/invoice-frequency-types</c> behind
    /// <see cref="RolePolicy.CompassSuperAdmin"/> — so a Compass Ops user was authorised to SET an
    /// override but forbidden from listing the values to set it to. <c>GET
    /// /api/compass/assignments/pickers/invoice-frequency-types</c> is the read, mounted on the same
    /// <see cref="RolePolicy.CompassElevated"/> group as <c>GET /{id}</c> because everyone who can
    /// reach the assignment detail screen needs it, not only those who can write it.
    /// </para>
    /// <para>
    /// 29 → 30 (feature 007 US2, T051). One new route,
    /// <c>GET /api/compass/reports/availability</c> — the Availability Report (AC-38, #76). This is the
    /// gate's own sanctioned path, named verbatim in its failure message ("Update
    /// ExpectedCompassEndpointCount AND add the new route to CompassAuthorizationTests"), not a
    /// weakening: the constant exists to make a NEW Compass surface an explicit decision, and this one
    /// is declared. <c>CompassAuthorizationTests</c> needs no edit — it discovers routes by prefix and
    /// its tripwire counts WRITES; this route is a <c>GET</c>, exactly as T033 recorded for the two
    /// dashboard routes. <c>CompassPolicies</c> already carries
    /// <c>RolePolicy.CompassReporting</c> from US1, which is the policy this route requires.
    /// </para>
    /// <para>
    /// Both paragraphs above are real, and 30 was wrong for both — the answer is 31. This is
    /// exactly the collision the note two paragraphs up predicted: feature 006 US6 measured 29 → 30
    /// for the cadence picker and feature 007 US2 measured 29 → 30 for the Availability Report, each
    /// correct against the main it branched from and neither correct merged. 31 is re-derived against
    /// the merged tree — 29 plus BOTH new routes — rather than by keeping whichever landed last.
    /// </para>
    /// <para>
    /// 31 → 34 (merging 006 US3, issue #66, T082, 2026-08-18). A THIRD collision, of the exact
    /// same shape: feature 006 US3 measured 27 → 30 for the SOW write surface's three routes
    /// (<c>GET /</c>, <c>POST /</c>, <c>PUT /{sowId}</c> under
    /// <c>/api/compass/assignments/{assignmentId}/sows</c>, all on the SAME
    /// <c>CompassWriteRouteGroup</c> as the assignment surface under <c>RolePolicy.CompassOps</c> — no
    /// widened read exception for SOWs, contract §1, unlike assignments), correct against the main it
    /// branched from and not accounted for by any of the three paragraphs above. 34 is 31 (already
    /// re-derived for the dashboard/cadence/availability three-way collision) plus these three SOW
    /// routes — not 30 (US3's own branch-relative count) and not left at 31. No new policy from this
    /// story; <see cref="CompassPolicies"/> is otherwise unchanged.
    /// </para>
    /// <para>
    /// 34 → 35 (merging 006 US7, issue #65, 2026-08-19). A FOURTH collision, of the identical
    /// shape, landing in parallel with US3 above: the AC-19 deactivation precondition gained its own
    /// read, <c>GET /api/compass/assignments/blockers/{employeeId}</c>, on the write group under
    /// <see cref="RolePolicy.CompassOps"/> rather than the more permissive
    /// <see cref="RolePolicy.CompassElevated"/> group that serves <c>GET /{id}</c> — Ops is the role
    /// that end-dates assignments, so Ops is who can act on the answer, whereas Compass Admin is
    /// READ-ONLY (AC-44) and has no part in the decision this route serves. US7 measured 31 → 32
    /// against the same base US3 measured 31 → 34 against, and neither branch's count accounts for the
    /// other. 35 is 34 (US3's already-reconciled count, which this route does not touch) plus this one
    /// blockers route — not 32 (US7's own branch-relative count) and not left at 34. No new policy from
    /// this story either; <see cref="CompassPolicies"/> is otherwise unchanged.
    /// </para>
    /// <para>
    /// 35 → 36 (feature 007 US4, issue #78). The Assignment Start lookup adds
    /// <c>GET /api/compass/reports/assignment-start</c> to the existing reports group, so it inherits
    /// <see cref="RolePolicy.CompassReporting"/> from the group rather than declaring its own — Sales,
    /// Ops or the Compass root, with Compass Admin refused at the server (FR-018, FR-019). No new
    /// policy, so <see cref="CompassPolicies"/> is unchanged.
    /// </para>
    /// <para>
    /// And it happened again, twice, within a day — this line has now been renumbered THREE
    /// times on one branch (2026-08-19). #78 measured 29 → 30 when it was written, rebased to
    /// 31 → 32, then to 32 → 33 when #65 landed, and finally to 35 → 36 when #66's three SOW routes
    /// and #65's blocker route both arrived. Not one of those was wrong when it was written; every one
    /// was wrong by the time it merged.
    /// That is the whole case for keeping this constant hand-written, and it is now supported by
    /// six collisions rather than an argument. A computed count would have absorbed every one of
    /// those parallel routes silently and told nobody a surface had grown. The conflict IS the
    /// notification — it is supposed to be annoying, and each time it has fired, the resolution has
    /// been to re-derive against the merged tree rather than keep whichever side landed last.
    /// </para>
    /// 35 → 39 (merging main into feature 010 / PR #281, 2026-08-19). A FIFTH collision — and
    /// the first one git did not flag. The four above all produced a merge conflict, because
    /// the two sides wrote different numbers. Here both sides had independently arrived at the
    /// literal <c>35</c> from a shared base of 31 — feature 010 by adding its four routes, feature
    /// 006 US3/US7 by adding theirs — so the constant auto-merged with NO conflict and NO warning,
    /// while the true merged count was 39. Identical values are not agreement; they were answers
    /// to different questions. A ratchet constant that both branches touch will merge silently
    /// whenever the two increments happen to coincide, and only running the discovery test catches
    /// it. Run it after every merge — do not trust a clean auto-merge on this line.
    /// </para>
    /// <para>
    /// The four routes feature 010 contributes: <c>GET /api/compass/v1/admin/sows/</c>,
    /// <c>GET /api/compass/v1/admin/sows/{id}</c>, <c>POST /api/compass/v1/admin/sows/</c> (the
    /// migration's contract-period create path — distinct from 006's
    /// <c>/api/compass/assignments/{assignmentId}/sows</c> surface, and the only route to the
    /// identity-gated <c>LegacyMigrated</c> bypass), and
    /// <c>GET /api/compass/v1/admin/migration/provenance</c>. All four sit on the
    /// <c>CompassAdminRouteGroup</c> under <see cref="RolePolicy.CompassSuperAdmin"/>. 39 is 35 plus
    /// these four, re-derived by running the enumeration against the merged tree. No new policy;
    /// <see cref="CompassPolicies"/> is unchanged.
    /// </para>
    /// <para>
    /// 39/36 → 40 (merging main into feature 010 / PR #281 a second time, 2026-08-19). A
    /// SEVENTH collision, resolved the same way: main reached 36 by adding the Assignment Start
    /// lookup to 35, feature 010 reached 39 by adding its four migration routes to the same 35, and
    /// neither number accounts for the other. 40 is 35 plus BOTH — re-derived by running the
    /// enumeration against the merged tree, not by keeping whichever side landed last.
    /// </para>
    /// <para>
    /// 40 + 1 → 41 (merging main into feature 007 US3 / PR #311, 2026-08-20). Another collision
    /// of the same shape. This branch had already re-derived 36 → 37 for
    /// <c>GET /api/compass/reports/assignment-duration</c>; main then reached 40 by way of feature
    /// 010's four migration routes. Neither number accounts for the other, so 41 is 40 plus this
    /// branch's one route — and, per the warning three paragraphs up, verified by running the
    /// discovery test against the merged tree rather than by arithmetic. The ordinal is left off
    /// deliberately: the counts above no longer agree on one (two separate paragraphs claim
    /// "SEVENTH"), and inventing an eighth would add a third disputed number to a file whose whole
    /// point is that numbers drift when nobody re-measures.
    /// </para>
    /// <para>
    /// 41 → 42 (spec 009 Slice 2, T019–T026). One new route,
    /// <c>GET /api/compass/v1/invoice-frequencies</c> — read family 7 of the published Directory
    /// boundary (FR-010), behind <see cref="RolePolicy.CompassAdmin"/> via its own route group in
    /// <c>CompassInvoiceFrequencyEndpoints</c>. This is a <c>GET</c>, so
    /// <c>CompassAuthorizationTests</c>' write-count tripwire is untouched, matching the precedent
    /// recorded above for the Availability Report and dashboard routes. No new policy;
    /// <see cref="CompassPolicies"/> is unchanged.
    /// </para>
    /// <para>
    /// 42 → 43 (spec 009 Slice 3, T027–T035). One new route,
    /// <c>GET /api/compass/v1/clients/{id}</c> — read family 2 of the published Directory boundary
    /// (FR-005), behind <see cref="RolePolicy.CompassAdmin"/> via its own route group in
    /// <c>CompassClientEndpoints</c>. Another <c>GET</c>, so <c>CompassAuthorizationTests</c>'
    /// write-count tripwire is again untouched. No new policy; <see cref="CompassPolicies"/> is
    /// unchanged.
    /// </para>
    /// <para>
    /// 43 → 44 (spec 009 Slice 4, T036–T043). One new route,
    /// <c>GET /api/compass/v1/clients/{id}/billable-categories</c> — read family 6 of the published
    /// Directory boundary (FR-009). It is mapped inside the EXISTING <c>CompassClientEndpoints</c>
    /// group, so it inherits <see cref="RolePolicy.CompassAdmin"/> from the group rather than declaring
    /// its own. Another <c>GET</c>, so <c>CompassAuthorizationTests</c>' write-count tripwire is again
    /// untouched. No new policy; <see cref="CompassPolicies"/> is unchanged.
    /// </para>
    /// <para>
    /// 44 → 47 (spec 009 Slice 5, T044–T052). THREE new routes — read family 3 of the published
    /// Directory boundary (FR-006, FR-012): <c>GET /api/compass/v1/assignments/{id}</c> (its own route
    /// group in the new <c>CompassAssignmentEndpoints</c>), <c>GET
    /// /api/compass/v1/employees/{id}/assignments</c> (mapped inside the EXISTING
    /// <c>CompassEmployeeEndpoints</c> group), and <c>GET /api/compass/v1/clients/{id}/assignments</c>
    /// (mapped inside the EXISTING <c>CompassClientEndpoints</c> group). All three inherit or declare
    /// <see cref="RolePolicy.CompassAdmin"/>. All three are <c>GET</c>s, so
    /// <c>CompassAuthorizationTests</c>' write-count tripwire is unchanged. No new policy;
    /// <see cref="CompassPolicies"/> is unchanged.
    /// </para>
    /// <para>
    /// 47 → 48 (spec 009 Slice 6, T053–T062). One new route,
    /// <c>GET /api/compass/v1/assignments/{id}/sows</c> — read family 4 of the published Directory
    /// boundary (FR-007). It is mapped inside the EXISTING <c>CompassAssignmentEndpoints</c> group, so
    /// it inherits <see cref="RolePolicy.CompassAdmin"/> from the group rather than declaring its own.
    /// Another <c>GET</c>, so <c>CompassAuthorizationTests</c>' write-count tripwire is again untouched.
    /// No new policy; <see cref="CompassPolicies"/> is unchanged.
    /// </para>
    /// <para>
    /// 48 → 51 (issue #337, CSV export). Three new routes —
    /// <c>GET /api/compass/reports/{availability,assignment-duration,assignment-start}/export</c> —
    /// mapped inside the EXISTING <c>CompassReportEndpoints</c> group, so each inherits
    /// <see cref="RolePolicy.CompassReporting"/> from the group rather than declaring its own. That
    /// inheritance is the point: an export hands over the whole population in one request, so a route
    /// authorizing differently from the screen it exports would be a more permissive second door.
    /// All three are <c>GET</c>, so <c>CompassAuthorizationTests</c>' write-count tripwire is again
    /// untouched. No new policy; <see cref="CompassPolicies"/> is unchanged.
    /// </para>
    /// <para>
    /// Spec 009 Slice 1 (T011–T018) makes no appearance above, and that is correct, not an
    /// omission — investigated for T073. <c>GET /api/compass/v1/employees/{id}</c> is not spec
    /// 009's route: it predates the feature entirely. It was added by Phase 49 plan 49-05 (commit
    /// <c>e42e3108</c>, "feat(49-05): add versioned Compass endpoint behind one contract",
    /// 2026-08-01), originally as <c>{id:guid}</c> against the legacy <c>public.employees</c> table,
    /// then narrowed to <c>{id:int}</c> against <c>compass.employee</c> by feature 003's R1 (commit
    /// <c>00c24a86</c>) — a type/store change, not a new route. It was therefore already counted
    /// inside the baseline <c>41</c> the Slice 2 paragraph above measures FROM, long before spec 009
    /// started (the earliest spec 009 commit, Slice 1, landed 2026-08-21; Phase 49-05 landed
    /// 2026-08-01). Slice 1 grew <c>CompassEmployeeDto</c>'s fields (<c>HireDate</c>,
    /// <c>EmployeeType</c>, <c>StateOfResidence</c>, the nullable <c>Coach</c>, the tier-gated
    /// <c>TimeTracking</c> block) on that PRE-EXISTING route. It added no route, so
    /// <see cref="ExpectedCompassEndpointCount"/> does not move for it and there is nothing for this
    /// history to record — unlike every other entry above, which each names a route this constant's
    /// value actually changed for. Confirmed by
    /// <c>git log --all --oneline -S'compass/v1/employees' --follow -- '*CompassEmployeeEndpoints.cs'</c>,
    /// whose only two hits are Phase 49-05 (the route's origin) and the LEAP rename
    /// (<c>04f36514</c>) that carried the file forward unchanged.
    /// </para>
    /// <para>
    /// UNCHANGED at 51 by the developer-tools routes (2026-08-26), and the reason is worth
    /// knowing. <c>GET /api/compass/developer-tools/availability</c> and
    /// <c>POST .../clear-compass-data</c> are real Compass routes, but they are mapped only when
    /// <c>DeveloperToolsGate</c> allows it — and this gate builds the DEFAULT
    /// <c>TestWebApplicationFactory</c>, whose host runs in <c>Testing</c> and therefore reads
    /// <c>DeveloperTools:Enabled=false</c> from <c>appsettings.json</c>. They are genuinely absent
    /// from the endpoint graph this counts, so the number does not move.
    /// The consequence: if someone enables that flag in the default unit host, this assertion will
    /// report 53 and tell them a new Compass surface was added. It was not — the host changed.
    /// Bump this only for a route that exists unconditionally; otherwise give the new host its own
    /// factory, as <c>DeveloperToolsTestWebApplicationFactory</c> does. The integration project's
    /// <c>CompassAuthorizationTests.ExpectedCompassWriteSurfaceCount</c> DOES count the write, because
    /// <c>IntegrationTestFactory</c> enables the flag for every test in that project.
    /// </para>
    /// <para>
    /// 51 → 53 (issue #534, SOW Extension Report). Two new routes,
    /// <c>GET /api/compass/reports/sow-extension</c> and <c>GET
    /// /api/compass/reports/sow-extension/export</c> — mapped inside the EXISTING
    /// <c>CompassReportEndpoints</c> group, so both inherit <see cref="RolePolicy.CompassReporting"/>
    /// from the group rather than declaring their own, the same shape as the CSV-export entry above.
    /// Both are <c>GET</c>, so <c>CompassAuthorizationTests</c>' write-count tripwire is unchanged. No
    /// new policy; <see cref="CompassPolicies"/> is unchanged. Note this coincides with the 53 the
    /// paragraph above warns a stray developer-tools host would ALSO report — that collision is
    /// coincidental (different routes, different reason), not evidence the two are related.
    /// </para>
    /// <para>
    /// 53 → 54 (the ETL's legacy-timezone read). One new route,
    /// <c>GET /api/compass/v1/admin/migration/legacy-timezones</c> — mapped inside the EXISTING
    /// <c>migration</c> group in <c>CompassMigrationProvenanceEndpoints</c>, so it inherits
    /// <see cref="RolePolicy.CompassSuperAdmin"/> from <c>CompassAdminRouteGroup</c> rather than
    /// declaring its own, exactly as <c>/provenance</c> does beside it. It is a <c>GET</c>, so
    /// <c>CompassAuthorizationTests</c>' write-count tripwire is unchanged. No new policy;
    /// <see cref="CompassPolicies"/> is unchanged.
    /// The group policy is deliberately NOT this route's real gate. Like <c>/provenance</c>, the
    /// handler additionally requires the migration principal by IDENTITY, because
    /// <see cref="RolePolicy.CompassSuperAdmin"/> is satisfied by every Compass Super Admin and this
    /// route publishes the frozen legacy directory's timezone column. That check is asserted by
    /// <c>CompassLegacyTimezoneEndpointsTests</c> in both test projects, not here — this file measures
    /// route metadata, which cannot see an in-handler check.
    /// 54 was re-derived by running <see cref="TheEnumeration_DiscoversCompassSurfaces_SoAnEmptySweepCannotPass"/>
    /// and counting what it printed, not by adding one to 53 — which is what the paragraphs above
    /// ask for, and the only method that survives the silent auto-merge they record.
    /// </para>
    /// <para>
    /// 54 → 56 (issue #593, 2026-09-08). Two new routes: <c>DELETE
    /// /api/compass/assignments/{id}</c> (a true delete, cascading to every SOW under the assignment)
    /// and <c>DELETE /api/compass/assignments/{assignmentId}/sows/{sowId}</c> (one contract period
    /// alone). Both are mounted on their OWN <c>MapGroup</c>, under
    /// <see cref="RolePolicy.CompassSuperAdmin"/> — deliberately narrower than the
    /// <see cref="RolePolicy.CompassOps"/> the rest of this surface accepts, per the owner's explicit
    /// answer that only the Compass root may perform a permanent delete. This is the module's first
    /// <c>DELETE</c> verb anywhere, and its one deliberate exception to ADR-007/Principle VIII's
    /// "deactivate, never hard-delete" rule — see the remarks on
    /// <c>CompassAssignmentService.DeleteAsync</c> for the full reasoning. Per-route denial is
    /// <c>CompassAssignmentAuthorizationTests</c>'/<c>CompassSowAuthorizationTests</c>'; the integration
    /// project's <c>CompassAuthorizationTests.ExpectedCompassWriteSurfaceCount</c> counts both too.
    /// </para>
    /// <para>
    /// 56 → 55 (Timesheet/Ooto module removal). <c>GET /api/compass/v1/admin/migration/legacy-timezones</c>
    /// is gone: it published the frozen legacy directory's timezone column, which was ported from the
    /// Timesheet-owned <c>ILegacyTimezoneDirectory</c>/<c>LegacyEmployeeTimezone</c>, and those types
    /// were deleted with the rest of the Timesheet module rather than kept for one route. No
    /// replacement route exists. <c>CompassLegacyTimezoneEndpointsTests</c> is deleted alongside it.
    /// </para>
    /// </remarks>
    private const int ExpectedCompassEndpointCount = 55;

    /// <summary>
    /// The published Directory boundary's route prefix (ADR-004, ADR-008).
    /// </summary>
    /// <remarks>
    /// Compass serves TWO HTTP surfaces and they carry different authority requirements, so the gate
    /// has to tell them apart. Anything under this prefix is the versioned contract out-of-monolith
    /// consumers bind to; anything else under <see cref="CompassRoutePrefix"/> is the application read
    /// surface the Compass SPA calls.
    /// </remarks>
    private const string BoundaryRoutePrefix = "/api/compass/v1";

    private static readonly string[] WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>The five Compass policies. Any other value is not a Compass authority check.</summary>
    private static readonly string[] CompassPolicies =
    [
        RolePolicy.CompassSuperAdmin,
        RolePolicy.CompassAdmin,
        RolePolicy.CompassOps,
        RolePolicy.CompassSales,
        RolePolicy.CompassElevated,
        RolePolicy.CompassReporting,
    ];

    private static IReadOnlyList<RouteEndpoint> AllEndpoints()
    {
        using var factory = new TestWebApplicationFactory();

        // Forces the host to build; EndpointDataSource is empty until it has.
        using var _ = factory.CreateClient();

        return [.. factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()];
    }

    private static List<RouteEndpoint> CompassEndpoints() =>
        [.. AllEndpoints().Where(IsCompassRoute)];

    /// <summary>A route's path with exactly one leading slash, whatever the raw text carried.</summary>
    /// <remarks>
    /// ⚠️ Always compare the normalised path. This file originally hedged by testing the raw
    /// text against both the slashed and unslashed prefix, which worked — and masked the fact that
    /// <c>CompassAuthorizationTests</c> tested only the unslashed one and therefore matched nothing.
    /// <c>RoutePattern.RawText</c> carries the leading slash. One helper, one form, no hedging.
    /// </remarks>
    private static string NormalisedPath(RouteEndpoint endpoint) =>
        "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');

    private static bool IsCompassRoute(RouteEndpoint endpoint) =>
        NormalisedPath(endpoint).StartsWith(CompassRoutePrefix, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> MethodsOf(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];

    private static bool IsWrite(RouteEndpoint endpoint) =>
        MethodsOf(endpoint).Any(m => WriteMethods.Contains(m, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// True when the endpoint requires one of the four Compass policies and is not opted out.
    /// </summary>
    /// <remarks>
    /// An <see cref="IAllowAnonymous"/> anywhere in the metadata wins over an authorize attribute at
    /// runtime, so it has to disqualify the endpoint here too — otherwise the gate would report a
    /// surface as protected while the pipeline lets it through.
    /// </remarks>
    private static bool CarriesCompassAuthorityCheck(RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return false;
        }

        return endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(a => a.Policy is { } policy
                && CompassPolicies.Contains(policy, StringComparer.Ordinal));
    }

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join('/', MethodsOf(endpoint))} /{endpoint.RoutePattern.RawText?.TrimStart('/')}";

    // ---------------------------------------------------------------- non-vacuity

    [Fact]
    public void TheEnumeration_DiscoversCompassSurfaces_SoAnEmptySweepCannotPass()
    {
        // Arrange / Act
        var compass = CompassEndpoints();

        // Assert -- if this is ever 0, every other assertion in this file is meaningless
        compass.ShouldNotBeEmpty(
            "Discovered NO Compass endpoints. Either the route prefix moved or the host failed to "
            + "compose them -- do not read the rest of this suite as evidence until this passes.");
        compass.Count.ShouldBe(
            ExpectedCompassEndpointCount,
            $"Compass surface count changed. Discovered: {string.Join(", ", compass.Select(Describe))}. "
            + "Update ExpectedCompassEndpointCount AND add the new route to CompassAuthorizationTests.");
    }

    [Fact]
    public void TheDetector_Fires_WhenAnEndpointHasNoAuthorityCheck()
    {
        // Arrange -- a positive control. The predicate is only trustworthy if something fails it, and
        // asserting that over Compass alone is impossible while Compass is fully gated. /health is
        // deliberately anonymous, so it is the honest specimen.
        var health = AllEndpoints()
            .FirstOrDefault(e => e.RoutePattern.RawText?.Contains("health", StringComparison.OrdinalIgnoreCase) == true);

        // Act / Assert
        health.ShouldNotBeNull("expected a /health endpoint to use as the detector's positive control");
        CarriesCompassAuthorityCheck(health).ShouldBeFalse(
            "the detector reported an ungated endpoint as protected -- it cannot be trusted to fail");
    }

    // ---------------------------------------------------------------- the gate

    [Fact]
    public void EveryCompassWriteSurface_CarriesAServerSideAuthorityCheck()
    {
        // Arrange
        var compass = CompassEndpoints();
        compass.ShouldNotBeEmpty();
        var writes = compass.Where(IsWrite).ToList();

        // Act
        var ungated = writes.Where(e => !CarriesCompassAuthorityCheck(e)).Select(Describe).ToList();

        // Assert
        ungated.ShouldBeEmpty(
            $"Compass write surfaces with no Compass policy: {string.Join(", ", ungated)}");
    }

    [Fact]
    public void EveryPublishedBoundarySurface_ReadIncluded_CarriesAServerSideAuthorityCheck()
    {
        // Arrange -- the assertion that has teeth on the boundary, and the one T046's mutation removes
        var boundary = CompassEndpoints().Where(IsPublishedBoundary).ToList();
        boundary.ShouldNotBeEmpty(
            "no published-boundary surfaces discovered under " + BoundaryRoutePrefix
            + " -- this assertion would pass vacuously");

        // Act
        var ungated = boundary.Where(e => !CarriesCompassAuthorityCheck(e)).Select(Describe).ToList();

        // Assert
        ungated.ShouldBeEmpty(
            $"published Compass boundary surfaces with no Compass policy: {string.Join(", ", ungated)}");
    }

    [Fact]
    public void EveryApplicationReadSurface_RequiresAuthentication()
    {
        // Arrange -- the application read surface (ADR-008). It deliberately carries NO Compass
        // policy: AC-5 and AC-12 both open "Given any authenticated EDJEr", and the base EDJEr role is
        // implicit (BR-2). Requiring one of the four policies here would deny the two directories to
        // exactly the audience the criteria grant them to.
        //
        // ⚠️ This is a WEAKER check than the boundary's, and deliberately so -- but the thing it gives
        // up is recovered elsewhere, not discarded. What a baseline viewer may see is scoped PER FIELD
        // by the projection (BR-1, FR-005), which route metadata cannot express and which
        // role-scoped-read-projection.md's own suite asserts against response payloads. What must
        // never weaken is the floor: no application read surface may be anonymous.
        var readSurfaces = CompassEndpoints().Where(e => !IsPublishedBoundary(e)).ToList();
        readSurfaces.ShouldNotBeEmpty(
            "no application read surfaces discovered outside " + BoundaryRoutePrefix
            + " -- this assertion would pass vacuously");

        // Act
        var anonymous = readSurfaces
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null
                        || e.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .Select(Describe)
            .ToList();

        // Assert
        anonymous.ShouldBeEmpty(
            $"Compass application read surfaces reachable without authentication: {string.Join(", ", anonymous)}. "
            + "AC-NFR-2: there is no anonymous access to any Compass area.");
    }

    /// <summary>Whether a route belongs to the published Directory boundary rather than the SPA's surface.</summary>
    private static bool IsPublishedBoundary(RouteEndpoint endpoint) =>
        NormalisedPath(endpoint).StartsWith(BoundaryRoutePrefix, StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void NoCompassSurface_IsGatedByATimesheetOrOotoPolicy()
    {
        // Arrange -- the direction that fails OPEN. A Compass route gated by, say, the timesheet Admin
        // policy would be "authorized" and would hand Compass to a timesheet root, which is the owner
        // decision this module exists to keep.
        var compass = CompassEndpoints();
        compass.ShouldNotBeEmpty();

        // Act
        var foreign = compass
            .SelectMany(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Where(a => a.Policy is { } p && !CompassPolicies.Contains(p, StringComparer.Ordinal))
                .Select(a => $"{Describe(e)} -> {a.Policy}"))
            .ToList();

        // Assert
        foreign.ShouldBeEmpty(
            $"Compass surfaces gated by a non-Compass policy: {string.Join(", ", foreign)}");
    }

    // ---------------------------------------------------------------- US2 cross-cutting (T072, T074)

    /// <summary>
    /// Hand-maintained mapping from each published boundary route (identifier constraints stripped,
    /// see <see cref="NormalisedBoundaryKey"/>) to the name of the denial <c>[Fact]</c> in
    /// <see cref="CompassBoundaryAuthorizationTests"/> that proves an authenticated caller with no
    /// Compass authority is refused it — T072.
    /// </summary>
    /// <remarks>
    /// A route added later with no entry here — or whose entry names a method that does not exist, or
    /// exists but is not a <see cref="FactAttribute"/> — fails
    /// <see cref="EveryPublishedBoundaryRoute_HasAProvenDenialTestInCompassBoundaryAuthorizationTests"/>.
    /// Deliberately hand-written rather than derived from the endpoint graph or from
    /// <see cref="CompassBoundaryAuthorizationTests"/>'s own method list — see
    /// <see cref="ExpectedCompassEndpointCount"/>'s remarks above for why a ratchet like this earns
    /// more by being annoying to update than by being computed. A computed version (e.g. "does some
    /// method name contain a fragment of the route") would auto-satisfy itself for a route added
    /// without ever writing a real denial assertion.
    /// </remarks>
    private static readonly Dictionary<string, string> ExpectedDenialTestByRoute = new(StringComparer.Ordinal)
    {
        ["/api/compass/v1/employees/{id}"] =
            nameof(CompassBoundaryAuthorizationTests.Get_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
        ["/api/compass/v1/employees/{id}/assignments"] =
            nameof(CompassBoundaryAuthorizationTests.GetAssignmentsByEmployee_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
        ["/api/compass/v1/clients/{id}"] =
            nameof(CompassBoundaryAuthorizationTests.GetClient_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
        ["/api/compass/v1/clients/{id}/billable-categories"] =
            nameof(CompassBoundaryAuthorizationTests.GetBillableCategories_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
        ["/api/compass/v1/clients/{id}/assignments"] =
            nameof(CompassBoundaryAuthorizationTests.GetAssignmentsByClient_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
        ["/api/compass/v1/assignments/{id}"] =
            nameof(CompassBoundaryAuthorizationTests.GetAssignmentById_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
        ["/api/compass/v1/assignments/{id}/sows"] =
            nameof(CompassBoundaryAuthorizationTests.GetSowsByAssignment_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
        ["/api/compass/v1/invoice-frequencies"] =
            nameof(CompassBoundaryAuthorizationTests.GetInvoiceFrequencies_AsAnAuthenticatedCallerWithNoCompassAuthority_Returns403),
    };

    /// <summary>
    /// The eight-route Compass Directory boundary spec 009 publishes -- <see cref="IsPublishedBoundary"/>
    /// narrowed further to exclude <c>/api/compass/v1/admin/*</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="BoundaryRoutePrefix"/> is a plain string prefix, and <c>/api/compass/v1/admin/*</c>
    /// (<see cref="LeadingEDJE.Leap.Api.Modules.Compass.Endpoints.Admin.CompassAdminRouteGroup"/> --
    /// the EDJEr/client/lookup/SOW CONFIGURATION surface features 004/006/010 added) happens to share
    /// that same prefix, so <c>IsPublishedBoundary</c> alone matches 28 routes, not 8 -- confirmed by
    /// running <see cref="EveryPublishedBoundaryRoute_HasAProvenDenialTestInCompassBoundaryAuthorizationTests"/>
    /// against the unnarrowed filter first, which is exactly the "discovered a different number than
    /// expected" failure its non-vacuity guard exists to catch. T072/T074 are specifically about the
    /// eight read-only routes spec 009's Phase 4 header names, not the admin write surface (which
    /// already has its own coverage: <see cref="EveryCompassWriteSurface_CarriesAServerSideAuthorityCheck"/>
    /// and <see cref="ExpectedCompassEndpointCount"/> above), so this file needs a strictly narrower
    /// predicate for those two tests specifically -- the other assertions in this file that use
    /// <see cref="IsPublishedBoundary"/> directly (<see cref="EveryPublishedBoundarySurface_ReadIncluded_CarriesAServerSideAuthorityCheck"/>)
    /// are unaffected by the wider match because they only assert non-emptiness, not a count.
    /// </remarks>
    /// <remarks>
    /// The admin segment itself (<c>"/admin/"</c>) is <see cref="PrivateHandlerXmlDocOperationTransformer.AdminSurfaceSegment"/>,
    /// not a second hand-typed literal here -- that transformer draws the identical boundary to decide
    /// which operations may receive a summary/description, and two independent literals for the same
    /// boundary can drift apart silently if the admin route segment is ever renamed. Found in
    /// adversarial code review of spec 009 Phase 5.
    /// </remarks>
    private static bool IsDirectoryBoundaryReadRoute(RouteEndpoint endpoint) =>
        IsPublishedBoundary(endpoint)
        && !NormalisedPath(endpoint).StartsWith(
            BoundaryRoutePrefix + PrivateHandlerXmlDocOperationTransformer.AdminSurfaceSegment,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The route's normalised path with any <c>{name:constraint}</c> narrowed to <c>{name}</c> and any trailing slash trimmed.</summary>
    /// <remarks>
    /// Route-constraint syntax (<c>{id:int}</c>) and ASP.NET's group+route concatenation (which can
    /// leave a trailing <c>/</c> on a route mapped as <c>MapGet("/", …)</c> under a group prefix) are
    /// both stable artifacts of how routes compose, not business logic — normalising them out keeps
    /// <see cref="ExpectedDenialTestByRoute"/> written the way a person reads a route rather than the
    /// way ASP.NET renders one.
    /// </remarks>
    private static string NormalisedBoundaryKey(RouteEndpoint endpoint)
    {
        var path = NormalisedPath(endpoint).TrimEnd('/');
        return Regex.Replace(path, ":[a-zA-Z]+\\}", "}");
    }

    [Fact]
    public void EveryPublishedBoundaryRoute_HasAProvenDenialTestInCompassBoundaryAuthorizationTests()
    {
        // Arrange -- non-vacuity guard (T072's own requirement): the handler scan must find the eight
        // routes, or a namespace typo/moved prefix would make this pass while inspecting nothing.
        var boundary = CompassEndpoints().Where(IsDirectoryBoundaryReadRoute).ToList();
        boundary.Count.ShouldBe(
            8, "discovered a different number of published boundary routes than expected -- either "
            + "the boundary grew (add an entry to ExpectedDenialTestByRoute) or the discovery itself "
            + "is broken (do not trust the rest of this test until this passes)");

        var denialTestType = typeof(CompassBoundaryAuthorizationTests);

        // Act
        var problems = new List<string>();
        foreach (var endpoint in boundary)
        {
            var key = NormalisedBoundaryKey(endpoint);
            if (!ExpectedDenialTestByRoute.TryGetValue(key, out var methodName))
            {
                problems.Add($"{Describe(endpoint)} (key '{key}') has no entry in {nameof(ExpectedDenialTestByRoute)}");
                continue;
            }

            var method = denialTestType.GetMethod(
                methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method is null)
            {
                problems.Add($"{Describe(endpoint)} names a denial test method that does not exist: {methodName}");
                continue;
            }

            if (method.GetCustomAttribute<FactAttribute>() is null)
            {
                problems.Add($"{Describe(endpoint)}'s denial test {methodName} exists but is not a [Fact]");
            }
        }

        // Assert
        problems.ShouldBeEmpty(
            $"boundary routes without a proven denial test: {string.Join("; ", problems)}");
    }

    /// <summary>
    /// T074 (FR-017's structural requirement). Each of the eight published routes must carry EXACTLY
    /// one Compass authorization requirement -- its own route group's <c>RequireAuthorization</c>, not
    /// zero and not stacked from a second, boundary-wide gate.
    /// </summary>
    /// <remarks>
    /// What this test can and cannot prove. Reflection over the composed
    /// <see cref="EndpointDataSource"/> sees only the METADATA that results from mapping a route, not
    /// which C# statement produced it -- it cannot distinguish "one <c>RequireAuthorization</c> call on
    /// this route's own <c>MapGroup</c>" from some hypothetical alternate wiring that happened to
    /// produce the identical single <see cref="IAuthorizeData"/> entry. What it DOES prove is that no
    /// route is ungated (zero entries) and no route is gated by more than one Compass policy (which
    /// would suggest a second, overlapping gate). The complementary claim -- that the four route groups
    /// are FOUR SEPARATE <c>MapGroup</c> call sites in four separate files, each with its own
    /// <c>.RequireAuthorization</c> chained directly onto it -- is not something reflection can see at
    /// all.
    /// </remarks>
    [Fact]
    public void EveryPublishedBoundaryRoute_CarriesExactlyOneCompassAuthorizationRequirement()
    {
        // Arrange
        var boundary = CompassEndpoints().Where(IsDirectoryBoundaryReadRoute).ToList();
        boundary.ShouldNotBeEmpty(
            "no published-boundary surfaces discovered -- this assertion would pass vacuously");
        boundary.Count.ShouldBe(8, "non-vacuity guard, matching the other boundary-wide assertions above");

        // Act
        var wrong = boundary
            .Where(e =>
            {
                var authData = e.Metadata.GetOrderedMetadata<IAuthorizeData>().ToList();
                return authData.Count != 1
                    || authData[0].Policy != RolePolicy.CompassAdmin;
            })
            .Select(Describe)
            .ToList();

        // Assert
        wrong.ShouldBeEmpty(
            "boundary routes not carrying EXACTLY one CompassAdmin authorization requirement: "
            + string.Join(", ", wrong));
    }
}
