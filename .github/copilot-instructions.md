# nostify - Event-Sourcing Microservices Framework

## Template System

- Package first: `dotnet pack` to create `bin/Release/nostify.3.8.1.nupkg`
- Install templates locally: `dotnet new install bin/Release/nostify.3.8.1.nupkg`
- Create new service: `dotnet new nostify -ag <AggregateRootName> -p <PortNumber>`
  - Example: `dotnet new nostify -ag TestItem -p 7999`
- Template validation: After installation, verify templates are available with:
  ```bash
  dotnet new list | grep nostify

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

## Working Effectively

### Bootstrap, Build, and Test
- Install dependencies: `dotnet restore` -- takes 3-7 seconds for main project, up to 25 seconds for cold restore
- Build the library: `dotnet build` -- takes 1-3 seconds. NEVER CANCEL. Set timeout to 120+ seconds
- Test the library: `dotnet test nostify.Tests/` -- takes 5-6 seconds, runs 78 tests. NEVER CANCEL. Set timeout to 120+ seconds  
- Package for distribution: `dotnet pack` -- takes 1-2 seconds
- Clean build artifacts: `dotnet clean` -- takes 1 second

### Validation
- ALWAYS run `dotnet build && dotnet test nostify.Tests/` after making changes to validate they work
- The build produces 115+ warnings but zero errors when building from clean state - this is normal and expected
- Build warnings include: XML documentation issues, nullability warnings, missing XML comments
- All 78 tests must pass - any test failures indicate breaking changes
- Run `dotnet format --verify-no-changes` to check code formatting (many violations exist in current codebase)
- Use `dotnet format` to fix whitespace formatting issues before committing

### Template System
- Package first: `dotnet pack` to create bin/Release/nostify.3.8.1.nupkg
- Install templates locally: `dotnet new install bin/Release/nostify.3.8.1.nupkg`
- Create new service: `dotnet new nostify -ag <AggregateRootName> -p <PortNumber>`
  - This creates a complete Azure Functions project with CQRS/Event Sourcing pattern
  - Example: `dotnet new nostify -ag InventoryItem -p 7072`
  - Template validation: Full process takes ~14 seconds including restore and build
- Add aggregate to existing service: `dotnet new nostifyAggregate -ag <AggregateName> -s <ServiceName>`
- Add projection: `dotnet new nostifyProjection -ag <BaseAggregateName> --projectionName <ProjectionName>`
- Uninstall templates: `dotnet new uninstall nostify` (if conflicts occur)

## External Dependencies (Required for Full Operation)

The nostify framework requires these external services to function properly:

### Azure Cosmos DB
- Install: Cosmos DB Emulator from Microsoft
- Connection string: Uses emulator by default (`CosmosEmulatorConnectionString`)
- Purpose: Event store and current state projections

### Apache Kafka
- Install: Confluent CLI from https://docs.confluent.io/confluent-cli/current/install.html
- Start: `confluent local kafka start` -- takes 30-60 seconds to fully start. NEVER CANCEL
- Create topics: `confluent local kafka topic create <TopicName>`
- Purpose: Event messaging and pub/sub between microservices

### Azurite (Azure Storage Emulator)
- Install: `npm install -g azurite`
- VS Code Extension: Available in marketplace
- Purpose: Azure Functions runtime storage

### Docker Desktop
- Required for: Kafka, additional containers
- Install from: https://www.docker.com/products/docker-desktop/

### .NET 8.0
- Target framework: net8.0
- Required for building and running

## Common Development Tasks

### Creating a New Microservice
1. `dotnet new nostify -ag OrderAggregate -p 7073`
2. `cd OrderAggregate && dotnet restore`
3. `dotnet build` -- verify it compiles
4. Edit configuration in `appsettings.json` and `local.settings.json`
5. Start external dependencies (Cosmos DB, Kafka)
6. `func start` or F5 in VS Code to run locally

### Template Generated Structure
```
ProjectName/
├── Aggregates/           # Domain aggregates and commands
├── Projections/         # Read model projections  
├── Events/              # Event handlers
├── Scripts/             # Helper scripts
├── Program.cs           # DI container setup
├── host.json           # Azure Functions configuration
├── local.settings.json # Local environment settings
└── appsettings.json    # Application settings
```

### Validation Scenarios
After making changes to the nostify library, validate by:

1. **Build validation**: `dotnet build` succeeds with only warnings (115+ warnings, 0 errors expected)
2. **Test validation**: All 78 tests pass in `dotnet test nostify.Tests/`
3. **Template validation**: 
   - `dotnet pack` to create the NuGet package
   - `dotnet new install bin/Release/nostify.3.8.1.nupkg` to install templates
   - Create test service: `mkdir /tmp/test && cd /tmp/test && dotnet new nostify -ag TestItem -p 7999`
   - `cd TestItem && dotnet restore && dotnet build` -- should succeed with warnings but no errors
4. **Integration validation**: Start Cosmos DB emulator and Kafka, run generated service with `func start`
   - Note: Azure Functions Core Tools required for running services locally
   - Generated services include complete Azure Functions runtime with HTTP triggers

## Documentation Guidelines

### Specification Files
- **BEFORE modifying any class, interface, or module**: Read the corresponding spec file from `/Specs/` to understand the component's purpose, responsibilities, and relationships
  - Spec files follow naming convention: `<ComponentName>.spec.md`
  - Example: Before modifying `Event.cs`, read `/Specs/Event.spec.md`
  - Use the spec file index at `/Specs/README.md` to locate related specs
- **AFTER making changes to any class, interface, or module**: Update the corresponding spec file to reflect the changes
  - Add new properties, methods, or behaviors to the spec
  - Update relationships if dependencies change
  - Document any new usage examples or patterns
  - Update the "Key Relationships" section if component interactions change
- **When creating new classes/interfaces**: Create a corresponding spec file following the existing format
- Spec files are the source of truth for component behavior and design intent

### Version Update List Management
- **NEVER overwrite or remove existing version entries** in README.md update list unless explicitly instructed to do so
- When adding new version entries, **always preserve all previous version information**
- Version entries should be added at the top of the list, pushing older versions down
- Each version should maintain its complete feature/bug fix descriptions
- Only modify version entries when explicitly requested for corrections
- **MANDATORY**: For each version bump, a new bullet point must be added to the update list documenting the changes

## Key Architecture Concepts

### Event Sourcing
- All state changes captured as immutable events
- Current state derived by replaying events
- Events stored in Cosmos DB containers

### CQRS (Command Query Responsibility Segregation)  
- Commands: Write operations that create events
- Queries: Read operations against projections
- Separate containers for commands and queries

### Aggregates
- Domain entities that handle commands
- Implement `IAggregate` and extend `NostifyObject`
- Apply events via `Apply(Event eventToApply)` method

### Projections
- Read models built from event streams  
- Can combine data from multiple aggregates
- Implement `IProjection` and extend `NostifyObject`

### Commands
- Extend `NostifyCommand` base class
- Represent user intentions (Create, Update, Delete)
- Validated before becoming events

## Troubleshooting Common Issues

### Build Warnings
- 115+ warnings are normal (nullability, XML comments, etc.)
- Zero errors required for successful build
- Warnings do not affect functionality

### Template Installation Issues
- Uninstall conflicting versions: `dotnet new uninstall nostify`
- Clear template cache if needed: `dotnet new --debug:reinit`

### External Service Dependencies
- Cosmos DB: Ensure emulator is running on https://localhost:8081
- Kafka: Verify `confluent local kafka start` completed successfully
- Check ports are not in use by other applications

### Formatting Issues  
- Run `dotnet format` to fix whitespace violations
- Many formatting issues exist in current codebase
- Focus on functional correctness over formatting consistency

## File Locations Reference

### Main Library Structure
```
/src/
├── Base_Classes/        # Core base classes (NostifyObject)
├── CosmosExtensions/    # Cosmos DB query extensions
├── Event/              # Event handling infrastructure
├── Extensions/         # Extension methods
├── Projection/         # Projection infrastructure
├── Saga/               # Saga orchestration
├── Validation/         # Command validation
├── INostify.cs         # Main interface
├── Nostify.cs          # Main implementation
├── NostifyCommand.cs   # Command base class
└── NostifyFactory.cs   # Factory for configuration
```

### Test Structure  
```
/nostify.Tests/
├── Event.Tests.cs           # Event handling tests
├── ProjectionInitializer.Tests.cs # Projection tests
├── Saga.Tests.cs           # Saga orchestration tests
└── TestModels.cs           # Test domain models
```

### Templates
```
/templates/
├── nostify/            # Main service template
├── nostifyAggregate/   # Aggregate template  
└── nostifyProjection/  # Projection template
```

## Performance Notes

- **Build time**: 1-3 seconds (fast iteration)
- **Test time**: 5-6 seconds for full suite (78 tests)
- **Package time**: 1-2 seconds  
- **Template generation**: Instantaneous
- **Template validation**: ~14 seconds for full restore + build cycle
- **Cosmos queries**: Use LINQ for type-safe queries
- **Event processing**: Async/await throughout
- **Binary output**: Generated services create ~35MB of deployment artifacts

## Common Command Outputs

### Expected Build Output
```bash
$ dotnet build
MSBuild version 17.8.32+74df0b3f5 for .NET
  Determining projects to restore...
  All projects are up-to-date for restore.
  nostify -> /home/runner/work/nostify/nostify/bin/Debug/net8.0/nostify.dll

Build succeeded.
    0 Warning(s)  # May be 115+ warnings if building from clean state
    0 Error(s)

Time Elapsed 00:00:01.17
```

### Expected Test Output  
```bash
$ dotnet test nostify.Tests/
Test run for /home/runner/work/nostify/nostify/nostify.Tests/bin/Debug/net8.0/nostify.Tests.dll (.NETCoreApp,Version=v8.0)
Microsoft (R) Test Execution Command Line Tool Version 17.8.0 (x64)
Copyright (c) Microsoft Corporation.  All rights reserved.

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    78, Skipped:     0, Total:    78, Duration: 258 ms - nostify.Tests.dll (net8.0)
```

### Template Installation
```bash
$ dotnet new install bin/Release/nostify.3.8.1.nupkg
Success: nostify::3.8.1 installed the following templates:
Template Name       Short Name         Language  Tags              
------------------  -----------------  --------  ------------------
Nostify Aggregate   nostifyAggregate             Azure/Microservice
Nostify Projection  nostifyProjection            Azure/Microservice
Nostify Template    nostify                      Azure/Microservice
```