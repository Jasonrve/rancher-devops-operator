using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace rancher_devops_operator.Services;

public interface IRancherAuthService
{
    Task<AuthenticationHeaderValue> GetAuthorizationHeaderAsync(CancellationToken cancellationToken);
    void ConfigureHttpClient(HttpClient httpClient);
}

public class RancherAuthService : IRancherAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RancherAuthService> _logger;
    private readonly string _rancherUrl;
    private readonly string? _token;
    private readonly string? _username;
    private readonly string? _password;
    private readonly IRancherRequestAuthContext _requestAuthContext;
    
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public RancherAuthService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IRancherRequestAuthContext requestAuthContext,
        ILogger<RancherAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _requestAuthContext = requestAuthContext;
        _logger = logger;
        _rancherUrl = configuration["Rancher:Url"] ?? "https://rancher.local";
        _token = configuration["Rancher:Token"];
        _username = configuration["Rancher:Username"];
        _password = configuration["Rancher:Password"];
    }

    public async Task<AuthenticationHeaderValue> GetAuthorizationHeaderAsync(CancellationToken cancellationToken)
    {
        var passthrough = _requestAuthContext.CurrentAuthorizationHeader;
        if (!string.IsNullOrWhiteSpace(passthrough))
        {
            return AuthenticationHeaderValue.Parse(passthrough);
        }

        if (!string.IsNullOrEmpty(_token))
        {
            _logger.LogDebug("Using static token for authentication");
            return new AuthenticationHeaderValue("Bearer", _token);
        }

        if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
        {
            _logger.LogDebug("Using cached token");
            return new AuthenticationHeaderValue("Bearer", _cachedToken);
        }

        if (string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password))
        {
            throw new InvalidOperationException(
                "Rancher authentication not configured. Provide either a pass-through Authorization header, Token, or Username/Password");
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return new AuthenticationHeaderValue("Bearer", _cachedToken);
            }

            _logger.LogInformation("Creating new Rancher API token");
            var newToken = await CreateTokenAsync(cancellationToken);
            _cachedToken = newToken;
            _tokenExpiry = DateTime.UtcNow.AddHours(12);
            MetricsService.TokensCreated.Inc();
            return new AuthenticationHeaderValue("Bearer", newToken);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public void ConfigureHttpClient(HttpClient httpClient)
    {
        httpClient.BaseAddress = new Uri(_rancherUrl);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private async Task<string> CreateTokenAsync(CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("RancherAuth");
        httpClient.BaseAddress = new Uri(_rancherUrl);

        try
        {
            // First, login to get a token
            var loginRequest = new LoginRequest
            {
                Username = _username,
                Password = _password,
                Description = $"rancher-devops-operator-{DateTime.UtcNow:yyyyMMddHHmmss}",
                Ttl = 43200000 // 12 hours in milliseconds
            };

            var loginJson = JsonSerializer.Serialize(loginRequest, RancherJsonSerializerContext.Default.LoginRequest);
            var content = new StringContent(loginJson, Encoding.UTF8, "application/json");

            // Use basic auth for login
            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_username}:{_password}"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

            var response = await httpClient.PostAsync("/v3-public/localProviders/local?action=login", content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to login to Rancher: {StatusCode} - {Error}", response.StatusCode, error);
                throw new HttpRequestException($"Failed to login to Rancher: {response.StatusCode}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var loginResponse = JsonSerializer.Deserialize(responseJson, RancherJsonSerializerContext.Default.LoginResponse);

            if (loginResponse?.Token == null)
            {
                throw new InvalidOperationException("Login response did not contain a token");
            }

            _logger.LogInformation("Successfully created Rancher API token");
            return loginResponse.Token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Rancher token");
            throw;
        }
    }
}

// Models for login
public class LoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Description { get; set; }
    public int Ttl { get; set; }
}

public class LoginResponse
{
    public string? Token { get; set; }
    public string? Type { get; set; }
    public string? UserId { get; set; }
}
