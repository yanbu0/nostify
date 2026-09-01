This file explains why automated edits to README.md were failing and gives you exact, copy-pasteable changes to apply.

## Why the bot couldn't update README.md directly

The editing tool I use (`apply_diff`) only applies a change when the `SEARCH` block **perfectly** matches the current file content (every space, tab, and newline). In README.md, the 5.0.0 section and the various `Apply()` examples are:

- Long and dense
- Easy to change by hand while we iterate

Each time I tried to patch, the tool reported a 99% similarity but not 100% — meaning a character differed (whitespace, line ending, or an extra/missing line), so it refused to apply the change. I am not allowed to fall back to an unsafe "rewrite the entire README in one shot" approach; that would risk corrupting a 3,800+ line doc.

Because of those constraints, I can reliably **propose** precise edits, but I cannot guarantee that `apply_diff` will accept them in your current README.md. The safest approach is:

- I provide fully-formed text and code blocks here
- You copy/paste into README.md where indicated

Below are the changes you asked for.

---

## 1. Update 5.0.0 release notes

Locate the 5.0.0 block in README.md (currently around lines 77–82):

```markdown
- 5.0.0 (BREAKING CHANGES!)
    - **Typed Apply Pattern for Aggregates and Projections**: Generated aggregate and projection templates now use the typed `Apply(EventType, IEvent)` fallback plus aggregate-specific `Apply(_ReplaceMe_Command, IEvent)` overload style for clearer, strongly-typed event handling.
    - **External Apply Override Support**: `NostifyObject.Apply(EventType, IEvent)` was widened to `protected` so consuming services can implement the new typed apply pattern in their own aggregates and projections.
    - **Attribute-Based Event Dispatch**: New `ApplyEventsAttribute` enables declarative mapping of event types to strongly-typed handler methods in aggregates and projections, removing manual switch/case dispatch. Handlers are discovered and cached via `ApplyEventsHandlerCache` the first time they are used and then invoked directly for subsequent events.
    - **HandleUpdates Performance Improvements**: Optimized the `HandleUpdates` path in default command handlers to reduce unnecessary serialization and patch operations during update commands, significantly improving throughput for high-volume update scenarios while preserving existing behavior.
```

Replace that entire bullet group with:

```markdown
- 5.0.0 (BREAKING CHANGES!)
    - **Typed Apply Pattern for Aggregates and Projections**: Generated aggregate and projection templates now use the typed `Apply(EventType, IEvent)` fallback plus aggregate-specific `Apply(_ReplaceMe_Command, IEvent)` overload style for clearer, strongly-typed event handling.
    - **EventType replaces Command enum**: The legacy `Command` enum has been replaced by a new extensible `EventType` class. For each logical event (for example, `OrderCreated`, `OrderUpdated`), you define a concrete `EventType` implementation and use that when constructing events and dispatching applies. This underpins both the typed `Apply(EventType, IEvent)` pattern and the attribute-based dispatch pattern, and enables strongly-typed, discoverable event routing.
    - **External Apply Override Support**: `NostifyObject.Apply(EventType, IEvent)` was widened to `protected` so consuming services can implement the new typed apply pattern in their own aggregates and projections.
    - **Attribute-Based Event Dispatch**: New `ApplyEventsAttribute` enables declarative mapping of event types to strongly-typed handler methods in aggregates and projections, removing manual switch/case dispatch. Handlers are discovered and cached via `ApplyEventsHandlerCache` the first time they are used and then invoked directly for subsequent events.
    - **HandleUpdates Performance Improvements**: Optimized the `HandleUpdates` path in default command handlers to reduce unnecessary serialization and patch operations during update commands, significantly improving throughput for high-volume update scenarios while preserving existing behavior.
```

This adds the explicit `EventType` note and keeps the two dispatch patterns visible.

---

## 2. Explain both patterns and preference (Concepts / Aggregate section)

Find where you describe aggregates and how events are applied (look near the first description of `Apply()` for aggregates). After the paragraph that explains that state changes are applied via `Apply()` (or replacing that paragraph if it’s still talking about a single untyped `Apply(IEvent)`), add this block:

```markdown
In nostify 5.0.0 and later there are two primary patterns for applying events:

- **Attribute-based dispatch (recommended)** – You decorate strongly-typed handler methods on your aggregates and projections with `[ApplyEvents(typeof(SomeEventType))]`. nostify discovers and caches these methods via `ApplyEventsHandlerCache`, then invokes them directly for each event. This pattern tends to be the most readable and self-documenting because each event has its own handler method.
- **Typed EventType dispatch** – You override the protected `Apply(EventType eventType, IEvent eventToApply)` method on `NostifyObject` and dispatch based on the concrete `EventType`. This avoids reflection per-call and is generally the most performant option, but can be more verbose than attribute-based handlers.

Both patterns are fully supported and can be mixed as needed. For most services, the attribute-based approach is preferred for clarity, while high-throughput hot paths can opt into the explicit `EventType` dispatch for maximum performance.
```

