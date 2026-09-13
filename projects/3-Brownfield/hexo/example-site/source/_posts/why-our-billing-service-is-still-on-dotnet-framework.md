---
title: Why Our Billing Service Is Still on .NET Framework
date: 2025-06-03 12:00:00
tags:
  - dotnet
categories: Engineering
---

Every few months someone new joins the billing team and asks why we haven't ported the invoicing engine to .NET 8 like the rest of our services. It's a fair question. Here's the honest answer, not the polished one.

## What it would take

The invoicing engine depends on a third-party tax-calculation library that only ships a .NET Framework build. The vendor has said a .NET Standard build is "on the roadmap" for three years running. We don't control that timeline, and we've stopped assuming it will change.

Working around it means either running the tax library in a separate Framework-hosted process and calling it over gRPC from a modernized core service, or replacing the vendor entirely. We scoped the gRPC-bridge approach last year:

- Estimated 6-8 weeks of engineering time.
- A new operational dependency: a Windows-hosted process our Linux-based Kubernetes cluster doesn't natively run, meaning a separate deployment target to maintain.
- Ongoing latency cost from the cross-process call on every invoice line item.

## Why we haven't done it

The invoicing engine works, is well-tested, and pages on-call less often than almost anything else we run. The business case for the migration is entirely "avoid future risk," not "fix a current problem." Every time we've prioritized it against work with a concrete customer-facing outcome, the concrete work has won.

That's a real tradeoff, not an oversight. We're carrying platform risk — .NET Framework is out of mainstream support, and the pool of engineers comfortable maintaining it shrinks every year — against the cost of a project with no immediate payoff.

## What would change our mind

Either the tax vendor ships a modern build, making this a much smaller project, or we hit a specific wall: a security patch we can't get, a hosting environment that stops supporting Framework workloads, or a hire who simply can't work in the old codebase effectively. None of those has happened yet. When one does, this becomes urgent instead of deferred, and we'll actually do the gRPC bridge or the vendor replacement, whichever is cheaper at that point.

Until then, the invoicing engine stays exactly as it is, and we keep re-answering this question every few months, which is a cheaper cost than the alternative.

## How we mitigate the risk in the meantime

We don't just leave this alone and hope. The invoicing engine's host is isolated onto its own set of Windows VMs, patched on a stricter schedule than the rest of our infrastructure specifically because it's no longer receiving mainstream vendor support. We keep an internal list of every engineer still comfortable working in the codebase and treat a drop below three people as a trigger to actively cross-train someone, rather than waiting for the current maintainers to leave and finding out the hard way. And every new engineer who touches billing gets a short onboarding session specifically on the .NET Framework parts of the system, so institutional knowledge doesn't quietly concentrate in one or two people.

## The question underneath the question

The real tension this post is dancing around isn't really about .NET Framework versus .NET 8 — it's about how a company decides to spend engineering time on paying down risk that hasn't materialized yet versus building things customers are asking for today. We don't have a formula for that tradeoff. What we do have is a habit of revisiting it out loud, in writing, often enough that the decision to defer stays a decision, not just an accumulating default.
