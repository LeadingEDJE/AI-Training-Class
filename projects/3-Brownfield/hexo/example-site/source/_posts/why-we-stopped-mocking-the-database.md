---
title: Why We Stopped Mocking the Database (A Rant)
date: 2023-11-02 16:20:00
author: J. Alvarez
tags:
  - testing
categories: Engineering
---

I've now had this argument in three different team channels, so I'm writing it down once and pointing people here.

## The claim I keep hearing

"Mocking the database makes tests fast and isolated, so we should mock it everywhere." This is true for exactly one of those two reasons and false for the other. Mocking the database does make tests fast. It does not make them isolated from reality — it isolates them from your actual persistence layer, which is precisely the part most likely to contain your bugs.

## What a mock actually promises you

A mock of your repository interface tests one thing: that your code calls the interface the way you expect it to. It tells you nothing about whether Postgres will actually enforce the constraint you're relying on, whether your query is index-friendly, or whether two concurrent writes will interleave the way you assumed in your head.

I've watched a hand-rolled in-memory fake silently allow a duplicate insert that Postgres's unique constraint would have rejected. The test suite was green. Production was not.

## The counterargument, and why it's not enough

The usual counter is "that's what integration tests are for — keep unit tests mocked, and cover the database behavior separately." I don't disagree with that structure in principle. Where it breaks down is that "separately" quietly becomes "rarely," because integration tests against a real database are annoying to set up, so they get written once, get stale, and stop being trusted.

Testcontainers changed the cost-benefit here enough that I don't think the tradeoff holds anymore. A real Postgres instance, started per test run, adds a few seconds — not the ten minutes it used to take when "integration test" meant a shared staging database someone had to remember to reset.

## Where I'll still take the mock

External HTTP APIs we don't control. Anything involving wall-clock time. Anything where the real dependency is genuinely slow or flaky in ways unrelated to what you're testing. The database, for us, stopped qualifying for any of those exceptions once the tooling got good enough.

Come argue with me in `#eng-testing` if you disagree — I'd genuinely like to hear the case for the other side again, now that Testcontainers is table stakes.

## The objection I take most seriously

The strongest pushback I've gotten isn't "mocks are fine," it's "your team can afford this because you have the infrastructure and discipline to keep container-based tests fast, and not every team does." That's fair. If your CI environment can't run containers, or your test suite is already slow enough that adding a database dependency to every test would push it past what anyone tolerates, the calculus is genuinely different, and I wouldn't tell that team to rip out their mocks tomorrow. My claim is narrower than "always use a real database" — it's "don't assume mocking the database is free, and re-check that assumption once the tooling that used to make it expensive has gotten cheap."

## One more thing worth saying

None of this is really about mocks specifically. It's about how easily a tooling decision made for good reasons at the time — mocks were genuinely the right call when integration tests meant a shared staging database someone had to babysit — outlives the reasons and becomes an assumption nobody re-examines. That pattern shows up constantly in this codebase, not just in how we test. Worth watching for elsewhere.
