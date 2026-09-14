<?php

/**
 * Mock Google SAML app — user roster (Phase 47, D-01/D-06/D-07/D-08).
 *
 * This is the ONE PHP file in the phase: a declarative user list mounted read-only into
 * the `google-saml-mock` container (kenchan0130/simplesamlphp:1.19.9). It impersonates the
 * Google Workspace SAML app the deployed config points at, so the real Sustainsys.Saml2
 * handshake is exercised end-to-end in a browser locally + in CI. No PHP toolchain anywhere.
 *
 * Attribute shape (RESEARCH Pattern 3 — the chosen NameID discretion call):
 *   Each user carries TWO attributes:
 *     1. An attribute literally named with the full ClaimTypes.Email URI
 *        (http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress). Sustainsys maps
 *        SAML attribute Name -> claim type VERBATIM, so this lands as the ClaimTypes.Email claim
 *        that HandleLoginCallback reads FIRST (the transient NameID is never consulted). This
 *        avoids mounting a second config file (saml20-idp-hosted.php) for a NameID authproc.
 *     2. A `groups` array — the default Saml2:GroupsAttributeName the callback reads. Group
 *        strings BYTE-MATCH appsettings.Development.json GoogleAuth:Groups[].GroupName —
 *        space-free (`Compass-Admin-dev`), verified from a live Google SAML assertion; copy
 *        byte-exactly, never "normalize".
 *
 * Roster rationale (trimmed after the Timesheet and OOTO modules were deleted — this file used to
 * carry one user per Timesheet/OOTO role too; those are gone along with the UI that needed them):
 *   D-06: one user per surviving Compass group + `edjer` (no groups, the baseline case).
 *   D-07: emails reuse AbsorbedDirectorySeeder identities. Only 5 active seeded people have both
 *         an email and an EdjeId (ivy.control, blair.dev, casey.dev, alex.coach, hank.deliver —
 *         all @example.test), so manual-use users SHARE emails; roles come from the ASSERTED
 *         groups, not the person row.
 *   D-08 negatives: `outsider` = wrong domain (fails GoogleAuth:AllowedDomain=example.test),
 *         `deactivated` = person row exists but IsActive=false (denied `Inactive Person Record`),
 *         `unmapped` = groups match no mapping (lands as baseline EDJEr only).
 *         `ghost` = valid domain, NO person row: since quick task 260729-sqc this is a POSITIVE
 *         case (auto-create), not a denial — see the per-user comments below. Each carries a
 *         Compass-SuperAdmin-dev group so the privileged-vs-denied contrast still holds.
 *
 * SECURITY: passwords are the throwaway literal `password`; emails are already-committed
 * synthetic seed data (@example.test). NEVER copy real employee emails into this roster. This
 * container's signing key is PUBLIC — its metadata is radioactive outside localhost/CI.
 */

$config = array(

    'admin' => array(
        'core:AdminPassword',
    ),

    'example-userpass' => array(
        'exampleauth:UserPass',

        // --- D-06: one user per surviving GoogleAuth:Groups group + a no-groups EDJEr ---

        // Baseline EDJEr — no groups, no privilege.
        'edjer:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'blair.dev@example.test',
            'groups' => array(),
        ),

        // --- One user per Compass group, keeping this file's stated invariant.
        // Space-free group strings byte-match appsettings.Development.json / live Google SAML
        // (Attribute Name="Groups"). Local mock uses the "-dev" groups.
        'compass-superadmin:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'casey.dev@example.test',
            'groups' => array('Compass-SuperAdmin-dev'),
        ),
        'compass-admin:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'alex.coach@example.test',
            'groups' => array('Compass-Admin-dev'),
        ),
        'compass-ops:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'hank.deliver@example.test',
            'groups' => array('Compass-Ops-dev'),
        ),
        'compass-sales:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'casey.dev@example.test',
            'groups' => array('Compass-Sales-dev'),
        ),

        // #53's named regression, live: holds BOTH a bare production group and a
        // higher-privilege -dev group at once, exactly the
        // GoogleAuthServiceTests.MapGroupsToRoles_ProdAndDevGroupsBothPresentIn* shape (still
        // covered at tests/unit/Services/GoogleAuthServiceTests.cs). In Production this must
        // resolve to Compass Ops alone; in any non-Production environment to Compass Super Admin
        // alone.
        'compass-ops-prod-plus-dev-superadmin:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'hank.deliver@example.test',
            'groups' => array('Compass-Ops', 'Compass-SuperAdmin-dev'),
        ),

        // --- D-08: three negative-case users. Each carries a Compass-SuperAdmin-dev group so the
        // denial is provably about the case under test (domain / ghost / deactivated), not about
        // lacking any privileged group to begin with. ---

        // D-08a: wrong domain (fails GoogleAuth:AllowedDomain=example.test).
        'outsider:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'outsider@gmail.com',
            'groups' => array('Compass-SuperAdmin-dev'),
        ),
        // D-08b: valid domain but no person row. Since quick task 260729-sqc this is the
        // AUTO-CREATE path, not a denial: a domain-validated user with no person row gets one
        // minted at sign-in and proceeds. Kept as the E2E proof that the first-sign-in deadlock
        // is broken (the deployed `people` table starts empty).
        'ghost:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'ghost@example.test',
            'groups' => array('Compass-SuperAdmin-dev'),
        ),
        // D-08d: person row EXISTS but is deactivated -> still denied `Inactive Person Record`.
        // erin.ghost is AbsorbedDirectorySeeder's Person-INACTIVE/Employee-active persona. Carries
        // a privileged group ON PURPOSE: deactivation must beat any group-derived role. This is the
        // negative case that guards quick task 260729-sqc's D-06 (auto-create must NEVER resurrect
        // a deactivated person) at the real-handshake level.
        'deactivated:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'erin.ghost@example.test',
            'groups' => array('Compass-SuperAdmin-dev'),
        ),
        // D-08c: groups match no mapping — lands as baseline EDJEr only.
        'unmapped:password' => array(
            'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress' => 'hank.deliver@example.test',
            'groups' => array('Some-Unmapped-Group'),
        ),
    ),

);
