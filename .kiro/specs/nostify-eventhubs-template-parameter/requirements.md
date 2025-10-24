# Requirements Document

## Introduction

This feature enhances the nostify dotnet template to support Azure EventHubs as an alternative messaging system to Kafka. Users will be able to specify an EventHubs parameter when creating new projects, which will configure the template to use EventHubs instead of the default Kafka configuration.

## Glossary

- **Nostify Template**: The dotnet project template that generates microservice scaffolding code
- **EventHubs Parameter**: A command-line parameter (--eventHubs or -eh) that switches messaging configuration from Kafka to EventHubs
- **Template Engine**: The dotnet template system that processes template.json configuration and applies parameter substitutions
- **NostifyFactory**: The factory class that configures messaging providers for the nostify framework

## Requirements

### Requirement 1

**User Story:** As a developer, I want to specify EventHubs as the messaging provider when creating a new nostify project, so that I can use Azure EventHubs instead of Kafka for my microservice.

#### Acceptance Criteria

1. WHEN a developer runs `dotnet new nostify --eventHubs true`, THE Template_Engine SHALL generate code that uses `.WithEventHubs(eventHubConnectionString)` instead of `.WithKafka(kafka)`
2. WHEN a developer runs `dotnet new nostify -eh true`, THE Template_Engine SHALL generate code that uses `.WithEventHubs(eventHubConnectionString)` instead of `.WithKafka(kafka)`
3. WHEN a developer runs `dotnet new nostify` without the EventHubs parameter, THE Template_Engine SHALL generate code that uses `.WithKafka(kafka)` as the default behavior
4. WHERE EventHubs is enabled, THE Template_Engine SHALL replace configuration variable references from Kafka-specific to EventHubs-specific settings

### Requirement 2

**User Story:** As a developer, I want the template to use appropriate configuration variable names for EventHubs, so that my generated project has clear and consistent configuration settings.

#### Acceptance Criteria

1. WHERE EventHubs is enabled, THE Template_Engine SHALL replace "BrokerList" configuration references with "EventHubConnectionString"
2. WHERE EventHubs is enabled, THE Template_Engine SHALL update configuration comments to reference EventHubs instead of Kafka
3. WHEN EventHubs configuration is used, THE Template_Engine SHALL maintain the same configuration pattern as the Kafka implementation
4. THE Template_Engine SHALL ensure both Kafka and EventHubs configurations follow the same structural pattern in Program.cs

### Requirement 3

**User Story:** As a developer, I want the template parameter to be properly documented and discoverable, so that I can understand how to use the EventHubs option.

#### Acceptance Criteria

1. THE Template_Engine SHALL include the eventHubs parameter in the template.json symbols section
2. THE Template_Engine SHALL provide a clear description for the eventHubs parameter
3. THE Template_Engine SHALL set the eventHubs parameter as optional with a default value of false
4. THE Template_Engine SHALL support both long form (--eventHubs) and short form (-eh) parameter syntax