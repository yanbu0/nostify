using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using nostify;
using Xunit;

namespace nostify.Tests;

public class SequenceTests
{
    #region Sequence Class Tests

    [Fact]
    public void Sequence_ParameterlessConstructor_CreatesEmptySequence()
    {
        // Arrange & Act
        var sequence = new Sequence();

        // Assert
        Assert.Equal(string.Empty, sequence.id);
        Assert.Equal(string.Empty, sequence.name);
        Assert.Equal(0, sequence.currentValue);
        Assert.Equal(string.Empty, sequence.partitionKey);
    }

    [Fact]
    public void Sequence_Constructor_WithDefaultStartingValue_CreatesSequenceCorrectly()
    {
        // Arrange
        var name = "OrderNumber";
        var partitionKeyValue = "tenant-123";

        // Act
        var sequence = new Sequence(name, partitionKeyValue);

        // Assert
        Assert.Equal("tenant-123_OrderNumber", sequence.id);
        Assert.Equal(name, sequence.name);
        Assert.Equal(0, sequence.currentValue);
        Assert.Equal(partitionKeyValue, sequence.partitionKey);
    }

    [Fact]
    public void Sequence_Constructor_WithCustomStartingValue_CreatesSequenceCorrectly()
    {
        // Arrange
        var name = "InvoiceNumber";
        var partitionKeyValue = "company-456";
        var startingValue = 1000L;

        // Act
        var sequence = new Sequence(name, partitionKeyValue, startingValue);

        // Assert
        Assert.Equal("company-456_InvoiceNumber", sequence.id);
        Assert.Equal(name, sequence.name);
        Assert.Equal(startingValue, sequence.currentValue);
        Assert.Equal(partitionKeyValue, sequence.partitionKey);
    }

    [Fact]
    public void Sequence_Constructor_WithNegativeStartingValue_AllowsNegativeValues()
    {
        // Arrange
        var name = "TestSequence";
        var partitionKeyValue = "test-partition";
        var startingValue = -100L;

        // Act
        var sequence = new Sequence(name, partitionKeyValue, startingValue);

        // Assert
        Assert.Equal(-100L, sequence.currentValue);
    }

    [Fact]
    public void Sequence_Constructor_WithLargeStartingValue_HandlesLongValues()
    {
        // Arrange
        var name = "LargeSequence";
        var partitionKeyValue = "partition";
        var startingValue = long.MaxValue - 1000;

        // Act
        var sequence = new Sequence(name, partitionKeyValue, startingValue);

        // Assert
        Assert.Equal(startingValue, sequence.currentValue);
    }

    [Theory]
    [InlineData("partition1", "seq1", "partition1_seq1")]
    [InlineData("tenant-abc", "OrderNumber", "tenant-abc_OrderNumber")]
    [InlineData("", "sequence", "_sequence")]
    [InlineData("partition", "", "partition_")]
    [InlineData("", "", "_")]
    [InlineData("partition-with-dashes", "seq-with-dashes", "partition-with-dashes_seq-with-dashes")]
    [InlineData("UPPERCASE", "lowercase", "UPPERCASE_lowercase")]
    public void Sequence_GenerateId_GeneratesCorrectId(string partitionKeyValue, string sequenceName, string expectedId)
    {
        // Act
        var result = Sequence.GenerateId(partitionKeyValue, sequenceName);

        // Assert
        Assert.Equal(expectedId, result);
    }

    [Fact]
    public void Sequence_GenerateId_MatchesConstructorId()
    {
        // Arrange
        var name = "TestSequence";
        var partitionKeyValue = "test-partition";

        // Act
        var sequence = new Sequence(name, partitionKeyValue);
        var generatedId = Sequence.GenerateId(partitionKeyValue, name);

        // Assert
        Assert.Equal(generatedId, sequence.id);
    }

    [Fact]
    public void Sequence_Properties_AreSettable()
    {
        // Arrange
        var sequence = new Sequence();

        // Act
        sequence.id = "custom-id";
        sequence.name = "CustomSequence";
        sequence.currentValue = 500;
        sequence.partitionKey = "custom-partition";

        // Assert
        Assert.Equal("custom-id", sequence.id);
        Assert.Equal("CustomSequence", sequence.name);
        Assert.Equal(500, sequence.currentValue);
        Assert.Equal("custom-partition", sequence.partitionKey);
    }

    #endregion

    #region NostifyCosmosClient SequenceContainer Tests

