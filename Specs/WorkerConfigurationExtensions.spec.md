# WorkerConfigurationExtensions Specification

## Overview

`WorkerConfigurationExtensions` provides Azure Functions worker configuration helpers that move nostify's JSON worker setup out of the service template and into the reusable library.

## Class Definition

```csharp
public static class WorkerConfigurationExtensions
```

## Methods

### UseNostifyDefaultJson

```csharp
public static IFunctionsWorkerApplicationBuilder UseNostifyDefaultJson(
    this IFunctionsWorkerApplicationBuilder builder)
```

Configures the Functions worker to use `NewtonsoftJsonObjectSerializer` with `SerializationSettings.NostifyDefault`.

### UseNostifyDefaultConfiguredNewtonsoftJson

```csharp
public static IFunctionsWorkerApplicationBuilder UseNostifyDefaultConfiguredNewtonsoftJson(
    this IFunctionsWorkerApplicationBuilder builder)
```

Configures the Functions worker to use `NewtonsoftJsonObjectSerializer` with `SerializationSettings.NostifyDefault`.

`UseNostifyDefaultJson()` is a shorter wrapper around this method. The underlying configuration lives here.

### UseNostifySystemTextJson

```csharp
[Experimental("NOSTIFY001")]
public static IFunctionsWorkerApplicationBuilder UseNostifySystemTextJson(
    this IFunctionsWorkerApplicationBuilder builder)
```

Configures the Functions worker to use `JsonObjectSerializer` with nostify-compatible `System.Text.Json` options. The method is marked experimental because it depends on custom converters to approximate the framework's Newtonsoft-based behavior.

## System.Text.Json Defaults

The experimental serializer is configured with:

- camelCase property names
- camelCase dictionary keys
- case-insensitive property matching during deserialization
- null-value inclusion
- string enum serialization
- interface converters for `IEvent`, `ISaga`, and `ISagaStep`
- inferred object conversion for dynamic payloads (`Dictionary<string, object?>`, `List<object?>`, primitives)

## Template Integration

The `templates/nostify/_ReplaceMe_/Program.cs` file now calls:

```csharp
builder.UseNostifyDefaultJson();
```

This removes the old duplicated template-local `WorkerConfigurationExtensions` implementation.

## Usage Example

```csharp
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.UseWhenHttp<JwtAuthenticationMiddleware>();
        builder.UseWhenHttp<JwtAuthorizationMiddleware>();
        builder.UseMiddleware<HttpExceptionMiddleware>();
        builder.UseNostifyDefaultJson();
    })
    .Build();
```

## Related Types

- [SerializationSettings](../src/Serialization/SerializationSettings.cs) - Newtonsoft.Json defaults reused by the worker helper
- [NewtonsoftJsonCosmosSerializer](NewtonsoftJsonCosmosSerializer.spec.md) - Related Newtonsoft serialization behavior within nostify
