
using Microsoft.Extensions.Logging;
using nostify;
using nostify.Grpc;
using Grpc.Core;

namespace _ReplaceMe__Service;

/// <summary>
/// gRPC service implementation that handles incoming <see cref="EventRequestMessage"/> requests.
/// Queries the event store for the requested aggregate root IDs and returns the matching events.
/// This is the gRPC equivalent of <see cref="EventRequest"/> (HTTP) and <see cref="AsyncEventRequestHandler"/> (Kafka).
/// </summary>
/// <remarks>
/// To host this service, register it in your ASP.NET Core pipeline:
/// <code>
/// builder.Services.AddGrpc();
/// // ... build app ...
/// app.MapGrpcService&lt;GrpcEventRequestService&gt;();
/// </code>
/// </remarks>
public class GrpcEventRequestService : EventRequestService.EventRequestServiceBase
{
    private readonly INostify _nostify;
    private readonly ILogger<GrpcEventRequestService> _logger;

    public GrpcEventRequestService(INostify nostify, ILogger<GrpcEventRequestService> logger)
    {
        this._nostify = nostify;
        this._logger = logger;
    }

    /// <summary>
    /// Handles a gRPC RequestEvents call by delegating to <see cref="DefaultEventRequestHandlers.HandleGrpcEventRequestAsync"/>.
    /// </summary>
    public override async Task<EventResponseMessage> RequestEvents(EventRequestMessage request, ServerCallContext context)
    {
        return await DefaultEventRequestHandlers.HandleGrpcEventRequestAsync(_nostify, request, _logger);
    }
}
