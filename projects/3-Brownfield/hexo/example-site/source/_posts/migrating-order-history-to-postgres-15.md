---
title: 'Migrating Order History to Postgres 15'
date: 2023-03-06 14:30:00
tags:
  - postgres
  - kubernetes
categories: Platform
---

The order-history database had been running Postgres 11 since before most of the current team joined. It was end-of-life, it was slow on our biggest customer's account, and every time someone proposed touching it the meeting ended with "let's do it next quarter." This quarter, we finally did it.

## Why we put this off for two years

Order history isn't glamorous. It's a single table with 340 million rows, written to constantly by the order service and read from constantly by support tooling, billing reconciliation, and the analytics warehouse sync. Nobody wanted to be the one who broke it.

The actual blockers, once we wrote them down, were smaller than the fear suggested:

- Postgres 11 support had already ended upstream; we were on our own for security patches.
- Our largest customer's query patterns had gotten bad enough that we were adding indexes defensively instead of understanding why.
- The database ran on a hand-rolled EC2 instance with a cron-based backup script nobody remembered writing.

## The plan

We moved the workload onto a managed Postgres 15 cluster running in Kubernetes, using the same operator we already run for two smaller services. Three phases:

1. Stand up the new cluster and replicate from the old primary using logical replication.
2. Cut over reads first, in stages, behind a feature flag per consumer.
3. Cut over writes in a single maintenance window once every reader was confirmed healthy on the new cluster for at least a week.

### Logical replication setup

Getting logical replication working across major versions took longer than expected. The publication side was simple:

```sql
CREATE PUBLICATION order_history_pub FOR TABLE orders, order_line_items;
```

The subscription side needed `wal_level = logical` and a bump to `max_replication_slots` on the old primary, which meant a restart we had to schedule around the nightly batch window. We also had to backfill the initial snapshot separately for the two largest tables — `pg_dump` with `--jobs` sped that up considerably once we split it by primary key range instead of running it as one long-running job.

## What broke

Two things, neither of which we'd predicted:

- **Sequence drift.** Logical replication doesn't replicate sequence state by default. We had to manually align `orders_id_seq` after cutover or new inserts would have collided with replicated rows. We caught this in staging, not production, but only because someone happened to insert a test row at the wrong moment.
- **Connection pooler config.** Our PgBouncer config assumed a single fixed host. Cutting reads over gradually meant juggling two pool configs side by side for about a week, which was more operational overhead than the migration plan accounted for.

## Results

| Metric | Postgres 11 (old) | Postgres 15 (new) |
|---|---|---|
| p50 query latency (order lookup) | 38ms | 11ms |
| p99 query latency (order lookup) | 640ms | 95ms |
| Nightly vacuum duration | 3h 40m | 22m |
| Backup mechanism | cron script, EC2 | operator-managed, automated |

The latency improvement is mostly newer hardware and a fresh set of indexes we couldn't add safely to the old cluster without downtime. The vacuum improvement is almost entirely from finally tuning `autovacuum_vacuum_cost_limit`, which nobody had touched since the table was small.

## Choosing Kubernetes over another managed EC2 instance

We considered three options before settling on the Kubernetes-hosted operator: staying on hand-managed EC2 with better tooling around it, moving to a fully managed cloud database service, and running Postgres inside Kubernetes with an operator. We ruled out the managed cloud service mainly on cost — at our data volume, the pricing model would have roughly tripled our monthly database spend compared to running it ourselves, and we didn't get enough operational simplicity in return to justify that, especially since we already run two other stateful workloads on the same operator.

Hand-managed EC2 with better tooling was the closer call. It would have been less migration work in the short term. We ruled it out because the actual failure mode of the old setup wasn't "EC2 is bad," it was "nobody owns this specific box and its specific cron jobs," and moving to a slightly better-documented version of the same pattern wouldn't have fixed that. The operator gives us declarative backups, automated failover, and a consistent upgrade path that doesn't depend on institutional memory of one server's quirks.

## The cutover window, in more detail

The write cutover was scheduled for a Saturday morning, historically our lowest-traffic window. The runbook had four rollback checkpoints, each with an explicit go/no-go decision:

1. **Before stopping writes to the old primary** — verify replication lag is under 5 seconds and has been for 10 minutes straight.
2. **After stopping writes, before flipping the connection string** — verify the new cluster's row count matches the old primary's row count exactly, not approximately.
3. **After flipping the connection string, before removing the old primary from the fallback path** — run the full smoke-test suite against the new primary in production.
4. **24 hours after cutover** — confirm no consumer has reported an issue and vacuum/autovacuum behavior looks normal under real write load, not just synthetic load from the smoke tests.

We didn't need to roll back at any checkpoint, but having them written down in advance, with specific numeric thresholds rather than "if it looks okay," made the on-call engineer running the cutover comfortable moving forward at each stage without escalating for a judgment call.

## Cost impact

The new cluster's raw infrastructure cost is about 15% higher than the old EC2 instance, mostly from running a standby replica we didn't have before. We consider that a feature, not a regression — the old setup had no tested failover path at all. Engineering time saved on backup babysitting and ad-hoc "is this box still healthy" checks more than offsets the difference, though we don't have a clean dollar figure for that side of the ledger.

## Communicating the cutover to the rest of the company

We underestimated how much internal communication a database migration needs, even one with zero planned downtime. Support, fulfillment, and the data team all query order history in ways the platform team doesn't have full visibility into, and "zero downtime" for us didn't automatically mean "zero visible change" for them — the brief period of dual-pool operation during staged reads introduced a small, real increase in tail latency for one internal reporting dashboard that nobody had flagged as a stakeholder beforehand. We now keep a checklist of every known internal consumer of a database before starting a migration like this, not just the services we own directly.

## What we'd do differently

Start the sequence-drift investigation earlier — it should have been the first thing we tested, not something we found by accident. Also: two years of "let's do it next quarter" cost us real toil in the form of defensive indexing and a backup process nobody trusted. The migration itself took three weeks. The decision to start it took two years.

## Documentation debt we found along the way

Part of what made the two-year delay possible was that nobody had written down how the old instance was actually configured — its backup cadence, its cron schedule, its autovacuum settings. Rebuilding that picture from scratch, largely by reading shell history and old deploy scripts on the box itself, took almost as long as planning the actual migration. We now keep a one-page runbook per stateful service listing exactly this kind of operational detail, updated as part of the on-call handoff process rather than as a separate task nobody prioritizes.

If you're staring down a similar migration, happy to talk through the replication setup — find the platform team in `#platform-eng`.
