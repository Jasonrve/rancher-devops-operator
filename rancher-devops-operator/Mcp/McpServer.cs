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
    private readonly IRancherRequestAuthContext _requestAuthContext;
    private readonly ILogger<McpServer> _logger;

    public McpServer(IMcpToolCatalog catalog, IMcpToolExecutor executor, IRancherRequestAuthContext requestAuthContext, ILogger<McpServer> logger)
    {
        _catalog = catalog;
        _executor = executor;
        _requestAuthContext = requestAuthContext;
        _logger = logger;
    }

    public async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        using var _ = _requestAuthContext.Push(string.IsNullOrWhiteSpace(context.Request.Headers.Authorization.ToString())
            ? null
            : context.Request.Headers.Authorization.ToString());

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
            var response = await HandleRequestAsync(request, cancellationToken);
            return Results.Json(response, options: JsonOptions);
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

    private async Task<McpRpcResponse> HandleRequestAsync(McpRpcRequest request, CancellationToken cancellationToken)
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
                tools = _catalog.GetTools(McpRole.Viewer)
                    .Select(tool => new
                    {
                        name = tool.Name,
                        description = tool.Description,
                        inputSchema = new { type = "object", properties = new { }, additionalProperties = true },
                    })
                    .ToArray(),
            }),
            "tools/call" => await HandleToolCallAsync(request, cancellationToken),
            _ => CreateError(request.Id, -32601, $"Unknown method '{request.Method}'"),
        };
    }

    private async Task<McpRpcResponse> HandleToolCallAsync(McpRpcRequest request, CancellationToken cancellationToken)
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

        var result = await _executor.ExecuteAsync(toolName, args, McpPrincipal.AnonymousViewer(), cancellationToken);
        return new McpRpcResponse("2.0", request.Id, result);
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static McpRpcResponse CreateError(JsonElement? id, int code, string message)
        => new("2.0", id, null, new McpRpcError(code, message));
}
