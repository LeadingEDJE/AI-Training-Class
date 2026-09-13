---
title: How We Run On-Call at Northwind
date: 2023-02-14 09:12:00
tags:
  - on-call
categories: Culture
---

Northwind Engineering runs a weekly on-call rotation across the Platform and Engineering teams. If you're joining one of those teams, here's the short version.

## The rotation

One primary and one secondary, swapping every Monday at 10am. Primary carries the pager. Secondary is a backstop if primary doesn't ack within 15 minutes.

## What you're on the hook for

- Order and shipment services (the two systems that page the most)
- The nightly batch jobs that reconcile warehouse inventory
- Anything PagerDuty routes to the `platform-oncall` schedule

## Handoff

Every Monday, primary writes a two-line handoff note in the team channel: anything still smoldering, anything to watch. Read it before you take the pager.

That's it for now — we'll post again if the rotation changes.
