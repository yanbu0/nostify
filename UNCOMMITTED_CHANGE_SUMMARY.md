# Uncommitted Change Summary and Behavior Impact

**Prepared:** 2026-09-23

**Scope:** Current working tree relative to the checked-out commit

**Status:** Uncommitted; no changes are staged

## Executive summary

This change set hardens the Nostify library for release by enabling strict compiler and analyzer gates, correcting nullable contracts, removing obsolete handler APIs, improving validation and structured logging, making Cosmos-dependent flows deterministic to test, and substantially expanding automated test coverage.

The changes are not exclusively internal cleanup. Several items alter source compatibility or observable runtime behavior and must be considered during release review:

1. Obsolete non-async command-handler and event-handler wrappers have been removed. Consumers must call the corresponding async APIs.
2. Factory construction now fails early with clear exceptions when required Cosmos or Kafka configuration is missing.
3. The default partition-key path is now `/tenantId` when no path was configured.
4. Several APIs now accurately declare nullable return values or optional dependencies. This can produce new nullable warnings in consuming projects but more accurately reflects existing runtime outcomes.
5. Missing or invalid event payloads now generally produce `InvalidOperationException` with specific messages instead of incidental `NullReferenceException` failures.
6. Event and topic comparisons now use explicit ordinal semantics, removing current-culture dependence.
7. Request-handler logging now uses stable structured events and includes additional validation, completion, timing, and failure messages.
8. The Cosmos client now implements `IDisposable`, allowing owned Cosmos resources to be released deterministically.
9. Release builds now fail on warnings, recommended analyzer diagnostics, nullable diagnostics, code-style diagnostics, and missing or malformed public XML documentation.

All active tests pass in Release configuration. The current measured production coverage is 84.57% line coverage and 77.02% branch coverage. The repository collects coverage but does not currently enforce a numeric coverage threshold.

## Working-tree scope

At the time this document was prepared, the working tree contained:

- 80 modified tracked files.
- 14 untracked files, including this document after it is created.
- 3,952 tracked insertions and 1,646 tracked deletions before this document.
- No staged files.
- No tracked files currently reported as deleted.

The tracked changes span 50 production files, 29 test-project files, and the root library project file. New repository policy files and new behavioral test files are untracked and must be explicitly added before committing.

## Behavior-impact matrix

| Area | Previous behavior | New behavior | Compatibility and migration impact |
|---|---|---|---|
| Command-handler wrappers | Obsolete methods without the `Async` suffix remained callable and delegated to async implementations. | Obsolete patch, post, delete, bulk-create, bulk-update, and bulk-delete wrappers are removed. | **Source breaking.** Replace calls with the corresponding async methods and await the returned task. |
| Event-handler wrappers | Deprecated aggregate, projection, multi-apply, and bulk wrappers remained callable. | The deprecated wrapper region is removed; async methods remain. | **Source breaking.** Migrate to the corresponding async event-handler methods. |
| Factory validation | Incomplete configuration could flow into dependency constructors and fail later or less clearly. | Build validates required Cosmos key, database name, endpoint, and Kafka bootstrap configuration before constructing dependencies. | **Observable behavior change.** Invalid startup configuration now fails earlier with `ArgumentNullException` or `InvalidOperationException` and actionable messages. |
| Default partition path | An unset configured path could remain null through construction. | An unset path defaults to `/tenantId`. | **Observable behavior change.** Applications relying on a null path must configure an explicit path; normal tenant-partitioned applications receive a safe default. |
| Optional HTTP support | The HTTP client factory contract was non-null even though it could be absent. | The contract is nullable; HTTP-dependent operations explicitly fail when no factory is configured. | **Contract correction.** Consumers may see nullable warnings and should configure HTTP support before invoking HTTP-dependent projection behavior. |
| Event payload errors | Missing or unconvertible payloads could cause incidental `NullReferenceException` failures. | Missing and unconvertible payloads produce explicit `InvalidOperationException` failures with contextual messages. | **Exception contract change.** Error handling that catches `NullReferenceException` for malformed payloads must be updated. |
| Apply-and-persist results | Several overloads declared a non-null generic result despite not-found paths returning null. | Return types are annotated as nullable where no persisted object may be found. | **Source annotation change.** Runtime semantics are clarified; nullable-enabled consumers must handle null. |
| String comparison | Some event/topic comparisons depended on defaults, casing conversions, or current culture. | Event names use ordinal comparison; topic deduplication uses ordinal case-insensitive comparison. | **Potential edge-case behavior change.** Results are now stable across process cultures and avoid culture-specific casing behavior. |
| Cosmos client lifetime | The client did not expose the standard disposable contract. | The Cosmos client implements `IDisposable` and disposes its owned SDK client. | **New capability.** DI containers and explicit owners can now release resources deterministically. Consumers must not dispose shared instances prematurely. |
| Request validation | Null dependencies and malformed request envelopes were not consistently rejected at the public boundary. | Required dependencies are validated, empty messages and missing ID collections are explicitly handled, and failures are logged consistently. | **Observable behavior change.** Invalid requests fail or return through defined validation paths sooner and produce structured diagnostics. |
| Request-handler logging | Logging used dynamic templates and had less consistent event identity and timing coverage. | Compiled structured logging uses stable event IDs for invalid input, processing, chunking, completion, and errors. | **Operational behavior change.** Log messages, event IDs, and structured fields change; dashboards and alert queries may require updates. |
| Initializer queries and delays | Initializers directly invoked Cosmos query extensions and hard-coded delays. | Query execution and delay boundaries are injected internally; production defaults preserve Cosmos execution and one-second waits. | No intended production semantic change. The refactor enables deterministic tests and isolation of deletion/query behavior. |
| Build quality gates | Warnings and recommended analyzers were not uniformly fatal. | Warnings, nullable issues, recommended analyzers, code style, and public documentation errors fail the build. | **Build behavior change.** Existing downstream source included in this repository must be warning-clean. CI can fail on diagnostics that previously passed. |
| Coverage | No repository coverage settings file existed. | Cobertura collection is configured with generated protobuf and intermediate files excluded. | Collection behavior changes only. No numeric pass/fail threshold is configured. |

