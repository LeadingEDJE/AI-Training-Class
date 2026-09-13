---
title: 'A Year of Flaky Tests: What Actually Fixed Them'
date: 2026-08-25 10:00:00
tags:
  - postgres
  - on-call
categories: Engineering
---

Fourteen months ago, our CI pipeline had a flaky-test rate high enough that engineers had a standing habit of re-running failed builds twice before looking at the failure. That habit is gone now. This is what it actually took to get there, and it wasn't the thing we expected going in.

<!-- more -->

## What we thought the problem was

Going in, the working theory was "our tests are badly written." Some certainly were. But a first pass of rewriting the noisiest test files barely moved the needle — the flaky-test rate dropped from about 6% of CI runs to about 5%. That told us the real cause was somewhere else.

## What it actually was

Two root causes accounted for the overwhelming majority of flakes, and neither was really about test code quality:

### 1. Shared Postgres state across parallel test workers

Our CI ran tests in parallel across eight workers, all pointed at the same Postgres instance with different schemas. Tests that touched sequences, advisory locks, or anything using `pg_notify` weren't actually isolated by schema — those are cluster-wide, not per-schema. A test in worker 3 could observe a notification sent by a test in worker 6.

The fix was to give each worker its own Postgres instance via Testcontainers rather than sharing one cluster with separate schemas. This increased CI resource usage measurably, but it eliminated an entire category of failure that no amount of better test-writing would have caught, because the tests were individually correct — they just weren't actually isolated from each other.

```yaml
# before: one shared postgres service, per-schema isolation
services:
  postgres:
    image: postgres:15
# after: each parallel worker gets its own container
strategy:
  matrix:
    worker: [1, 2, 3, 4, 5, 6, 7, 8]
```

### 2. On-call incident response tests racing against real time

A smaller but nastier source of flakes came from tests that simulated on-call alert timing — things like "an alert that isn't acknowledged within 15 minutes should escalate." These tests used real sleeps and real timers, which meant they were sensitive to CI runner load: a busy runner could delay a timer callback long enough to flip an assertion about ordering.

We replaced real time with an injectable clock abstraction across the alerting service's test suite, so "15 minutes pass" became a controlled, instant operation in tests instead of an actual wall-clock wait subject to scheduler jitter.

## The cost of not fixing this sooner

It's worth being honest about the cost of the eighteen months this took to actually address, since "our CI is flaky" is a complaint every team makes and not every team acts on. We estimate the habit of re-running failed builds cost each engineer roughly ten minutes a day in waiting and context-switching, which across the engineering org adds up to more calendar time than the fix itself took. The harder cost to quantify is trust: engineers who've been burned by flaky CI stop trusting red builds at all, which means real failures increasingly get re-run instead of investigated. That's the actual danger of tolerating flakiness — not the wasted time, but the erosion of a red build meaning anything.

## What didn't help as much as expected

Retrying flaky tests automatically in CI is a common recommendation, and we tried it as a stopgap early on. It made the pipeline green more often, which felt like progress, but it didn't reduce the actual defect count — it just hid it. We kept the retry mechanism for genuinely external flakiness (a third-party sandbox API with its own uptime issues) but stopped treating it as a fix for anything internal to our own tests.

## Numbers

| Period | Flaky-test rate (CI runs) | Median CI runtime |
|---|---|---|
| 14 months ago | ~6% | 11m |
| After rewriting noisy tests only | ~5% | 11m |
| After per-worker Postgres isolation | ~1.5% | 14m |
| After injectable clock in alerting tests | ~0.3% | 14m |

CI got three minutes slower in exchange for going from "re-run twice, ignore it" to "a failure almost always means something real." That's a trade we'd make again.

## How we found the two root causes

Neither root cause was obvious from looking at individual failed test runs — a flaky test, by definition, mostly passes, so any single failure looks like an anomaly. What actually worked was aggregating failure data across three months of CI runs and looking for correlation rather than reading failures one at a time. Two patterns fell out immediately once we did that: failures clustered heavily on specific test files that used `pg_notify` or advisory locks, and failures in the alerting service's test suite clustered on CI runners under the highest concurrent load, which we could see from our own runner utilization metrics.

That correlation step is the part we'd recommend to anyone chasing flakiness without a clear lead. Individually, a flaky test always looks like it might just be a badly written test. In aggregate, the shared infrastructure underneath a whole cluster of otherwise-unrelated flaky tests becomes visible in a way that no individual failure log will show you.

## The rollout, and what almost went wrong

Moving to per-worker Postgres containers wasn't a single change — we rolled it out service by service over about six weeks, starting with the alerting service since it had the worst flake rate. The first attempt at the shipment service's suite actually made things briefly worse: its tests assumed a warm connection pool with pre-existing data seeded once per CI run, and per-worker isolation meant that seed step now ran eight times instead of once, tripling the seed time and, for about a week, causing a new category of timeout-related flakes in CI while we tuned the seed script to run in parallel across workers instead of serially within each one.

We considered rolling that specific change back when it first made things worse, and the discussion about whether to revert is worth mentioning: the argument for reverting was that we'd introduced a new problem while trying to fix an old one, and the argument against was that the new problem was fixable in the seed script while the old problem — genuinely shared cluster state — wasn't fixable without the architectural change. We kept it in place and fixed the seed script, and in hindsight that was the right call, but it wasn't obvious in the moment that we were fixing a shallow problem on top of a deep improvement rather than trading one deep problem for another.

## What we'd tell a team starting this today

Don't start by rewriting test code. Start by pulling failure data across a long enough window to see clustering, because the fix that actually moves the needle is rarely in the test file that's failing — it's usually in whatever that test file quietly shares with a dozen others. And budget for the rollout itself to introduce short-term regressions; ours did, twice, and both times the instinct to revert would have been the wrong call.

## The actual lesson

The instinct to fix flakiness by improving individual test quality wasn't wrong, but it was aimed at a small fraction of the actual problem. The bigger wins came from questioning the shared infrastructure the tests ran against — a single Postgres cluster, real wall-clock time — rather than the test code itself. If your flaky-test rate isn't moving despite genuine effort on test quality, it's worth asking what's shared underneath the tests that shouldn't be.
