using Grpc.Core;
using Grpc.Core.Interceptors;

namespace _GrpcServerName_.Security;

/// <summary>
/// gRPC server interceptor that validates an API key from the "x-api-key" metadata header.
/// Compares against the configured Security:ApiKey value.
/// Enabled when Security:Mode is set to "ApiKey" in appsettings.json.
/// </summary>
public class ApiKeyInterceptor : Interceptor
{
    private readonly string _expectedApiKey;
    private readonly ILogger<ApiKeyInterceptor> _logger;

    public ApiKeyInterceptor(IConfiguration configuration, ILogger<ApiKeyInterceptor> logger)
    {
        _expectedApiKey = configuration["Security:ApiKey"]
            ?? throw new InvalidOperationException("Security:ApiKey configuration is required when Security:Mode is 'ApiKey'");
        _logger = logger;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var apiKey = context.RequestHeaders
            .FirstOrDefault(h => string.Equals(h.Key, "x-api-key", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("gRPC call rejected: missing x-api-key header from {Peer}", context.Peer);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing x-api-key header"));
        }

        if (!string.Equals(apiKey, _expectedApiKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("gRPC call rejected: invalid API key from {Peer}", context.Peer);
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
        }

        return await continuation(request, context);
    }
}
