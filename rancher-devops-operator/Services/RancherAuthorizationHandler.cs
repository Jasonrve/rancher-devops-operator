using System.Net.Http.Headers;

namespace rancher_devops_operator.Services;

public sealed class RancherAuthorizationHandler : DelegatingHandler
{
    private readonly IRancherAuthService _authService;

    public RancherAuthorizationHandler(IRancherAuthService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = await _authService.GetAuthorizationHeaderAsync(cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}