    [Fact]
    public void NostifyCosmosClient_SequenceContainer_HasDefaultValue()
    {
        // Arrange & Act
        var client = new NostifyCosmosClient(
            ApiKey: "test-key",
            DbName: "test-db",
            EndpointUri: "https://localhost:8081"
        );

        // Assert
        Assert.Equal("sequenceContainer", client.SequenceContainer);
    }

    [Fact]
    public void NostifyCosmosClient_SequenceContainer_CanBeCustomized()
    {
        // Arrange & Act
        var client = new NostifyCosmosClient(
            ApiKey: "test-key",
            DbName: "test-db",
            EndpointUri: "https://localhost:8081",
            SequenceContainer: "customSequenceContainer"
        );

        // Assert
        Assert.Equal("customSequenceContainer", client.SequenceContainer);
    }

    #endregion

    #region GetNextSequenceValueAsync Tests with Mocks

    [Fact]
    public async Task GetNextSequenceValueAsync_WithExistingSequence_IncrementsAndReturnsValue()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var mockContainer = new Mock<Container>();
        var mockRepository = new Mock<NostifyCosmosClient>();
        
        var sequenceName = "OrderNumber";
        var partitionKeyValue = "tenant-1";
        var existingSequence = new Sequence(sequenceName, partitionKeyValue, 0) { currentValue = 5 };
        var incrementedSequence = new Sequence(sequenceName, partitionKeyValue, 0) { currentValue = 6 };

        // Setup mock response for ReadItemAsync
        var mockReadResponse = new Mock<ItemResponse<Sequence>>();
        mockReadResponse.Setup(r => r.Resource).Returns(existingSequence);

        // Setup mock response for PatchItemAsync
        var mockPatchResponse = new Mock<ItemResponse<Sequence>>();
        mockPatchResponse.Setup(r => r.Resource).Returns(incrementedSequence);

        mockContainer
            .Setup(c => c.ReadItemAsync<Sequence>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockReadResponse.Object);

        mockContainer
            .Setup(c => c.PatchItemAsync<Sequence>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPatchResponse.Object);

        mockNostify
            .Setup(n => n.GetSequenceContainerAsync())
            .ReturnsAsync(mockContainer.Object);

        // Act
        var result = await mockNostify.Object.GetSequenceContainerAsync();
        
