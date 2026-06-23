using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using rancher_devops_operator.Services;

namespace rancher_devops_operator.Mcp;

public sealed class McpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly IMcpToolCatalog _catalog;
    private readonly IMcpToolExecutor _executor;
    private readonly IMcpTokenStore _tokenStore;
    private readonly IRancherApiService _rancherApiService;
    private readonly IRancherPassthroughTokenContext _passthroughTokenContext;
    private readonly ILogger<McpServer> _logger;

    public McpServer(
        IMcpToolCatalog catalog,
        IMcpToolExecutor executor,
        IMcpTokenStore tokenStore,
        IRancherApiService rancherApiService,
        IRancherPassthroughTokenContext passthroughTokenContext,
        ILogger<McpServer> logger)
    {
        _catalog = catalog;
        _executor = executor;
        _tokenStore = tokenStore;
        _rancherApiService = rancherApiService;
        _passthroughTokenContext = passthroughTokenContext;
        _logger = logger;
    }

    public async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        McpPrincipal principal;
        IDisposable? passthroughScope = null;
        try
        {
            (principal, passthroughScope) = await ResolvePrincipalAsync(context, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Results.Json(CreateError(null, -32001, "Unauthorized"), statusCode: StatusCodes.Status401Unauthorized, options: JsonOptions);
        }

        using (passthroughScope)
        {
            McpRpcRequest? request;
            try
            {
                request = await JsonSerializer.DeserializeAsync<McpRpcRequest>(context.Request.Body, JsonOptions, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Invalid JSON-RPC payload");
                return Results.Json(CreateError(null, -32700, "Parse error"), statusCode: StatusCodes.Status400BadRequest, options: JsonOptions);
            }

            if (request is null)
            {
                return Results.Json(CreateError(null, -32600, "Invalid request"), statusCode: StatusCodes.Status400BadRequest, options: JsonOptions);
            }

            if (!string.Equals(request.Jsonrpc, "2.0", StringComparison.Ordinal))
            {
                return Results.Json(CreateError(request.Id, -32600, "Invalid request"), statusCode: StatusCodes.Status400BadRequest, options: JsonOptions);
            }

            try
            {
                var response = await HandleRequestAsync(request, principal, cancellationToken);
                return Results.Json(response, options: JsonOptions);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning("Forbidden MCP tool call: {Message}", ex.Message);
                return Results.Json(CreateError(request.Id, -32003, "Forbidden"), statusCode: StatusCodes.Status403Forbidden, options: JsonOptions);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(CreateError(request.Id, -32602, ex.Message), statusCode: StatusCodes.Status400BadRequest, options: JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled MCP error");
                return Results.Json(CreateError(request.Id, -32603, "Internal error"), statusCode: StatusCodes.Status500InternalServerError, options: JsonOptions);
            }
        }
    }

    private async Task<McpRpcResponse> HandleRequestAsync(McpRpcRequest request, McpPrincipal principal, CancellationToken cancellationToken)
    {
        return request.Method switch
        {
            "initialize" => new McpRpcResponse("2.0", request.Id, new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new { name = "rancher-devops-operator", version = "1.0.0-mcp" },
                capabilities = new { tools = new { } },
            }),
            "tools/list" => new McpRpcResponse("2.0", request.Id, new
            {
                tools = _catalog.GetTools(principal.Role)
                    .Select(tool => new
                    {
                        name = tool.Name,
                        description = tool.Description + (tool.Implemented ? string.Empty : " (not implemented in this build)") ,
                        inputSchema = new { type = "object", properties = new { }, additionalProperties = true },
                    })
                    .ToArray(),
            }),
            "tools/call" => await HandleToolCallAsync(request, principal, cancellationToken),
            _ => CreateError(request.Id, -32601, $"Unknown method '{request.Method}'"),
        };
    }

    private async Task<McpRpcResponse> HandleToolCallAsync(McpRpcRequest request, McpPrincipal principal, CancellationToken cancellationToken)
    {
        if (request.Params is null || request.Params.Value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("tools/call requires params with name and optional arguments");
        }

        var toolName = GetString(request.Params.Value, "name") ?? throw new ArgumentException("tools/call missing name");
        JsonElement? args = null;
        if (request.Params.Value.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
        {
            args = arguments;
        }

        var result = await _executor.ExecuteAsync(toolName, args, principal, cancellationToken);
        return new McpRpcResponse("2.0", request.Id, result);
    }

    private async Task<(McpPrincipal Principal, IDisposable Scope)> ResolvePrincipalAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            throw new UnauthorizedAccessException("Missing bearer token");
        }

        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Unsupported authorization scheme");
        }

        var rawToken = authHeader[7..].Trim();
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            throw new UnauthorizedAccessException("Missing bearer token");
        }

        var scope = _passthroughTokenContext.UseToken(rawToken);
        try
        {
            await _rancherApiService.InvokeRawAsync(HttpMethod.Get, "/v3", null, cancellationToken);
            return (new McpPrincipal(McpRole.Admin, false), scope);
        }
        catch (HttpRequestException ex) when (IsUnauthorized(ex))
        {
            scope.Dispose();
            throw new UnauthorizedAccessException("Invalid Rancher token", ex);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static bool IsUnauthorized(HttpRequestException ex)
        => ex.Message.Contains("status 401", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("status 403", StringComparison.OrdinalIgnoreCase);

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static McpRpcResponse CreateError(JsonElement? id, int code, string message)
        => new("2.0", id, null, new McpRpcError(code, message));
}
