---
title: How We Test the Shipment Service
date: 2023-05-22 11:00:00
tags:
  - testing
  - postgres
categories: Engineering
---

New engineers on the shipment service usually ask the same question in their first week: "why are there almost no mocks in this test suite?" Here's the reasoning, so it stops being a mystery.

## The old approach

For years, shipment-service tests mocked the database layer entirely. Every repository had an interface, every interface had a fake in-memory implementation, and tests were fast. They were also almost useless — the fakes drifted from real Postgres behavior constantly. Foreign key violations, ordering guarantees, and case-sensitivity bugs all made it to production because the fake repository didn't enforce any of that.

## What we do now

Tests run against a real Postgres instance, spun up per test run with Testcontainers. Slower than mocks, but they catch the bugs that actually cost us incidents.

```csharp
public class ShipmentRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:15-alpine")
        .Build();

    public Task InitializeAsync() => _db.StartAsync();
    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task DuplicateTrackingNumber_IsRejected()
    {
        var repo = new ShipmentRepository(_db.GetConnectionString());
        await repo.InsertAsync(new Shipment { TrackingNumber = "1Z999" });

        await Assert.ThrowsAsync<DuplicateTrackingNumberException>(
            () => repo.InsertAsync(new Shipment { TrackingNumber = "1Z999" }));
    }
}
```

## The tradeoffs, honestly

- **Slower CI.** The full suite takes about six minutes now instead of ninety seconds. We parallelize across containers to keep it tolerable.
- **Fewer false negatives.** In the eighteen months since we switched, three bugs that would have shipped with the old mock-based suite were caught before merge — all three involved constraint behavior the fakes didn't model.
- **Harder to test failure injection.** Simulating a connection timeout against a real container is more work than flipping a mock to throw. We still keep a thin layer of mocks for that specific case.

We don't think mocking the database is always wrong. For services where the persistence layer is simple and stable, fakes are fine. Shipment has enough constraint logic, triggers, and concurrency behavior that faking it faithfully was more work than just using the real thing.

## How we keep it from getting slower over time

The obvious risk with real-database tests is that the suite creeps slower every quarter as the test count grows, until someone proposes going back to mocks out of frustration. We've kept this in check with two rules: every new test file gets its own container instance so tests within a file can run without cleaning up shared state between them, and any test that takes more than two seconds on its own gets flagged in code review as a candidate for either optimization or a move to a smaller, more targeted nightly suite instead of the per-commit one.

## Where this leaves new engineers

If you're new to the team and used to a mock-heavy codebase elsewhere, the main adjustment is trusting the test infrastructure to handle container lifecycle for you — you write a test against what looks like a normal repository class, and Testcontainers handles spinning up and tearing down Postgres behind the scenes. The first time this clicks for people is usually when they write a test expecting a duplicate-key exception, get it for free from the real unique constraint, and realize they didn't have to hand-write that assertion into a fake at all.