## Breaking API changes and required migrations

### Removed command-handler compatibility methods

The obsolete compatibility methods in `DefaultCommandHandler` have been removed. The supported surface now consists of async methods:

- `HandlePatchAsync`
- `HandlePostAsync`
- `HandleDeleteAsync`
- `HandleBulkCreateAsync`
- `HandleBulkUpdateAsync`
- `HandleBulkDeleteAsync`

**Migration:** Replace each removed call with the same operation carrying the `Async` suffix and await it. The underlying operation was already asynchronous, so this makes the API contract explicit rather than introducing a synchronous implementation.

### Removed event-handler compatibility methods

The deprecated method region in `DefaultEventHandlers` has been removed. This includes wrappers for:

- Single aggregate event handling.
- Single projection event handling.
- Multi-apply projection handling.
- Aggregate and projection bulk create handling.
- Aggregate and projection bulk update handling.
- Aggregate and projection bulk delete handling.

**Migration:** Use the corresponding async method, including `HandleAggregateEventAsync`, `HandleProjectionEventAsync`, `HandleMultiApplyEventAsync`, and the aggregate/projection bulk `Async` methods.

### Nullable public-contract corrections

Nullable annotations now represent actual optional states in several areas:

- `INostify.HttpClientFactory` is nullable.
- Event payload is nullable at the interface boundary.
- Apply-and-persist overloads can return nullable generic objects.
- Optional constructor values and configuration properties are declared nullable.
- Optional event collections and metadata are declared nullable where applicable.

These are primarily metadata/source-analysis changes, but nullable-enabled consumers may receive new warnings. Consumers should add explicit null handling instead of suppressing those warnings.

### Cosmos client disposal

`NostifyCosmosClient` now implements `IDisposable`. Its disposal path releases the owned Cosmos SDK client.

**Migration guidance:**

- Let a dependency-injection container dispose registered owned instances.
- Dispose explicitly created clients when their lifetime ends.
- Do not dispose a shared singleton while a `Nostify` instance still uses it.

## Runtime behavior changes in detail

### Factory construction and configuration

`NostifyFactory.Build` now validates configuration before creating runtime clients. Required values include the Cosmos API key, database name, endpoint, and Kafka bootstrap servers. Missing or whitespace-only values produce an `InvalidOperationException` that identifies the missing property and the configuration method that should be called.