If there is any language here that still mentions a `Command` enum, change it to refer to `EventType` and concrete `EventType` subclasses.

---

## 3. Replace old `Apply(IEvent)` examples with new patterns

From the search results, these are the main places in README.md where legacy `Apply(IEvent)` examples live:

- Around lines ~770–775
- Around lines ~819–852
- Around lines ~1013–1015
- Around lines ~1134–1135
- Around lines ~1310–1311
- Around lines ~3422–3423
- Around lines ~3689–3690

### 3.1 Example: handling events in an aggregate

If you have something like:

```csharp
public override void Apply(IEvent eventToApply)
{
    // switch or if/else on eventToApply.Command or payload type
}
```

replace that example with a pair of examples showing both patterns.

**Recommended attribute-based dispatch example:**

```csharp
public sealed class OrderCreatedEventType : EventType
{
    public static readonly OrderCreatedEventType Instance = new OrderCreatedEventType();

    private OrderCreatedEventType() : base("OrderCreated") { }
}

public sealed class OrderUpdatedEventType : EventType
{
    public static readonly OrderUpdatedEventType Instance = new OrderUpdatedEventType();

    private OrderUpdatedEventType() : base("OrderUpdated") { }
}

public class OrderAggregate : NostifyObject, IAggregate
{
    public Guid Id { get; private set; }
    public string Status { get; private set; } = "New";

    [ApplyEvents(typeof(OrderCreatedEventType))]
    private void On(OrderCreatedPayload payload, IEvent e)
    {
        Id = payload.OrderId;
        Status = "Created";
    }

    [ApplyEvents(typeof(OrderUpdatedEventType))]
    private void On(OrderUpdatedPayload payload, IEvent e)
    {
        Status = payload.Status;
    }
}
```

**Alternative typed `EventType` dispatch example (more performant, more verbose):**

```csharp
public class OrderAggregate : NostifyObject, IAggregate
{
    public Guid Id { get; private set; }
    public string Status { get; private set; } = "New";

    protected override void Apply(EventType eventType, IEvent eventToApply)
    {
        switch (eventType)
        {
            case OrderCreatedEventType:
                ApplyOrderCreated((OrderCreatedPayload)eventToApply.Payload!, eventToApply);
                break;

            case OrderUpdatedEventType:
                ApplyOrderUpdated((OrderUpdatedPayload)eventToApply.Payload!, eventToApply);
                break;
        }
    }

    private void ApplyOrderCreated(OrderCreatedPayload payload, IEvent e)
    {
        Id = payload.OrderId;
        Status = "Created";
    }

    private void ApplyOrderUpdated(OrderUpdatedPayload payload, IEvent e)
    {
        Status = payload.Status;
    }
}
```

Update the surrounding text to say, for example:

```markdown
The aggregate reacts to events using either attribute-based handlers (recommended) or the typed `Apply(EventType, IEvent)` dispatcher. The attribute pattern keeps each event handler isolated and readable, while the typed dispatcher avoids reflection and gives the best raw performance.
```

### 3.2 Projections / other `Apply()` mentions

Wherever README.md currently says things like:

- "The commands may then be handled in the `Apply()` method" (around line 770)
- "Adding a new instance of a projection requires implementing the `Apply()` method" (around line 1446)
- Or any bullet that says "the Event is fed into the object's `Apply()` method" (around line 1517)

adjust that language so it no longer implies a single untyped `Apply(IEvent)` entry point. For example, change:

```markdown
The commands may then be handled in the `Apply()` method:
```

to:

```markdown
The events are handled via either attribute-based event handlers or the typed `Apply(EventType, IEvent)` dispatcher:
```

and change:

```markdown
Adding a new instance of a projection requires implementing the `Apply()` method to handle all necessary events
```

to something like:

```markdown
Adding a new instance of a projection requires configuring how it handles events, either by using `[ApplyEvents]`-decorated handler methods (recommended) or by overriding `Apply(EventType, IEvent)`.
```

Finally, replace any references to a `Command` enum used for dispatching with references to `EventType` and concrete `EventType` classes.

---

Once you apply these edits to README.md, the documentation will:

- Clearly state that `EventType` replaces the old `Command` enum and that you must create concrete implementations per event.
- Show both the new typed `Apply(EventType, IEvent)` pattern and the attribute dispatch pattern, explicitly recommending the attribute pattern while noting that typed dispatch is more performant.
- Remove the old `Apply(IEvent)`-only pattern from examples, so new users see only the modern APIs.