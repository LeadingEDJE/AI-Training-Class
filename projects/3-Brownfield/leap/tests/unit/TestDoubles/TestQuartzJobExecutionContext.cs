#pragma warning disable CS1591 // Phase 39 D-02: intentionally undocumented — test double.

using Quartz;

namespace LeadingEDJE.Leap.Api.Tests.TestDoubles;

/// <summary>Minimal IJobExecutionContext for testing trigger name detection, shared across job tests.</summary>
public sealed class TestJobExecutionContext(string triggerName) : IJobExecutionContext
{
    public IScheduler Scheduler => null!;
    public ITrigger Trigger { get; } = new TestTrigger(triggerName);
    public ICalendar? Calendar => null;
    public bool Recovering => false;
    public TriggerKey RecoveringTriggerKey => null!;
    public int RefireCount => 0;
    public JobDataMap MergedJobDataMap => new();
    public IJobDetail JobDetail => null!;
    public IJob JobInstance => null!;
    public DateTimeOffset FireTimeUtc { get; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFireTimeUtc => DateTimeOffset.UtcNow;
    public DateTimeOffset? PreviousFireTimeUtc => null;
    public DateTimeOffset? NextFireTimeUtc => null;
    public string FireInstanceId => "test";
    public object? Result { get; set; }
    public TimeSpan JobRunTime => TimeSpan.Zero;
    public CancellationToken CancellationToken => CancellationToken.None;
    public void Put(string key, object objectValue) { }
    public object? Get(string key) => null;
    public void Put(object key, object objectValue) { }
    public object? Get(object key) => null;
}

public sealed class TestTrigger(string name) : ITrigger
{
    public TriggerKey Key { get; } = new TriggerKey(name);
    public JobKey JobKey => new("TestJob");
    public string Description => "";
    public string? CalendarName => null;
    public JobDataMap JobDataMap => new();
    public DateTimeOffset? FinalFireTimeUtc => null;
    public int MisfireInstruction => 0;
    public DateTimeOffset? EndTimeUtc => null;
    public DateTimeOffset StartTimeUtc => DateTimeOffset.UtcNow;
    public int Priority { get; set; } = 5;
    public bool HasMillisecondPrecision => false;

    public ITrigger Clone() => this;
    public int CompareTo(ITrigger? other) => 0;
    public DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime) => null;
    public DateTimeOffset? GetNextFireTimeUtc() => null;
    public DateTimeOffset? GetPreviousFireTimeUtc() => null;
    public bool GetMayFireAgain() => false;
    public IScheduleBuilder GetScheduleBuilder() => null!;
    public TriggerBuilder GetTriggerBuilder() => TriggerBuilder.Create();
}