A null configuration receiver produces `ArgumentNullException`.

When no default partition-key path is supplied, the built instance now uses `/tenantId`.

Kafka topic discovery now compares existing broker topics with requested topics using ordinal case-insensitive equality. This keeps startup idempotent without culture-sensitive lowercasing.

Event Hubs connection-string parsing and endpoint manipulation use explicit ordinal string operations. This removes culture sensitivity and avoids analyzer-identified ambiguous string behavior.

### Event construction, conversion, and validation

Event construction and payload APIs now validate required inputs explicitly. Aggregate-root extraction is centralized so constructors and compatibility paths use the same validation behavior.

Payload conversion behavior is now explicit:

- A missing payload causes `InvalidOperationException` with the target payload type in the message.
- A payload that cannot be converted to the requested type causes `InvalidOperationException` rather than a generic null-reference failure.
- Payload cleanup and validation throw explicit invalid-operation errors if conversion unexpectedly yields null.
- Validation continues to throw `NostifyValidationException` when data-annotation validation fails or disallowed extra properties are present.

The event contract now declares payload as nullable, matching null-payload/delete scenarios and deserialization realities.

### Event-type and topic comparison

Event-type ordering now uses `StringComparison.Ordinal`. Kafka topic existence checks use `StringComparison.OrdinalIgnoreCase`.

This changes only culture-sensitive edge cases, but it is intentional: event and topic identifiers are protocol identifiers, not natural-language text. Their comparison must be deterministic across deployment cultures.

### Event application and property updates

Attribute-based event application now validates a null event argument immediately. Required reflection metadata for property extraction is resolved once and causes a clear `MissingMethodException` during type initialization if the expected method cannot be located.

Mapped-property lookup now performs one dictionary lookup rather than a presence check followed by an index lookup. This is intended as a performance and analyzer improvement with unchanged mapping semantics.

Optional cached property lists are correctly nullable. Existing callers that omit the cache continue to work.

### Default command and event handlers

The async command and event handlers retain their core persistence logic. Behavior-affecting changes are concentrated in API removal, argument validation, nullable contracts, retry-option resolution, and structured failure logging.

Retry-enabled paths continue to use explicitly supplied retry options when present. Otherwise, they resolve the configured default retry options when retry is allowed. Disabling retry bypasses implicit default options.

Tests were migrated to the async surface rather than deleting behavioral assertions, preserving coverage of retry delegation, filtering, persistence, and undeliverable-event handling.

### Async and gRPC event request handling

Request handling now has explicit internal execution boundaries for query execution and logging. Public behavior includes:

- Null `INostify`, trigger-event, and query-executor dependencies are rejected immediately.
- Empty Kafka payloads are logged as warnings and do not proceed to deserialization.
- Deserialization failures are logged as errors with the offending message context.
- Requests without aggregate-root IDs are logged as invalid and do not issue event queries.
- Processing, response chunk counts, event counts, correlation IDs, completion duration, and failures are emitted through compiled structured logs.
- gRPC requests receive equivalent required-ID validation and completion logging.
- Existing response-size chunking remains, with explicit logging of the number of chunks and total events.

Operational consumers should review log-based alerts because message templates and event IDs are now stable but differ from previous ad hoc logging.

### Nostify core persistence and retrieval

Core `Nostify` paths receive stricter argument validation, nullable corrections, and structured logging. Cosmos and Kafka dependencies are separated more clearly so tests can verify persistence and publishing behavior without live services.

Bulk persistence, event retrieval, initialization, and undeliverable-event handling now fail through more explicit validation and dependency boundaries. Existing successful-path semantics are intended to remain unchanged.

Where an HTTP client factory is absent, operations that require HTTP clients now fail explicitly instead of relying on a non-null contract that was not guaranteed by construction.

### Cosmos container and query behavior

Container extensions now validate required inputs and use compiled structured logging for not-found, persistence, retry, and bulk-operation events.

Apply-and-persist overloads now declare nullable results where a missing target object can legitimately produce no value. This clarifies the existing not-found path and prevents callers from assuming a result is always available.

Paged and filtered query contracts receive nullability and execution cleanup. Query materialization in initializers is routed through `IQueryExecutor`, while the default production executor preserves normal Cosmos behavior.

### Projection and current-state initialization

