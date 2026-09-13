---
title: 'Kubernetes Resource Limits: A Postmortem in Slow Motion'
date: 2024-02-19 13:15:00
tags:
  - kubernetes
  - postgres
categories: Platform
---

Nobody paged us for this one. It took a quarterly cost review to notice that half our Postgres connection-pooler pods had been running with no memory limit set at all since the cluster migration eight months earlier.

## How it happened

When we migrated the connection poolers from EC2 to Kubernetes, the deployment manifest was copied from an internal template that set CPU requests but left memory limits commented out "to be tuned later." Later never came. Every pooler pod ran unbounded, and because none of them had actually hit a problematic memory ceiling, nothing alerted.

```yaml
resources:
  requests:
    cpu: 250m
    memory: 256Mi
  limits:
    cpu: "1"
    # memory: 1Gi   <- commented out at migration time, never restored
```

## What we found

Without a limit, the Kubernetes scheduler had no basis for bin-packing these pods efficiently, so it was placing them conservatively — worst case, as if they might need far more memory than they ever actually used. Actual usage per pooler pod hovered around 300–400Mi. The absence of a limit was costing us node capacity, not causing incidents, which is exactly why it went unnoticed for so long.

## The fix

We set `memory: 768Mi` as a limit, based on observed usage plus headroom, and rolled it out pooler-by-pooler over a week rather than all at once, watching for OOMKills after each change. None occurred.

## What we changed process-wise

- Resource limits are now a required field in our Kubernetes manifest linter — a manifest without both requests and limits fails CI.
- We audited every other deployment copied from that same internal template and found two more services missing memory limits, neither as long-lived as the pooler.
- Quarterly cost reviews now include a specific check for unbounded resources, not just aggregate spend.

## Why the scheduler's conservatism is invisible until you look for it

The frustrating part of this class of problem is that there's no error, no alert, and no user-facing symptom — the pods ran fine the entire time. The only trace of the issue was in aggregate node utilization numbers, which nobody was watching closely enough to notice a gradual, months-long drift in headroom. We've since started treating "unexplained gradual increase in node count relative to workload count" as its own kind of signal worth a dashboard, separate from the per-service dashboards that only show whether an individual service is healthy.

## Why we didn't just set a generous default limit everywhere

An easier fix than auditing individual services would have been requiring a generous default memory limit — say, 2Gi — on anything the linter didn't already have an explicit value for. We considered and rejected this. A default that's generous enough to never cause an accidental OOMKill is also generous enough to reintroduce the exact scheduling inefficiency we were trying to fix, just with a number attached instead of no number at all. The linter requiring an explicit, justified value forces whoever writes the manifest to actually look at expected usage, which a silent fallback default wouldn't have forced anyone to do.

## Takeaway

This wasn't a crisis. It was eight months of quietly wasted node capacity because a "TODO: tune later" comment never got revisited and nothing forced it to. The linter rule is the actual fix here — the specific missing memory limit was just this quarter's instance of a pattern that will keep recurring without an automated check.
