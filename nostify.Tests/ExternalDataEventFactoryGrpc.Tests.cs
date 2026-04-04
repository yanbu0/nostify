using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Moq;
using nostify;
using Xunit;
using Newtonsoft.Json.Linq;

namespace nostify.Tests;

/// <summary>
/// Tests for gRPC-related methods on <see cref="ExternalDataEventFactory{P}"/>.
/// </summary>
public class ExternalDataEventFactoryGrpcTests
{
    private readonly Mock<INostify> _mockNostify;
    private readonly List<FactoryTestProjection> _testProjections;

    public ExternalDataEventFactoryGrpcTests()
    {
        _mockNostify = new Mock<INostify>();

        _testProjections = new List<FactoryTestProjection>
        {
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Projection1",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
                externalId = Guid.NewGuid(),
                anotherExternalId = Guid.NewGuid()
            },
            new FactoryTestProjection
            {
                id = Guid.NewGuid(),
                name = "Projection2",
                siteId = Guid.NewGuid(),
                ownerId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                tagIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() },
                externalId = Guid.NewGuid(),
                anotherExternalId = Guid.NewGuid()
            }
        };
    }

    #region AddGrpcEventRequestors / WithGrpcEventRequestor Tests

    [Fact]
    public void AddGrpcEventRequestors_AddsRequestors()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var requestor = new GrpcEventRequester<FactoryTestProjection>(
            "https://localhost:5001",
            (Func<FactoryTestProjection, Guid?>)(p => p.externalId));

        // Act
        factory.AddGrpcEventRequestors(requestor);

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithGrpcEventRequestor_NullableGuidSelector_ReturnsThis()
    {
        // Arrange
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Act
        var result = factory.WithGrpcEventRequestor("https://localhost:5001", (Func<FactoryTestProjection, Guid?>)(p => p.externalId));

        // Assert
        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_NonNullableGuidSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", (Func<FactoryTestProjection, Guid>)(p => p.siteId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_NullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001",
            (Func<FactoryTestProjection, List<Guid?>>)(p => p.tagIds.Cast<Guid?>().ToList()));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_NonNullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001",
            (Func<FactoryTestProjection, List<Guid>>)(p => p.tagIds));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_MixedNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001",
            new Func<FactoryTestProjection, Guid?>[] { p => p.externalId },
            new Func<FactoryTestProjection, List<Guid?>>[] { p => p.tagIds.Cast<Guid?>().ToList() });

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_MixedNonNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001",
            new Func<FactoryTestProjection, Guid>[] { p => p.siteId },
            new Func<FactoryTestProjection, List<Guid>>[] { p => p.tagIds });

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_CanCombineWithHttpAndKafkaRequestors()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        // Act - all three transport types
        factory
            .WithEventRequestor("https://http-service.com/events", p => p.externalId)
            .WithAsyncEventRequestor("KafkaService", p => p.anotherExternalId)
            .WithGrpcEventRequestor("https://grpc-service:5001", (Func<FactoryTestProjection, Guid>)(p => p.siteId));

        // Assert
        Assert.NotNull(factory);
    }

    [Fact]
    public void WithGrpcEventRequestor_MultipleGrpcRequestors_AllAdded()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        factory
            .WithGrpcEventRequestor("https://service1:5001", (Func<FactoryTestProjection, Guid?>)(p => p.externalId))
            .WithGrpcEventRequestor("https://service2:5002", (Func<FactoryTestProjection, Guid?>)(p => p.anotherExternalId));

        Assert.NotNull(factory);
    }

    #endregion

    #region AddDependantGrpcEventRequestors / WithDependantGrpcEventRequestor Tests

    [Fact]
    public void AddDependantGrpcEventRequestors_AddsRequestors()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var requestor = new GrpcEventRequester<FactoryTestProjection>(
            "https://localhost:5001",
            (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId));

        factory.AddDependantGrpcEventRequestors(requestor);

        Assert.NotNull(factory);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_NullableGuidSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001",
            (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_NonNullableGuidSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001",
            (Func<FactoryTestProjection, Guid>)(p => p.dependentId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_NullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001",
            (Func<FactoryTestProjection, List<Guid?>>)(p => p.dependentListIds.Cast<Guid?>().ToList()));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_NonNullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001",
            (Func<FactoryTestProjection, List<Guid>>)(p => p.dependentListIds));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_MixedNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001",
            new Func<FactoryTestProjection, Guid?>[] { p => p.dependentExternalId },
            new Func<FactoryTestProjection, List<Guid?>>[] { p => p.dependentListIds.Cast<Guid?>().ToList() });

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_MixedNonNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001",
            new Func<FactoryTestProjection, Guid>[] { p => p.dependentId },
            new Func<FactoryTestProjection, List<Guid>>[] { p => p.dependentListIds });

        Assert.Same(factory, result);
    }

    #endregion

    #region Full Fluent Chaining with gRPC

    [Fact]
    public void FluentChaining_AllGrpcMethodsReturnThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory
            .WithGrpcEventRequestor("https://service1:5001", (Func<FactoryTestProjection, Guid?>)(p => p.externalId))
            .WithGrpcEventRequestor("https://service2:5002", (Func<FactoryTestProjection, Guid>)(p => p.siteId))
            .WithGrpcEventRequestor("https://service3:5003", (Func<FactoryTestProjection, List<Guid?>>)(p => p.tagIds.Cast<Guid?>().ToList()))
            .WithGrpcEventRequestor("https://service4:5004", (Func<FactoryTestProjection, List<Guid>>)(p => p.tagIds))
            .WithDependantGrpcEventRequestor("https://dep1:5005", (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId))
            .WithDependantGrpcEventRequestor("https://dep2:5006", (Func<FactoryTestProjection, Guid>)(p => p.dependentId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void FluentChaining_AllTransportTypesCombined_ReturnsThis()
    {
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        var result = factory
            // Same service local
            .WithSameServiceIdSelectors(p => p.siteId)
            // HTTP external
            .WithEventRequestor("https://http-service.com/events", p => p.externalId)
            // Kafka external
            .WithAsyncEventRequestor("KafkaService", p => p.anotherExternalId)
            // gRPC external
            .WithGrpcEventRequestor("https://grpc-service:5001", (Func<FactoryTestProjection, Guid>)(p => p.ownerId))
            // gRPC dependant
            .WithDependantGrpcEventRequestor("https://grpc-dep:5002", (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId));

        Assert.Same(factory, result);
    }

    #endregion

    #region WithGrpcEventRequestor (serviceName + authToken) Tests

    [Fact]
    public void WithGrpcEventRequestor_ServiceNameAuthToken_NullableGuidSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, Guid?>)(p => p.externalId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_ServiceNameAuthToken_NonNullableGuidSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, Guid>)(p => p.siteId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_ServiceNameAuthToken_NullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, List<Guid?>>)(p => p.tagIds.Cast<Guid?>().ToList()));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_ServiceNameAuthToken_NonNullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, List<Guid>>)(p => p.tagIds));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_ServiceNameAuthToken_MixedNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            new Func<FactoryTestProjection, Guid?>[] { p => p.externalId },
            new Func<FactoryTestProjection, List<Guid?>>[] { p => p.tagIds.Cast<Guid?>().ToList() });

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_ServiceNameAuthToken_MixedNonNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            new Func<FactoryTestProjection, Guid>[] { p => p.siteId },
            new Func<FactoryTestProjection, List<Guid>>[] { p => p.tagIds });

        Assert.Same(factory, result);
    }

    #endregion

    #region WithDependantGrpcEventRequestor (serviceName + authToken) Tests

    [Fact]
    public void WithDependantGrpcEventRequestor_ServiceNameAuthToken_NullableGuidSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_ServiceNameAuthToken_NonNullableGuidSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, Guid>)(p => p.dependentId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_ServiceNameAuthToken_NullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, List<Guid?>>)(p => p.dependentListIds.Cast<Guid?>().ToList()));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_ServiceNameAuthToken_NonNullableListSelector_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            (Func<FactoryTestProjection, List<Guid>>)(p => p.dependentListIds));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_ServiceNameAuthToken_MixedNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            new Func<FactoryTestProjection, Guid?>[] { p => p.dependentExternalId },
            new Func<FactoryTestProjection, List<Guid?>>[] { p => p.dependentListIds.Cast<Guid?>().ToList() });

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithDependantGrpcEventRequestor_ServiceNameAuthToken_MixedNonNullableSelectors_ReturnsThis()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithDependantGrpcEventRequestor("https://localhost:5001", "MyService", "my-token",
            new Func<FactoryTestProjection, Guid>[] { p => p.dependentId },
            new Func<FactoryTestProjection, List<Guid>>[] { p => p.dependentListIds });

        Assert.Same(factory, result);
    }

    #endregion

    #region Edge Case Tests (serviceName + authToken)

    [Fact]
    public void WithGrpcEventRequestor_NullAuthToken_DefaultsToEmptyString()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        // Should not throw — null authToken is coerced to ""
        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", null,
            (Func<FactoryTestProjection, Guid?>)(p => p.externalId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_EmptyAuthToken_AcceptedSilently()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory.WithGrpcEventRequestor("https://localhost:5001", "MyService", "",
            (Func<FactoryTestProjection, Guid?>)(p => p.externalId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_MultipleChained_AllAccepted()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory
            .WithGrpcEventRequestor("https://service1:5001", "Svc1", "token1",
                (Func<FactoryTestProjection, Guid?>)(p => p.externalId))
            .WithGrpcEventRequestor("https://service2:5002", "Svc2", "token2",
                (Func<FactoryTestProjection, Guid>)(p => p.siteId))
            .WithDependantGrpcEventRequestor("https://dep1:5003", "DepSvc", "dep-token",
                (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void WithGrpcEventRequestor_MixOldAndNewOverloads_AllAccepted()
    {
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections);

        var result = factory
            // Old overload (address-only, no serviceName/authToken)
            .WithGrpcEventRequestor("https://legacy:5001", (Func<FactoryTestProjection, Guid?>)(p => p.externalId))
            // New overload (with serviceName + authToken)
            .WithGrpcEventRequestor("https://modern:5002", "ModernService", "bearer-xyz",
                (Func<FactoryTestProjection, Guid>)(p => p.siteId))
            // Old dependant overload
            .WithDependantGrpcEventRequestor("https://dep-legacy:5003", (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId))
            // New dependant overload
            .WithDependantGrpcEventRequestor("https://dep-modern:5004", "DepService", "dep-token",
                (Func<FactoryTestProjection, Guid>)(p => p.dependentId));

        Assert.Same(factory, result);
    }

    [Fact]
    public void FluentChaining_AllTransportTypes_WithServiceNameAuthToken_ReturnsThis()
    {
        using var httpClient = new HttpClient();
        var factory = new ExternalDataEventFactory<FactoryTestProjection>(
            _mockNostify.Object,
            _testProjections,
            httpClient);

        var result = factory
            .WithSameServiceIdSelectors(p => p.siteId)
            .WithEventRequestor("https://http-service.com/events", p => p.externalId)
            .WithAsyncEventRequestor("KafkaService", p => p.anotherExternalId)
            .WithGrpcEventRequestor("https://grpc:5001", "GrpcSvc", "grpc-token",
                (Func<FactoryTestProjection, Guid>)(p => p.ownerId))
            .WithDependantGrpcEventRequestor("https://dep-grpc:5002", "DepGrpc", "dep-grpc-token",
                (Func<FactoryTestProjection, Guid?>)(p => p.dependentExternalId));

        Assert.Same(factory, result);
    }

    #endregion
}