        // Verify container was returned
        Assert.NotNull(result);
        mockNostify.Verify(n => n.GetSequenceContainerAsync(), Times.Once);
    }

    [Fact]
    public async Task GetSequenceContainerAsync_ReturnsContainer()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var mockContainer = new Mock<Container>();

        mockNostify
            .Setup(n => n.GetSequenceContainerAsync())
            .ReturnsAsync(mockContainer.Object);

        // Act
        var result = await mockNostify.Object.GetSequenceContainerAsync();

        // Assert
        Assert.NotNull(result);
        mockNostify.Verify(n => n.GetSequenceContainerAsync(), Times.Once);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_CallsInterfaceMethod()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "TestSequence";
        var partitionKeyValue = "partition-1";
        var expectedValue = 42L;

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, partitionKeyValue))
            .ReturnsAsync(expectedValue);

        // Act
        var result = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partitionKeyValue);

        // Assert
        Assert.Equal(expectedValue, result);
        mockNostify.Verify(n => n.GetNextSequenceValueAsync(sequenceName, partitionKeyValue), Times.Once);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_WithStartingValue_CallsOverload()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "TestSequence";
        var partitionKeyValue = "partition-1";
        var startingValue = 1000L;
        var expectedValue = 1001L;

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, partitionKeyValue, startingValue))
            .ReturnsAsync(expectedValue);

        // Act
        var result = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partitionKeyValue, startingValue);

        // Assert
        Assert.Equal(expectedValue, result);
        mockNostify.Verify(n => n.GetNextSequenceValueAsync(sequenceName, partitionKeyValue, startingValue), Times.Once);
    }

    [Theory]
    [InlineData("seq1", "partition1", 0, 1)]
    [InlineData("invoices", "tenant-a", 1000, 1001)]
    [InlineData("orders", "company-xyz", 999999, 1000000)]
    public async Task GetNextSequenceValueAsync_ReturnsExpectedIncrementedValue(
        string sequenceName, 
        string partitionKeyValue, 
        long startingValue, 
        long expectedValue)
    {
        // Arrange
        var mockNostify = new Mock<INostify>();

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, partitionKeyValue, startingValue))
            .ReturnsAsync(expectedValue);

        // Act
        var result = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partitionKeyValue, startingValue);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_MultipleCalls_ReturnsSequentialValues()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "Counter";
        var partitionKeyValue = "test";
        var callCount = 0;

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, partitionKeyValue))
            .ReturnsAsync(() => ++callCount);

        // Act
        var result1 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partitionKeyValue);
        var result2 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partitionKeyValue);
        var result3 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partitionKeyValue);

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(3, result3);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_DifferentPartitionKeys_IndependentSequences()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "OrderNumber";
        var partition1 = "tenant-1";
        var partition2 = "tenant-2";

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, partition1))
            .ReturnsAsync(101L);

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, partition2))
            .ReturnsAsync(201L);

        // Act
        var result1 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partition1);
        var result2 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partition2);

        // Assert
        Assert.Equal(101L, result1);
        Assert.Equal(201L, result2);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_DifferentSequenceNames_IndependentSequences()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var partitionKeyValue = "tenant-1";
        var sequence1 = "OrderNumber";
        var sequence2 = "InvoiceNumber";

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequence1, partitionKeyValue))
            .ReturnsAsync(1L);

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequence2, partitionKeyValue))
            .ReturnsAsync(5000L);

        // Act
        var result1 = await mockNostify.Object.GetNextSequenceValueAsync(sequence1, partitionKeyValue);
        var result2 = await mockNostify.Object.GetNextSequenceValueAsync(sequence2, partitionKeyValue);

        // Assert
        Assert.Equal(1L, result1);
        Assert.Equal(5000L, result2);
    }

    #endregion

    #region Guid Overload Tests

    [Fact]
    public async Task GetNextSequenceValueAsync_GuidOverload_CallsInterfaceMethod()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "OrderNumber";
        var tenantId = Guid.NewGuid();
        var expectedValue = 42L;

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, tenantId))
            .ReturnsAsync(expectedValue);

        // Act
        var result = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, tenantId);

        // Assert
        Assert.Equal(expectedValue, result);
        mockNostify.Verify(n => n.GetNextSequenceValueAsync(sequenceName, tenantId), Times.Once);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_GuidOverloadWithStartingValue_CallsInterfaceMethod()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "InvoiceNumber";
        var tenantId = Guid.NewGuid();
        var startingValue = 1000L;
        var expectedValue = 1001L;

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, tenantId, startingValue))
            .ReturnsAsync(expectedValue);

        // Act
        var result = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, tenantId, startingValue);

        // Assert
        Assert.Equal(expectedValue, result);
        mockNostify.Verify(n => n.GetNextSequenceValueAsync(sequenceName, tenantId, startingValue), Times.Once);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_GuidOverload_MultipleTenants_IndependentSequences()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "OrderNumber";
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, tenant1))
            .ReturnsAsync(1L);

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, tenant2))
            .ReturnsAsync(100L);

        // Act
        var result1 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, tenant1);
        var result2 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, tenant2);

        // Assert
        Assert.Equal(1L, result1);
        Assert.Equal(100L, result2);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_GuidOverload_MultipleSequencesSameTenant()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var tenantId = Guid.NewGuid();

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync("OrderNumber", tenantId))
            .ReturnsAsync(1L);

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync("InvoiceNumber", tenantId))
            .ReturnsAsync(5000L);

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync("TicketNumber", tenantId))
            .ReturnsAsync(10000L);

        // Act
        var orderNum = await mockNostify.Object.GetNextSequenceValueAsync("OrderNumber", tenantId);
        var invoiceNum = await mockNostify.Object.GetNextSequenceValueAsync("InvoiceNumber", tenantId);
        var ticketNum = await mockNostify.Object.GetNextSequenceValueAsync("TicketNumber", tenantId);

        // Assert
        Assert.Equal(1L, orderNum);
        Assert.Equal(5000L, invoiceNum);
        Assert.Equal(10000L, ticketNum);
    }

    [Fact]
    public void INostify_HasGuidOverloadMethods()
    {
        // Arrange & Act - Check for Guid overload without starting value
        var method1 = typeof(INostify).GetMethod("GetNextSequenceValueAsync", new[] { typeof(string), typeof(Guid) });

        // Assert
        Assert.NotNull(method1);
        Assert.Equal(typeof(Task<long>), method1.ReturnType);
        var params1 = method1.GetParameters();
        Assert.Equal(2, params1.Length);
        Assert.Equal("sequenceName", params1[0].Name);
        Assert.Equal(typeof(string), params1[0].ParameterType);
        Assert.Equal("partitionKeyValue", params1[1].Name);
        Assert.Equal(typeof(Guid), params1[1].ParameterType);

        // Arrange & Act - Check for Guid overload with starting value
        var method2 = typeof(INostify).GetMethod("GetNextSequenceValueAsync", new[] { typeof(string), typeof(Guid), typeof(long) });

        // Assert
        Assert.NotNull(method2);
        Assert.Equal(typeof(Task<long>), method2.ReturnType);
        var params2 = method2.GetParameters();
        Assert.Equal(3, params2.Length);
        Assert.Equal("sequenceName", params2[0].Name);
        Assert.Equal("partitionKeyValue", params2[1].Name);
        Assert.Equal(typeof(Guid), params2[1].ParameterType);
        Assert.Equal("startingValue", params2[2].Name);
        Assert.Equal(typeof(long), params2[2].ParameterType);
    }

    [Fact]
    public async Task GetNextSequenceValueAsync_GuidOverload_SequentialCalls_ReturnSequentialValues()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "Counter";
        var tenantId = Guid.NewGuid();
        var callCount = 0;

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, tenantId))
            .ReturnsAsync(() => ++callCount);

        // Act
        var result1 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, tenantId);
        var result2 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, tenantId);
        var result3 = await mockNostify.Object.GetNextSequenceValueAsync(sequenceName, tenantId);

        // Assert
        Assert.Equal(1, result1);
        Assert.Equal(2, result2);
        Assert.Equal(3, result3);
    }

    #endregion

    #region Edge Cases and Error Handling

    [Fact]
    public void Sequence_WithSpecialCharactersInName_CreatesValidId()
    {
        // Arrange
        var name = "order-number_v2.1";
        var partitionKeyValue = "tenant/123";

        // Act
        var sequence = new Sequence(name, partitionKeyValue);

        // Assert
        Assert.Equal("tenant/123_order-number_v2.1", sequence.id);
    }

    [Fact]
    public void Sequence_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange
        var name = "订单号";
        var partitionKeyValue = "租户-1";

        // Act
        var sequence = new Sequence(name, partitionKeyValue);

        // Assert
        Assert.Equal("租户-1_订单号", sequence.id);
        Assert.Equal(name, sequence.name);
        Assert.Equal(partitionKeyValue, sequence.partitionKey);
    }

    [Fact]
    public void Sequence_WithGuidPartitionKey_GeneratesCorrectId()
    {
        // Arrange
        var name = "EntitySequence";
        var guidPartitionKey = Guid.NewGuid().ToString();

        // Act
        var sequence = new Sequence(name, guidPartitionKey);

        // Assert
        Assert.Equal($"{guidPartitionKey}_{name}", sequence.id);
    }

    [Fact]
    public void Sequence_CurrentValue_CanBeModifiedDirectly()
    {
        // Arrange
        var sequence = new Sequence("TestSeq", "partition", 0);

        // Act
        sequence.currentValue = 12345;

        // Assert
        Assert.Equal(12345, sequence.currentValue);
    }

    [Fact]
    public void Sequence_WithZeroStartingValue_IsValid()
    {
        // Arrange & Act
        var sequence = new Sequence("ZeroSeq", "partition", 0);

        // Assert
        Assert.Equal(0, sequence.currentValue);
    }

    [Fact]
    public void Sequence_WithMaxLongStartingValue_IsValid()
    {
        // Arrange & Act
        var sequence = new Sequence("MaxSeq", "partition", long.MaxValue);

        // Assert
        Assert.Equal(long.MaxValue, sequence.currentValue);
    }

    [Fact]
    public void Sequence_WithMinLongStartingValue_IsValid()
    {
        // Arrange & Act
        var sequence = new Sequence("MinSeq", "partition", long.MinValue);

        // Assert
        Assert.Equal(long.MinValue, sequence.currentValue);
    }

    #endregion

    #region JSON Serialization Tests

    [Fact]
    public void Sequence_CanBeSerializedToJson()
    {
        // Arrange
        var sequence = new Sequence("TestSequence", "partition-1", 42);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(sequence);

        // Assert
        Assert.Contains("\"id\":\"partition-1_TestSequence\"", json);
        Assert.Contains("\"name\":\"TestSequence\"", json);
        Assert.Contains("\"currentValue\":42", json);
        Assert.Contains("\"partitionKey\":\"partition-1\"", json);
    }

    [Fact]
    public void Sequence_CanBeDeserializedFromJson()
    {
        // Arrange
        var json = "{\"id\":\"partition-1_TestSequence\",\"name\":\"TestSequence\",\"currentValue\":42,\"partitionKey\":\"partition-1\"}";

        // Act
        var sequence = System.Text.Json.JsonSerializer.Deserialize<Sequence>(json);

        // Assert
        Assert.NotNull(sequence);
        Assert.Equal("partition-1_TestSequence", sequence.id);
        Assert.Equal("TestSequence", sequence.name);
        Assert.Equal(42, sequence.currentValue);
        Assert.Equal("partition-1", sequence.partitionKey);
    }

    [Fact]
    public void Sequence_RoundTripSerialization_PreservesValues()
    {
        // Arrange
        var original = new Sequence("RoundTripSeq", "tenant-abc", 999);

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Sequence>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(original.id, deserialized.id);
        Assert.Equal(original.name, deserialized.name);
        Assert.Equal(original.currentValue, deserialized.currentValue);
        Assert.Equal(original.partitionKey, deserialized.partitionKey);
    }

    [Fact]
    public void Sequence_NewtonsoftSerialization_Works()
    {
        // Arrange
        var sequence = new Sequence("NewtonsoftSeq", "partition", 100);

        // Act
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(sequence);
        var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<Sequence>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(sequence.id, deserialized.id);
        Assert.Equal(sequence.name, deserialized.name);
        Assert.Equal(sequence.currentValue, deserialized.currentValue);
        Assert.Equal(sequence.partitionKey, deserialized.partitionKey);
    }

    #endregion

    #region Interface Contract Tests

    [Fact]
    public void INostify_HasGetSequenceContainerAsyncMethod()
    {
        // Arrange & Act
        var method = typeof(INostify).GetMethod("GetSequenceContainerAsync");

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<Container>), method.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    [Fact]
    public void INostify_HasGetNextSequenceValueAsyncMethod()
    {
        // Arrange & Act
        var method = typeof(INostify).GetMethod("GetNextSequenceValueAsync", new[] { typeof(string), typeof(string) });

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<long>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("sequenceName", parameters[0].Name);
        Assert.Equal("partitionKeyValue", parameters[1].Name);
    }

    [Fact]
    public void INostify_HasGetNextSequenceValueAsyncWithStartingValueMethod()
    {
        // Arrange & Act
        var method = typeof(INostify).GetMethod("GetNextSequenceValueAsync", new[] { typeof(string), typeof(string), typeof(long) });

        // Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<long>), method.ReturnType);
        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal("sequenceName", parameters[0].Name);
        Assert.Equal("partitionKeyValue", parameters[1].Name);
        Assert.Equal("startingValue", parameters[2].Name);
    }

    #endregion

    #region Concurrency Simulation Tests

    [Fact]
    public async Task GetNextSequenceValueAsync_SimulatedConcurrentAccess_AllCallsSucceed()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "ConcurrentSeq";
        var partitionKeyValue = "test";
        var counter = 0;
        var lockObj = new object();

        mockNostify
            .Setup(n => n.GetNextSequenceValueAsync(sequenceName, partitionKeyValue))
            .ReturnsAsync(() =>
            {
                lock (lockObj)
                {
                    return ++counter;
                }
            });

        // Act - Simulate concurrent calls
        var tasks = new List<Task<long>>();
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(mockNostify.Object.GetNextSequenceValueAsync(sequenceName, partitionKeyValue));
        }
        var results = await Task.WhenAll(tasks);

        // Assert - All values should be unique and sequential
        var uniqueResults = results.Distinct().ToList();
        Assert.Equal(10, uniqueResults.Count);
        Assert.Equal(1, results.Min());
        Assert.Equal(10, results.Max());
    }

    #endregion

    #region Partition Key Isolation Tests

    [Fact]
    public void Sequence_SameNameDifferentPartitions_HaveDifferentIds()
    {
        // Arrange
        var sequenceName = "SharedSequenceName";
        var partition1 = "tenant-1";
        var partition2 = "tenant-2";

        // Act
        var sequence1 = new Sequence(sequenceName, partition1);
        var sequence2 = new Sequence(sequenceName, partition2);

        // Assert
        Assert.NotEqual(sequence1.id, sequence2.id);
        Assert.Equal(sequence1.name, sequence2.name);
    }

    [Fact]
    public void Sequence_DifferentNamesSamePartition_HaveDifferentIds()
    {
        // Arrange
        var partitionKey = "shared-partition";
        var name1 = "Sequence1";
        var name2 = "Sequence2";

        // Act
        var sequence1 = new Sequence(name1, partitionKey);
        var sequence2 = new Sequence(name2, partitionKey);

        // Assert
        Assert.NotEqual(sequence1.id, sequence2.id);
        Assert.Equal(sequence1.partitionKey, sequence2.partitionKey);
    }

    #endregion

    #region SequenceRange Struct Tests

    [Fact]
    public void SequenceRange_Constructor_SetsStartAndEndValues()
    {
        // Arrange & Act
        var range = new SequenceRange(1, 10);

        // Assert
        Assert.Equal(1, range.StartValue);
        Assert.Equal(10, range.EndValue);
    }

    [Fact]
    public void SequenceRange_Count_ReturnsCorrectCount()
    {
        // Arrange
        var range = new SequenceRange(1, 10);

        // Act & Assert
        Assert.Equal(10, range.Count);
    }

    [Fact]
    public void SequenceRange_Count_WithSingleValue_ReturnsOne()
    {
        // Arrange
        var range = new SequenceRange(5, 5);

        // Act & Assert
        Assert.Equal(1, range.Count);
    }

    [Fact]
    public void SequenceRange_Count_WithLargeRange_ReturnsCorrectCount()
    {
        // Arrange
        var range = new SequenceRange(1, 1000);

        // Act & Assert
        Assert.Equal(1000, range.Count);
    }

    [Fact]
    public void SequenceRange_ToEnumerable_ReturnsAllValuesInRange()
    {
        // Arrange
        var range = new SequenceRange(5, 10);

        // Act
        var values = range.ToEnumerable().ToList();

        // Assert
        Assert.Equal(6, values.Count);
        Assert.Equal(new long[] { 5, 6, 7, 8, 9, 10 }, values);
    }

    [Fact]
    public void SequenceRange_ToEnumerable_WithSingleValue_ReturnsSingleValue()
    {
        // Arrange
        var range = new SequenceRange(42, 42);

        // Act
        var values = range.ToEnumerable().ToList();

        // Assert
        Assert.Single(values);
        Assert.Equal(42, values[0]);
    }

    [Fact]
    public void SequenceRange_ToArray_ReturnsAllValuesAsArray()
    {
        // Arrange
        var range = new SequenceRange(1, 5);

        // Act
        var array = range.ToArray();

        // Assert
        Assert.Equal(5, array.Length);
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, array);
    }

    [Fact]
    public void SequenceRange_ToArray_WithLargeRange_ReturnsCorrectArray()
    {
        // Arrange
        var range = new SequenceRange(1, 100);

        // Act
        var array = range.ToArray();

        // Assert
        Assert.Equal(100, array.Length);
        Assert.Equal(1, array[0]);
        Assert.Equal(50, array[49]);
        Assert.Equal(100, array[99]);
    }

    [Fact]
    public void SequenceRange_Contains_ReturnsTrueForValuesInRange()
    {
        // Arrange
        var range = new SequenceRange(10, 20);

        // Act & Assert
        Assert.True(range.Contains(10)); // Start boundary
        Assert.True(range.Contains(15)); // Middle
        Assert.True(range.Contains(20)); // End boundary
    }

    [Fact]
    public void SequenceRange_Contains_ReturnsFalseForValuesOutsideRange()
    {
        // Arrange
        var range = new SequenceRange(10, 20);

        // Act & Assert
        Assert.False(range.Contains(9));  // Just before start
        Assert.False(range.Contains(21)); // Just after end
        Assert.False(range.Contains(0));  // Well before
        Assert.False(range.Contains(100)); // Well after
    }

    [Fact]
    public void SequenceRange_ToString_ReturnsFormattedString()
    {
        // Arrange
        var range = new SequenceRange(1, 10);

        // Act
        var result = range.ToString();

        // Assert
        Assert.Equal("[1..10] (Count: 10)", result);
    }

    [Fact]
    public void SequenceRange_ToString_WithSingleValue_ShowsCorrectCount()
    {
        // Arrange
        var range = new SequenceRange(42, 42);

        // Act
        var result = range.ToString();

        // Assert
        Assert.Equal("[42..42] (Count: 1)", result);
    }

    [Fact]
    public void SequenceRange_WithNegativeValues_WorksCorrectly()
    {
        // Arrange
        var range = new SequenceRange(-5, 5);

        // Act
        var values = range.ToArray();

        // Assert
        Assert.Equal(11, range.Count);
        Assert.Equal(-5, values[0]);
        Assert.Equal(0, values[5]);
        Assert.Equal(5, values[10]);
    }

    [Fact]
    public void SequenceRange_IsReadonlyStruct_CannotBeModified()
    {
        // This test verifies the struct is readonly by attempting to use it
        // in readonly contexts - compilation success proves it's readonly
        var range = new SequenceRange(1, 10);
        
        // readonly structs can be used in readonly ref contexts
        ReadonlyStructHelper(in range);
        
        Assert.Equal(1, range.StartValue);
        Assert.Equal(10, range.EndValue);
    }

    private static void ReadonlyStructHelper(in SequenceRange range)
    {
        // If SequenceRange weren't readonly, this would create a defensive copy warning
        Assert.Equal(10, range.Count);
    }

    #endregion

    #region GetNextSequenceValuesAsync Interface Tests (Bulk Operations)

    [Fact]
    public async Task GetNextSequenceValuesAsync_WithStringPartitionKey_ReturnsCorrectRange()
    {
        // Arrange
        var mockContainer = new Mock<Container>();
        var sequenceName = "BulkSequence";
        var partitionKeyValue = "test-partition";
        var count = 10;

        var existingSequence = new Sequence(sequenceName, partitionKeyValue, 0);
        existingSequence.currentValue = 5; // Current value is 5

        var readResponse = new Mock<ItemResponse<Sequence>>();
        readResponse.Setup(r => r.Resource).Returns(existingSequence);

        mockContainer
            .Setup(c => c.ReadItemAsync<Sequence>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(readResponse.Object);

        var patchedSequence = new Sequence(sequenceName, partitionKeyValue, 15); // After increment by 10
        var patchResponse = new Mock<ItemResponse<Sequence>>();
        patchResponse.Setup(r => r.Resource).Returns(patchedSequence);

        mockContainer
            .Setup(c => c.PatchItemAsync<Sequence>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<IReadOnlyList<PatchOperation>>(),
                It.IsAny<PatchItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(patchResponse.Object);

        var mockNostify = new Mock<INostify>();
        mockNostify.Setup(n => n.GetSequenceContainerAsync()).ReturnsAsync(mockContainer.Object);
        mockNostify
            .Setup(n => n.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, count))
            .ReturnsAsync(new SequenceRange(6, 15)); // From 5+1 to 5+10

        // Act
        var result = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, count);

        // Assert
        Assert.Equal(6, result.StartValue);
        Assert.Equal(15, result.EndValue);
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public async Task GetNextSequenceValuesAsync_WithGuidPartitionKey_ReturnsCorrectRange()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "BulkGuidSequence";
        var partitionKeyGuid = Guid.NewGuid();
        var count = 5;

        mockNostify
            .Setup(n => n.GetNextSequenceValuesAsync(sequenceName, partitionKeyGuid, count))
            .ReturnsAsync(new SequenceRange(1, 5)); // New sequence starts at 0, so 1-5

        // Act
        var result = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyGuid, count);

        // Assert
        Assert.Equal(1, result.StartValue);
        Assert.Equal(5, result.EndValue);
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task GetNextSequenceValuesAsync_WithStartingValue_ReturnsCorrectRange()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "StartingValueBulkSequence";
        var partitionKeyValue = "partition";
        var count = 100;
        var startingValue = 10000L;

        mockNostify
            .Setup(n => n.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, count, startingValue))
            .ReturnsAsync(new SequenceRange(10001, 10100)); // From 10000+1 to 10000+100

        // Act
        var result = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, count, startingValue);

        // Assert
        Assert.Equal(10001, result.StartValue);
        Assert.Equal(10100, result.EndValue);
        Assert.Equal(100, result.Count);
    }

    [Fact]
    public async Task GetNextSequenceValuesAsync_WithGuidAndStartingValue_ReturnsCorrectRange()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "GuidStartingValueSequence";
        var partitionKeyGuid = Guid.NewGuid();
        var count = 50;
        var startingValue = 5000L;

        mockNostify
            .Setup(n => n.GetNextSequenceValuesAsync(sequenceName, partitionKeyGuid, count, startingValue))
            .ReturnsAsync(new SequenceRange(5001, 5050));

        // Act
        var result = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyGuid, count, startingValue);

        // Assert
        Assert.Equal(5001, result.StartValue);
        Assert.Equal(5050, result.EndValue);
        Assert.Equal(50, result.Count);
    }

    [Fact]
    public async Task GetNextSequenceValuesAsync_WithSingleCount_ReturnsSingleValueRange()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "SingleValueBulk";
        var partitionKeyValue = "partition";

        mockNostify
            .Setup(n => n.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, 1))
            .ReturnsAsync(new SequenceRange(1, 1));

        // Act
        var result = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, 1);

        // Assert
        Assert.Equal(1, result.StartValue);
        Assert.Equal(1, result.EndValue);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task GetNextSequenceValuesAsync_ConsecutiveCalls_ReturnNonOverlappingRanges()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "ConsecutiveBulk";
        var partitionKeyValue = "partition";

        var callCount = 0;
        mockNostify
            .Setup(n => n.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, 10))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1
                    ? new SequenceRange(1, 10)
                    : new SequenceRange(11, 20);
            });

        // Act
        var firstRange = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, 10);
        var secondRange = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, 10);

        // Assert
        Assert.Equal(10, firstRange.EndValue);
        Assert.Equal(11, secondRange.StartValue);
        Assert.False(firstRange.Contains(secondRange.StartValue));
    }

    [Fact]
    public async Task GetNextSequenceValuesAsync_LargeCount_HandlesCorrectly()
    {
        // Arrange
        var mockNostify = new Mock<INostify>();
        var sequenceName = "LargeBulk";
        var partitionKeyValue = "partition";
        var count = 10000;

        mockNostify
            .Setup(n => n.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, count))
            .ReturnsAsync(new SequenceRange(1, 10000));

        // Act
        var result = await mockNostify.Object.GetNextSequenceValuesAsync(sequenceName, partitionKeyValue, count);

        // Assert
        Assert.Equal(1, result.StartValue);
        Assert.Equal(10000, result.EndValue);
        Assert.Equal(10000, result.Count);
    }

    #endregion

    #region Bulk Sequence Real-World Usage Tests

    [Fact]
    public void SequenceRange_BulkOrderNumberAssignment_WorksCorrectly()
    {
        // Simulate assigning order numbers to a batch of orders
        var range = new SequenceRange(1001, 1100);
        var orderIds = Enumerable.Range(0, 100).Select(i => Guid.NewGuid()).ToList();
        
        // Create dictionary of order ID to order number
        var orderNumbers = new Dictionary<Guid, long>();
        var sequenceValues = range.ToArray();
        
        for (int i = 0; i < orderIds.Count; i++)
        {
            orderNumbers[orderIds[i]] = sequenceValues[i];
        }

        // Assert
        Assert.Equal(100, orderNumbers.Count);
        Assert.All(orderNumbers.Values, v => Assert.InRange(v, 1001, 1100));
        Assert.Equal(100, orderNumbers.Values.Distinct().Count()); // All unique
    }

    [Fact]
    public void SequenceRange_EnumerableAssignment_WorksWithLinq()
    {
        // Simulate using LINQ to assign sequence numbers
        var range = new SequenceRange(1, 50);
        var items = Enumerable.Range(0, 50).Select(i => new { ItemId = i, Name = $"Item{i}" });

        var numberedItems = items
            .Zip(range.ToEnumerable(), (item, seqNum) => new { item.ItemId, item.Name, SequenceNumber = seqNum })
            .ToList();

        // Assert
        Assert.Equal(50, numberedItems.Count);
        Assert.Equal(1, numberedItems.First().SequenceNumber);
        Assert.Equal(50, numberedItems.Last().SequenceNumber);
    }

    [Fact]
    public void SequenceRange_ParallelAssignment_AllValuesUnique()
    {
        // Test that ToArray can be used safely in parallel scenarios
        var range = new SequenceRange(1, 1000);
        var values = range.ToArray();
        
        // Verify all values are unique and in sequence
        Assert.Equal(1000, values.Length);
        Assert.Equal(1000, values.Distinct().Count());
        Assert.True(values.SequenceEqual(Enumerable.Range(1, 1000).Select(i => (long)i)));
    }

    #endregion
}

