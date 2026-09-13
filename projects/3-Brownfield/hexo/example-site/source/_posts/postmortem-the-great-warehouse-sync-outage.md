---
title: 'Postmortem: The Great Warehouse Sync Outage'
date: 2023-06-30 08:45:00
tags:
  - postgres
  - on-call
  - kubernetes
categories: Platform
---

On June 27th, the warehouse-sync job failed to run for eleven hours, during which inventory counts drifted enough that the storefront oversold 214 items across three fulfillment centers. This is the internal writeup. Names of individuals are omitted; the goal here is the mechanism, not the blame.

## Timeline

- **02:14** — `warehouse-sync` CronJob fails with a connection pool exhaustion error. No page fires because the job's failure alert was configured to only fire after three consecutive failures.
- **02:14 – 09:00** — The job continues to fail silently every fifteen minutes. Inventory counts in the storefront database slowly diverge from the warehouse systems' actual counts.
- **09:02** — Third consecutive failure trips the alert. Primary on-call acknowledges within four minutes.
- **09:20** — On-call identifies connection pool exhaustion on the shared Postgres instance, caused by an unrelated reporting job that had started leaking connections the previous week after a deploy.
- **09:45** — Reporting job is scaled to zero as a mitigation. Warehouse-sync is manually triggered and succeeds.
- **10:30** — Inventory counts reconciled. Overselling incidents identified and handed to support and fulfillment for manual resolution.

## Root cause

The reporting job's connection leak had been present for six days before this incident. It didn't cause problems on its own — the shared Postgres instance had enough headroom to absorb slow leakage. It only became critical when combined with the warehouse-sync job's retry behavior, which opened new connections on every retry attempt without closing the failed ones cleanly.

```yaml
# warehouse-sync CronJob, before the fix
spec:
  schedule: "*/15 * * * *"
  concurrencyPolicy: Allow   # <- the actual bug
  jobTemplate:
    spec:
      backoffLimit: 3
```

`concurrencyPolicy: Allow` meant every fifteen-minute tick started a new pod even if the previous run's connections hadn't been released yet. Combined with the reporting job's leak, the pool filled up and stayed full.

## Impact in more detail

The 214 oversold items broke down unevenly across the three fulfillment centers involved. The Reno center accounted for 158 of them, almost entirely from a single high-velocity SKU that sells fast enough that even a few hours of stale counts is enough to cross zero. The other two centers accounted for the remainder, spread across dozens of lower-velocity SKUs.

Of the 214 oversold orders, 189 were resolved by substituting an equivalent item or offering the customer a partial refund with an apology credit. The remaining 25 required a full cancellation because no substitute existed. Support handled all of this manually over the following two days, since we don't have tooling that automatically detects and resolves an oversold order — it's a fully human-driven process today.

## Why detection came from support, not monitoring

This is worth dwelling on, because it's the least comfortable part of the incident. We had no automated check comparing the storefront's believed inventory count against the warehouse system's actual count. The only thing that would have caught the drift earlier than support tickets is a reconciliation job — which is, uncomfortably, exactly the job that failed. We had no independent, secondary way of noticing that our primary drift-detection mechanism had itself gone silent.

This is a common shape of failure: the same system serves as both the thing being monitored and the monitor. When it fails, there's often no signal at all rather than a degraded one, because the very job that would have produced the signal is the one that's down.

## Contributing factors, in order of how much they compounded the outage

1. **A shared Postgres instance for both reporting and warehouse-sync**, with no per-job connection quota. Either job alone would have been fine; the combination wasn't.
2. **`concurrencyPolicy: Allow`** on a job whose failure mode was itself connection exhaustion — the worst possible interaction, since every retry made the underlying problem slightly worse instead of backing off.
3. **A three-strikes alert threshold** that had outlived the reason it was introduced.
4. **No secondary reconciliation signal** independent of the job that failed, meaning the blast radius kept growing for as long as nobody happened to check manually.

None of these four factors alone would have produced an eleven-hour outage. Together, they did. That's the pattern we're trying to get better at recognizing before it becomes a postmortem instead of after.

## Why the alert didn't fire sooner

The three-consecutive-failures threshold was set deliberately, months earlier, to reduce noise from a flaky external API the job used to depend on. That API was replaced in a later refactor, but the alert threshold was never revisited. It was a reasonable decision at the time that quietly became a seven-hour blind spot.

## What we changed

1. `concurrencyPolicy` changed to `Forbid` on `warehouse-sync` — a new run cannot start while a previous run is still active or hasn't been cleaned up.
2. The reporting job's connection handling was fixed to use a bounded pool with an explicit close on every code path, including error paths.
3. Alert thresholds are now reviewed whenever the underlying job's dependencies change, not left as a one-time decision. This is tracked as a checklist item in the deploy template for scheduled jobs.
4. We added a dashboard panel showing active connections per job, broken down by namespace, so a slow leak is visible before it becomes an outage.

## What went right

On-call diagnosis, once the alert fired, took eighteen minutes from acknowledgment to root cause. The runbook for "warehouse-sync failed" was accurate and current, which is not something we can say about every runbook in this repo.

## Follow-up items

| Action | Owner | Status |
|---|---|---|
| Fix `concurrencyPolicy` on warehouse-sync | Platform | Done |
| Bounded connection pool in reporting job | Platform | Done |
| Alert threshold review checklist | Platform | Done |
| Per-job connection dashboard | Platform | Done |
| Automated overselling detection | Engineering | In progress |

The last item is the one that matters most long-term: we found out about the overselling from customer support tickets, not from our own monitoring. That's the real gap this incident exposed.

## Blameless review notes

We ran the review with the engineer who wrote the original `concurrencyPolicy: Allow` line in the room, specifically so the conversation would stay about the system rather than about them. Nobody remembered why `Allow` had been chosen over `Forbid` originally — the best guess, which nobody could confirm from history, was that an earlier version of the job was expected to sometimes run long and a subsequent tick was not supposed to block on it. If that was ever true, it stopped being true once the job started touching a shared, constrained resource like a connection pool.

That's the uncomfortable generalizable lesson: a configuration choice that was reasonable for the system as it existed at the time can become a landmine as the system around it changes, without anyone making a new decision or noticing the old one needed revisiting. We don't have a good systemic answer to this yet beyond "review scheduled-job configuration whenever its dependencies change," which is the same fix we already applied to alert thresholds. It's possible these two problems — stale alert thresholds and stale concurrency settings — are actually one problem wearing two costumes, and we're tracking that as a longer-term question for the platform team rather than closing it out here.

## For whoever's on-call the next time something like this happens

If you're staring at a job that's failed silently for hours and a shared database that's out of connections, check `pg_stat_activity` grouped by application name before you do anything else — that's what actually pointed us at the reporting job in this incident, faster than working backward from the warehouse-sync job's own logs did.

