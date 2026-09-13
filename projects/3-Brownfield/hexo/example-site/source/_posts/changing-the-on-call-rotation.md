---
title: We're Changing the On-Call Rotation
date: 2023-09-11 10:00:00
tags:
  - on-call
categories: Culture
---

Starting next Monday, the on-call rotation is changing from weekly to two-week shifts, and we're splitting the combined Platform/Engineering schedule into two separate schedules.

## Why

The weekly rotation meant that on a team of eight, you were on-call roughly every seven weeks — often enough that context never really built up between shifts, but frequent enough to be disruptive. Feedback from the last engagement survey was consistent: people wanted longer, less frequent shifts rather than short, frequent ones.

The combined schedule was also a problem. Platform pages (infrastructure, Kubernetes, databases) and Engineering pages (application bugs, customer-facing errors) require different context, and we kept seeing the wrong person get paged for the wrong kind of problem.

## What's changing

- Rotation length: 1 week → 2 weeks.
- Schedules: one combined schedule → two schedules, `platform-oncall` and `engineering-oncall`.
- Handoff: still every Monday, now with a slightly longer handoff note since there's more to summarize after two weeks.

## What's staying the same

The secondary/backstop structure, the 15-minute ack window, and the Monday 10am swap time are all unchanged.

## How we decided on two weeks instead of, say, three or four

We looked at what a few other teams our size do before settling on two weeks. Four-week rotations came up as an option and got rejected quickly — that's long enough that a rough week can genuinely affect someone's morale for a meaningful chunk of the quarter, and long enough that the person who was on-call three shifts ago has usually forgotten enough context to make handoff notes less useful. One week, the status quo, packed too much rotation frequency into too little breathing room between shifts, as described above. Two weeks was the balance point where feedback converged: long enough to actually build context on an ongoing issue across a shift, short enough that a difficult stretch doesn't dominate a month.

## Compensation

On-call pay is unchanged: a flat stipend per shift, plus overtime for any incident response outside business hours, per the existing policy. Two-week shifts mean the stipend is now paid every two weeks per person rather than every week, at the same weekly rate.

## When this takes effect

The split schedules go live with next Monday's handoff. If you're currently on the combined rotation, check PagerDuty before then — you've already been assigned to whichever of the two new schedules matches your team, and the assignment is visible now even though the old combined schedule is still the one that's active until the swap.

Questions go to your manager or `#platform-eng`.
