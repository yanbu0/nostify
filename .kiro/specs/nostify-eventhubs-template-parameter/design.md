# Design Document

## Overview

This design implements EventHubs support in the nostify dotnet template by adding a conditional parameter that switches the messaging configuration from Kafka to EventHubs. The solution uses dotnet template engine's conditional compilation features to generate different code paths based on the parameter value.

## Architecture

The implementation follows the existing template architecture pattern:

1. **Template Configuration Layer**: Extends `template.json` with the new EventHubs parameter
2. **Code Generation Layer**: Uses conditional compilation directives in `Program.cs` to switch between Kafka and EventHubs
3. **Configuration Layer**: Provides appropriate configuration variable names for each messaging system

## Components and Interfaces

### Template Configuration (template.json)

**New Symbol Addition:**
```json
"eventHubs": {
    "type": "parameter",
    "dataType": "bool",
    "isRequired": false,
    "defaultValue": "false",
    "description": "Use EventHubs instead of Kafka for messaging"
}
```

**Parameter Mapping:**
- Long form: `--eventHubs`
- Short form: `-eh` (requires additional configuration)
- Default behavior: Kafka (when parameter is false or omitted)

### Program.cs Modifications

**Conditional Code Blocks:**
The design uses template engine conditional compilation to generate different code based on the `eventHubs` parameter. The `#if` directives are processed during template generation and do NOT appear in the final generated code.

**Template Source (with conditionals):**
```csharp
#if (eventHubs)
string eventHubConnectionString = config.GetValue<string>("EventHubConnectionString");
#else
string kafka = config.GetValue<string>("BrokerList");
#endif

var nostify = NostifyFactory.WithCosmos(...)
#if (eventHubs)
    .WithEventHubs(eventHubConnectionString)
#else
    .WithKafka(kafka)
#endif
    .WithHttp(httpClientFactory)
    .Build<_ReplaceMe_>(verbose: true);
```

### Event Handler Modifications

**Kafka Event Handlers (Current):**
Event handlers currently include DEBUG conditionals for Kafka authentication settings:

```csharp
[KafkaTrigger("BrokerList",
    "Create__ReplaceMe_",
    #if DEBUG
    Protocol = BrokerProtocol.NotSet,
    AuthenticationMode = BrokerAuthenticationMode.NotSet,
    #else
    Username = "KafkaApiKey",
    Password = "KafkaApiSecret",
    Protocol = BrokerProtocol.SaslSsl,
    AuthenticationMode = BrokerAuthenticationMode.Plain,
    #endif
    ConsumerGroup = "_ReplaceMe_")]
```

**EventHubs Event Handlers:**
When EventHubs is enabled, the DEBUG conditionals should be completely removed since EventHubs uses different authentication:

```csharp
#if (eventHubs)
[KafkaTrigger("BrokerList",
    "Create__ReplaceMe_",
    Username = "$ConnectionString",
    Password = "EventHubConnectionString",
    Protocol = BrokerProtocol.SaslSsl,
    AuthenticationMode = BrokerAuthenticationMode.Plain,
    ConsumerGroup = "_ReplaceMe_")]
#else
[KafkaTrigger("BrokerList",
    "Create__ReplaceMe_",
    #if DEBUG
    Protocol = BrokerProtocol.NotSet,
    AuthenticationMode = BrokerAuthenticationMode.NotSet,
    #else
    Username = "KafkaApiKey",
    Password = "KafkaApiSecret",
    Protocol = BrokerProtocol.SaslSsl,
    AuthenticationMode = BrokerAuthenticationMode.Plain,
    #endif
    ConsumerGroup = "_ReplaceMe_")]
#endif
```

**Generated Output Examples:**

*EventHubs = true:*
```csharp
[KafkaTrigger("BrokerList",
    "Create__ReplaceMe_",
    Username = "$ConnectionString",
    Password = "EventHubConnectionString",
    Protocol = BrokerProtocol.SaslSsl,
    AuthenticationMode = BrokerAuthenticationMode.Plain,
    ConsumerGroup = "_ReplaceMe_")]
```

*EventHubs = false/default:*
```csharp
[KafkaTrigger("BrokerList",
    "Create__ReplaceMe_",
    #if DEBUG
    Protocol = BrokerProtocol.NotSet,
    AuthenticationMode = BrokerAuthenticationMode.NotSet,
    #else
    Username = "KafkaApiKey",
    Password = "KafkaApiSecret",
    Protocol = BrokerProtocol.SaslSsl,
    AuthenticationMode = BrokerAuthenticationMode.Plain,
    #endif
    ConsumerGroup = "_ReplaceMe_")]
```

### Configuration Variables

**Kafka Configuration (Default):**
- Variable: `BrokerList`
- Usage: `.WithKafka(kafka)`

**EventHubs Configuration:**
- Variable: `EventHubConnectionString`
- Usage: `.WithEventHubs(eventHubConnectionString)`

## Data Models

No new data models are required. The implementation only affects:
- Template configuration schema
- Generated code structure
- Configuration variable names

## Error Handling

**Template Parameter Validation:**
- The dotnet template engine handles boolean parameter validation automatically
- Invalid values will result in standard template engine error messages

**Runtime Configuration:**
- Missing configuration values will be handled by the existing nostify framework error handling
- No additional error handling is required at the template level

## Testing Strategy

**Template Generation Testing:**
1. Test default behavior (Kafka) when no parameter is provided
2. Test EventHubs generation with `--eventHubs true`
3. Test EventHubs generation with `-eh true`
4. Verify correct configuration variable names are generated
5. Ensure generated code compiles successfully for both scenarios

**Integration Testing:**
1. Generate projects with both configurations
2. Verify the generated projects build successfully
3. Test that appropriate NuGet packages are referenced (if different)

## Implementation Details

### Template Engine Features Used

**Conditional Compilation:**
- Uses `#if (symbol)` preprocessor directives in template source
- Template engine evaluates conditions during generation
- Generates clean code with only the selected branch included
- No conditional compilation artifacts remain in generated output

**Parameter Substitution:**
- Configuration variable names are replaced based on conditions
- Comments and documentation are updated contextually

### File Modifications Required

1. **template.json**: Add eventHubs parameter definition
2. **Program.cs**: Add conditional compilation blocks for messaging configuration
3. **Event Handlers**: Remove DEBUG conditionals and Kafka-specific authentication when EventHubs is enabled
4. **local.settings.json** (optional): Could include conditional configuration examples

### Backward Compatibility

- Existing templates continue to work unchanged (Kafka remains default)
- No breaking changes to existing parameter structure
- Generated projects maintain same API surface regardless of messaging provider

## Design Decisions and Rationales

**Boolean Parameter Choice:**
- Simple true/false parameter is intuitive for developers
- Aligns with common dotnet template patterns
- Extensible for future messaging providers if needed

**Conditional Compilation over Runtime Switching:**
- Generates cleaner, more focused code
- Eliminates unused configuration variables
- Reduces cognitive load for developers reading generated code

**Configuration Variable Naming:**
- `EventHubConnectionString` follows Azure naming conventions
- `BrokerList` maintains existing Kafka convention
- Clear distinction prevents configuration mistakes

**Default to Kafka:**
- Maintains backward compatibility
- Existing documentation and examples remain valid
- Gradual adoption path for EventHubs users