using System.Text.Json;
using System.Text.Json.Serialization;

namespace rancher_devops_operator.Mcp;

public enum McpRole
{
    Viewer = 0,
    Admin = 1,
}

public sealed record McpPrincipal(
    McpRole Role,
    bool IsAnonymous,
    string? TokenSecretName = null,
    string? TokenHash = null)
{
    public static McpPrincipal AnonymousViewer() => new(McpRole.Viewer, true);
}

public sealed record McpTokenRecord(
    string SecretName,
    string TokenHash,
    McpRole Role,
    DateTimeOffset CreatedAt);

public sealed record McpTokenCreationResult(
    string RawToken,
    string SecretName,
    McpRole Role,
    string TokenHash);

public sealed record McpToolDefinition(
    string Name,
    string Description,
    McpRole MinimumRole,
    bool ReadOnly,
    string Category,
    bool Implemented = true);

public sealed record McpRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

public sealed record McpRpcError(int Code, string Message, object? Data = null);

public sealed record McpRpcResponse(
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc,
    [property: JsonPropertyName("id")] JsonElement? Id,
    [property: JsonPropertyName("result")] object? Result = null,
    [property: JsonPropertyName("error")] McpRpcError? Error = null);
