using Grpc.Core;
using nostify;
using nostify.Grpc;

namespace _GrpcServerName_.Services;

/// <summary>
/// Unified gRPC service that handles event request calls from all backend services.
/// Routes requests to the correct Cosmos DB event store based on the service_name field
/// in the EventRequestMessage.
/// </summary>
public class UnifiedGrpcEventRequestService : EventRequestService.EventRequestServiceBase
{
    private readonly IDictionary<string, INostify> _serviceMap;
    private readonly ILogger<UnifiedGrpcEventRequestService> _logger;

    public UnifiedGrpcEventRequestService(
        IDictionary<string, INostify> serviceMap,
        ILogger<UnifiedGrpcEventRequestService> logger)
    {
        _serviceMap = serviceMap;
        _logger = logger;
    }

    public override async Task<EventResponseMessage> RequestEvents(
        EventRequestMessage request, ServerCallContext context)
    {
        var serviceName = request.ServiceName;

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            _logger.LogWarning("gRPC EventRequest rejected: empty service_name from {Peer}", context.Peer);
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "service_name is required. Specify which service's event store to query."));
        }

        if (!_serviceMap.TryGetValue(serviceName, out var nostify))
        {
            _logger.LogWarning("gRPC EventRequest rejected: unknown service '{ServiceName}' from {Peer}",
                serviceName, context.Peer);
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                $"Unknown service: '{serviceName}'. Available services: {string.Join(", ", _serviceMap.Keys)}"));
        }

        _logger.LogDebug("Processing gRPC EventRequest for service '{ServiceName}' with {IdCount} aggregate root IDs",
            serviceName, request.AggregateRootIds.Count);

        return await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(
            nostify, request, _logger);
    }
}
