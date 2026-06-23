namespace rancher_devops_operator.Services;

public interface IRancherPassthroughTokenContext
{
    bool TryGetToken(out string token);
    IDisposable UseToken(string token);
}

public sealed class RancherPassthroughTokenContext : IRancherPassthroughTokenContext
{
    private readonly AsyncLocal<string?> _currentToken = new();

    public bool TryGetToken(out string token)
    {
        token = _currentToken.Value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(token);
    }

    public IDisposable UseToken(string token)
    {
        var previous = _currentToken.Value;
        _currentToken.Value = token;
        return new TokenScope(() => _currentToken.Value = previous);
    }

    private sealed class TokenScope(Action release) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                release();
            }
        }
    }
}
