using System.Threading;

namespace rancher_devops_operator.Services;

public interface IRancherRequestAuthContext
{
    string? CurrentAuthorizationHeader { get; }
    IDisposable Push(string? authorizationHeader);
}

public sealed class RancherRequestAuthContext : IRancherRequestAuthContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public string? CurrentAuthorizationHeader => Current.Value;

    public IDisposable Push(string? authorizationHeader)
    {
        var previous = Current.Value;
        Current.Value = authorizationHeader;
        return new Popper(previous);
    }

    private sealed class Popper : IDisposable
    {
        private readonly string? _previous;
        private bool _disposed;

        public Popper(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Current.Value = _previous;
            _disposed = true;
        }
    }
}
