---
title: Alert Fatigue and What We Did About It
date: 2024-08-08 15:00:00
tags:
  - on-call
  - kubernetes
categories: Platform
---

At the start of this year, the platform on-call schedule was firing an average of 34 pages per week. By the time we finished the cleanup described here, that number was down to 9, and — more importantly — the pages that remained were ones people actually acted on.

## The problem wasn't volume, it was signal

Thirty-four pages a week sounds bad on its own, but the real issue was that most of them didn't require action. A pod restarting once during a rolling deploy would page. A single failed health check that resolved on the next probe would page. On-call engineers learned, correctly, that most pages could be acknowledged and ignored, which meant the ones that mattered got the same treatment.

## What we audited

We pulled every alert rule firing against the platform on-call schedule and classified each one:

- Did it fire in the last 90 days?
- When it fired, did the on-call engineer take any action beyond acknowledging it?
- Was the underlying condition something Kubernetes already self-heals (a single pod restart, a rescheduled pod after node drain)?

About 60% of our alert volume came from conditions Kubernetes was already designed to recover from on its own. A pod crash-looping twice isn't the same signal as a pod crash-looping for twenty minutes straight, but our alert rules didn't distinguish between them.

## The changes

1. Alerts on pod restarts now require a sustained condition — three restarts within ten minutes, not one restart, before paging.
2. Health check alerts require two consecutive failures, not one, matching the readiness probe's own failure threshold instead of firing more aggressively than the platform itself does.
3. We split alerts into paging and non-paging tiers. Conditions worth knowing about but not worth waking someone up for now post to a dashboard channel instead of the pager.
4. Anything that fired zero times of consequence in 90 days was either deleted or downgraded to non-paging.

## Result

The pages that remain are ones with a clear action attached — this is a meaningfully different on-call experience than a rotation that pages constantly but rarely means anything. We're tracking "pages requiring action" as an ongoing metric now, not just raw volume, since volume alone can hide whether the signal is any good.

## The risk we accepted deliberately

Raising thresholds always trades detection speed for noise reduction, and we want to be upfront that we accepted slower detection in exchange for a pager people actually trust. A pod that's genuinely crash-looping now takes up to ten minutes to page instead of firing on the first restart. In every incident we reviewed from the past year, ten minutes of additional delay wouldn't have changed the outcome — none of our real incidents were resolved inside that window anyway — but we're watching for a case where it does matter, and we'll adjust the specific threshold rather than reverting the whole approach if that happens.

## Keeping this from drifting back

The hardest part of an alert-quality effort like this isn't the initial cleanup, it's not sliding back to where we started. New alerts get added by different engineers for different reasons, and each one seems reasonable in isolation. We now require a one-line justification and an explicit paging-tier decision on any new alert rule added to the platform on-call schedule, reviewed the same way a code change is reviewed, rather than letting alert configuration accumulate as an afterthought the way it did the first time around.
