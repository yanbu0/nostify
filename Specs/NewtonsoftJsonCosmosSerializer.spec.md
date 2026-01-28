# NewtonsoftJsonCosmosSerializer Specification

## Overview

`NewtonsoftJsonCosmosSerializer` is a custom Cosmos DB serializer using Newtonsoft.Json for consistent JSON serialization across the nostify framework.

## Class Definition

```csharp
public class NewtonsoftJsonCosmosSerializer : CosmosSerializer
```

## Constructor

```csharp
public NewtonsoftJsonCosmosSerializer(JsonSerializerSettings settings = null)
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `settings` | `JsonSerializerSettings` | `null` | Custom serializer settings |

## Methods

### FromStream

```csharp
public override T FromStream<T>(Stream stream)
```

Deserializes a JSON stream to a typed object.

| Parameter | Type | Description |
|-----------|------|-------------|
| `stream` | `Stream` | JSON stream from Cosmos DB |

**Returns:** `T` - Deserialized object

### ToStream

```csharp
public override Stream ToStream<T>(T input)
```

Serializes an object to a JSON stream.

| Parameter | Type | Description |
|-----------|------|-------------|
| `input` | `T` | Object to serialize |

**Returns:** `Stream` - JSON stream

## Default Settings

When no settings are provided:

```csharp
var defaultSettings = new JsonSerializerSettings
{
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    NullValueHandling = NullValueHandling.Ignore,
    DateTimeZoneHandling = DateTimeZoneHandling.Utc,
    Converters = { new StringEnumConverter() }
};
```

## Behavior Details

### FromStream

```csharp
public override T FromStream<T>(Stream stream)
{
    if (stream == null || stream.Length == 0)
    {
        return default!; // Cosmos may pass empty stream for null payloads
    }
    
    using var streamReader = new StreamReader(stream, Encoding.UTF8);
    using var jsonReader = new JsonTextReader(streamReader);
    
    return _serializer.Deserialize<T>(jsonReader);
}
```

### ToStream

```csharp
public override Stream ToStream<T>(T input)
{
    var stream = new MemoryStream();
    
    // Use StreamWriter with leaveOpen to prevent disposal
    using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true))
    using (var jsonWriter = new JsonTextWriter(writer))
    {
        _serializer.Serialize(jsonWriter, input);
        jsonWriter.Flush();
    }
    
    stream.Position = 0;
    return stream;
}
```

## Usage

### With NostifyFactory

```csharp
var nostify = NostifyFactory.Build(
    cosmosConnectionString: "...",
    kafkaUrl: "...",
    databaseName: "MyDB",
    jsonSerializerSettings: new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateFormatString = "yyyy-MM-ddTHH:mm:ss.fffZ"
    }
);
```

### Manual Configuration

```csharp
var cosmosOptions = new CosmosClientOptions
{
    Serializer = new NewtonsoftJsonCosmosSerializer(new JsonSerializerSettings
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) }
    })
};

var cosmosClient = new CosmosClient(connectionString, cosmosOptions);
```

## Why Newtonsoft.Json?

1. **Consistency** - Same serializer used across the framework
2. **Flexibility** - More configuration options than System.Text.Json
3. **JObject Support** - Better dynamic JSON handling
4. **Enum Handling** - String enum conversion built-in
5. **Legacy Compatibility** - Matches existing Azure SDK patterns

## Serialization Format

Objects are serialized with:

- **camelCase** property names
- **Null values** omitted
- **UTC dates** with ISO 8601 format
- **Enums** as strings

Example:

```csharp
public class Order
{
    public Guid Id { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTime? ShippedDate { get; set; }
}
```

Serializes to:

```json
{
    "id": "550e8400-e29b-41d4-a716-446655440000",
    "status": "Pending",
    "total": 99.99
}
```

Note: `ShippedDate` is omitted because it's null.

## UTF-8 Encoding

Uses UTF-8 without BOM (Byte Order Mark) as required by Cosmos DB:

```csharp
new StreamWriter(stream, Encoding.UTF8, 1024, leaveOpen: true)
```

## Best Practices

1. **Use Defaults** - The default settings work for most cases
2. **Consistent Settings** - Use same settings across the app
3. **Test Serialization** - Verify expected JSON format
4. **Handle Nulls** - Be aware null values are omitted

## Related Types

- [NostifyFactory](NostifyFactory.spec.md) - Creates serializer
- [Nostify](Nostify.spec.md) - Uses serializer
- [NostifyExtensions](NostifyExtensions.spec.md) - JSON helpers