Projection and durable current-state initializers now accept internal injectable query, delay, and deletion boundaries. Public constructors continue to use production defaults.

The production behavior remains:

- Events are loaded and applied in the existing order and scope.
- Point-in-time filters are retained.
- Container rebuilds still delete existing state before repopulation.
- Uninitialized projection polling still waits one second between checks.

The main effect is testability: tests can now verify batching, point-in-time selection, deletion, retry, and polling without a live Cosmos emulator or real delays.

### Validation exception handling and middleware

Validation handlers and middleware now validate dependencies, use structured logging, and handle response serialization more explicitly. Invalid Nostify payloads continue to produce validation responses, but null dependencies and unsupported response states fail earlier and more clearly.

Consumers that assert exact log text or exception types should update those assertions to the new structured behavior.

### Serialization and small contracts

Event-type converters, Cosmos JSON serialization, saga-step conversion, gRPC event mapping, and small DTO contracts receive explicit null checks and corrected nullable annotations.

Several DTO string properties now initialize to `string.Empty` instead of remaining uninitialized null values. Newly constructed DTOs therefore expose empty strings before population rather than null at runtime.

The protobuf source removes an obsolete line from the request contract. Generated intermediate output is excluded from coverage metrics.

## Build and analyzer policy changes

### Repository-wide policy

`Directory.Build.props` introduces these mandatory settings:

- `TreatWarningsAsErrors` enabled.
- Code-analysis warnings treated as errors.
- Latest recommended analysis level.
- Build-time code-style enforcement.
- Nullable reference analysis enabled.
- Deterministic compilation.
- Continuous-integration build metadata when the `CI` property is true.

This materially changes build behavior. A warning that previously allowed a build to succeed now fails the build unless it is fixed or covered by a documented, narrowly scoped exception.

### Public XML documentation

The production project treats missing and malformed public XML documentation as errors. Public members touched by the analyzer pass now have expanded or corrected summaries, parameter documentation, return descriptions, exception documentation, and inherited documentation links.

### Compatibility analyzer exceptions

The root `.editorconfig` disables only a documented set of design rules that would require breaking changes to the already-published 5.x API. These exceptions preserve existing global namespace types, public fields, names, generic parameter names, and mutable static fields until a major-version API review.

Compiler, nullable, reliability, security, globalization, performance, logging, and documentation diagnostics remain enabled.

### Test-project exceptions

The test project suppresses diagnostics that are not useful release gates for a non-packable fixture assembly, including obsolete compatibility usage, common mock/fixture nullability patterns, underscore-separated descriptive test names, and production API-design/performance rules.

Compiler correctness and xUnit diagnostics remain enabled. Real issues found by those diagnostics were corrected, including invalid protected members in sealed fixtures, ambiguous assertions, same-variable comparisons, and calls to removed APIs.

## Test changes

### New behavioral and contract suites

The following new test files expand deterministic coverage:

- `AsyncEventRequestEnvironmentCollection.cs` serializes tests that mutate shared request environment settings.
- `ContainerExtensionsBehavior.Tests.cs` covers Cosmos extension validation and behavioral boundaries.
- `DefaultEventHandlersBehavior.Tests.cs` covers filtering, persistence, retry, and failure paths independent of live Cosmos queries.
- `DefaultEventRequestHandlers.Tests.cs` covers Kafka and gRPC validation, query execution, chunking, logging, and failures.
- `ErrorEvent.Tests.cs` covers error-event and undeliverable-event contracts.
- `NostifyBehavior.Tests.cs` exercises the concrete runtime through injected Kafka and Cosmos boundaries.
- `NostifyCosmosClientContract.Tests.cs` covers client construction, configuration, disposal, and contract behavior.
- `NostifyValidationExceptionHandler.Tests.cs` covers validation exception-handler responses.
- `NostifyValidationExceptionMiddleware.Tests.cs` covers middleware pipeline and response behavior.
- `SerializationEdgeCase.Tests.cs` covers converter and serializer edge cases.
- `SmallContractBehavior.Tests.cs` covers small public contracts and boundary conditions.

### Expanded existing suites

Substantial additions were made to tests for:

