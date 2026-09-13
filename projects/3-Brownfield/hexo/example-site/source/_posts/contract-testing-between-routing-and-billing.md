---
title: Contract Testing Between Routing and Billing
date: 2024-05-14 09:30:00
updated: 2024-05-21 17:05:00
tags:
  - testing
  - postgres
categories: Engineering
---

The routing service and the billing service integrate through a shared Postgres schema — routing writes shipment cost estimates, billing reads them to generate invoices. This worked fine until routing changed a column's rounding behavior and billing quietly started generating invoices off stale estimates for two days before anyone noticed.

## The gap

Neither service had tests that verified the other's expectations of the shared schema. Routing's tests checked that it wrote what it intended to write. Billing's tests checked that it correctly processed whatever was already in the table. Nothing checked that the two sets of assumptions matched.

## What we added

A small contract test suite that both services run in CI, checking the shared schema against a documented contract rather than against either service's internal model:

| Field | Type | Routing guarantee | Billing assumption |
|---|---|---|---|
| `estimated_cost_cents` | integer | rounded, never null | rounded, never null |
| `currency` | text (ISO 4217) | always populated | always populated |
| `estimate_source` | text enum | one of 3 known values | ignores unknown values |

The table itself isn't the interesting part — it's that writing it down at all surfaced two mismatches immediately: routing was allowed to write a null `estimate_source` in one code path, which billing's "ignores unknown values" logic hadn't been built to handle since it expected a value to ignore, not an absence.

## Where the tests live

The contract tests run against the same Testcontainers Postgres instance both services already use for their own suites, so there was no new infrastructure to stand up — just a new shared test project that both CI pipelines pull in as a dependency and run against their current schema migrations before merge.

This doesn't replace end-to-end tests; it catches a narrower and more common failure mode: two services that each pass their own tests while quietly disagreeing about what the data between them means.

## Why we didn't just add an API instead of a shared schema

The obvious longer-term fix here is to stop sharing a schema at all — have routing expose an API and let billing call it, rather than both services reading and writing the same tables directly. We agree that's the right end state, and it's tracked as a larger piece of work than this post covers. The contract tests were deliberately scoped as a near-term mitigation we could ship in a week, not a replacement for that larger architectural change. Shared-schema integration is a pattern we inherited from a period when both services were owned by the same small team and an API boundary felt like unnecessary ceremony; it stopped making sense once the teams split, and the contract test suite is buying us time and safety while the API work gets prioritized against everything else competing for the same engineering capacity.

## What we'd flag to another team considering this

Writing the contract down as an explicit table, rather than leaving it implicit in each service's own code, is the part that mattered most. The actual test assertions were almost secondary — the act of getting both teams to agree on a single written description of what the shared data means surfaced the null-handling mismatch before we'd written a single line of test code. If your teams share a database and haven't written down what each side assumes about it, that conversation alone is worth having before any test framework enters the picture.