- Durable current-state initialization.
- Projection and durable-projection initialization.
- Event factories and event payload behavior.
- Default command and event handlers.
- Retryable containers and queries.
- Paged, filtered, and extension-based queries.
- Nostify construction, persistence, initialization, and retrieval.
- Factory validation and Kafka topic setup.
- Legacy command comparison and conversion.
- JObject conversion and serialization.
- Validation exceptions and saga behavior.

### Removed compatibility-only coverage

Tests dedicated solely to obsolete default-handler wrappers were removed with those wrappers. Behavioral tests that covered actual persistence, retry, and filtering logic were migrated to async APIs and retained.

## Coverage configuration and result

`coverage.runsettings` configures the XPlat Code Coverage collector with Cobertura output. It excludes:

- Intermediate output under `obj`.
- Generated protobuf/gRPC output under `src/Protos`.

The verified Release run produced:

- 84.57% line coverage: 4,233 of 5,005 lines.
- 77.02% branch coverage: 1,864 of 2,420 branches.

No line, branch, or method threshold is currently configured in the project, shared props, run settings, or CI workflow. Coverage is measured but is not an enforceable release gate.

## Verification evidence

The current implementation was verified with a Release build and test run:

- Production and test assemblies compiled successfully.
- Build result: 0 warnings and 0 errors.
- Test result: 1,508 passed, 0 failed, and 1 skipped.
- The skipped test is an existing performance comparison and is not a functional test failure.
- The Release coverage collector completed and generated a Cobertura report.
- Git whitespace validation completed without a whitespace error; four files emit line-ending normalization notices.

## Release-review checklist

Before committing or releasing this change set:

1. Add all intended untracked policy, test, and documentation files to Git.
2. Confirm that removing obsolete handler wrappers is acceptable for the target version under semantic-versioning policy.
3. Search consuming repositories for removed handler calls and migrate them to async methods.
4. Confirm callers handle nullable apply-and-persist results and the optional HTTP client factory.
5. Review exception handlers that depend on `NullReferenceException` for malformed event payloads.
6. Review operational dashboards and alerts for the new structured log event IDs and templates.
7. Confirm `/tenantId` is the desired fallback partition-key path.
8. Confirm ownership and disposal policy for `NostifyCosmosClient` instances.
9. Decide whether numeric line and branch coverage thresholds should be added to CI.
10. Normalize the four reported line-ending differences if the repository requires uniform endings.
11. Run the Release build, full test suite, package build, and coverage collection from the final staged tree.

## Files with the highest implementation impact

The highest-impact production files are:

- `src/DefaultHandlers/DefaultCommandHandlers.cs`
- `src/DefaultHandlers/DefaultEventHandlers.cs`
- `src/DefaultHandlers/DefaultEventRequestHandlers.cs`
- `src/Nostify.cs`
- `src/NostifyCosmosClient.cs`
- `src/NostifyFactory.cs`
- `src/Event/Event.cs`
- `src/Event/IEvent.cs`
- `src/Projection/ProjectionInitializer.cs`
- `src/Projection/DurableProjectionInitializer.cs`
- `src/Projection/DurableCurrentStateInitializer.cs`
- `src/Projection/ExternalDataEventFactory.cs`
- `src/CosmosExtensions/ContainerExtensions.cs`
- `src/ErrorHandling/NostifyValidationExceptionHandler.cs`
- `src/ErrorHandling/NostifyValidationExceptionMiddleware.cs`

The release policy and measurement files are:

- `.editorconfig`
- `Directory.Build.props`
- `coverage.runsettings`
- `nostify.csproj`
- `nostify.Tests/nostify.Tests.csproj`

## Classification of changes that should not alter successful-path behavior

The following broad categories are intended to be behavior-neutral for valid inputs:

- XML documentation additions and corrections.
- Nullable annotations that merely describe already-possible null states.
- Replacement of manual null checks with `ArgumentNullException.ThrowIfNull` when parameter names and boundary timing are equivalent.
- Compiled logging templates where log level and event meaning are preserved.
- Dictionary lookup and collection-count analyzer optimizations.
- Query and delay injection when production defaults call the same Cosmos and timing operations.
- Test-fixture corrections and xUnit assertion modernization.
- Generated-code coverage exclusions.

These changes can still affect compilation diagnostics, exact exception timing, exact log text, or source analysis. They are classified as successful-path behavior-neutral, not externally invisible.
