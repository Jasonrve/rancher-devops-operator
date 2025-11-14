# Rancher Authentication Guide

This guide explains the authentication options available in the Rancher DevOps Operator.

## Overview

The operator supports **two authentication methods** for connecting to the Rancher API:

1. **Static API Token** (Recommended for production)
2. **Username/Password** (Automatic token management)

## Authentication Methods

### Method 1: Static API Token

This is the **recommended approach for production** deployments.

#### How it works:
- You create a long-lived API token in Rancher
- The operator uses this token for all API calls
- No token rotation or expiry management needed
- Simplest and most reliable method

#### Creating a Rancher API Token:
1. Log into Rancher UI
2. Click on your user icon → API & Keys
3. Click "Add Key"
4. Set description: `rancher-devops-operator`
5. Set expiration (or no expiration for service accounts)
6. Click "Create"
7. Copy the token (format: `token-xxxxx:xxxxxxxxxxxxxxxxxx`)

#### Configuration:

**Helm values.yaml:**
```yaml
rancher:
  url: "https://rancher.example.com"
  token: "token-xxxxx:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
  allowInsecureSsl: false
```

**Environment variables:**
```bash
export Rancher__Url="https://rancher.example.com"
export Rancher__Token="token-xxxxx:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
export Rancher__AllowInsecureSsl=false
```

**Kubernetes Secret:**
```bash
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=token=token-xxxxx:xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
```

Then in Helm values:
```yaml
rancher:
  url: "https://rancher.example.com"
  existingSecret: "rancher-creds"
  existingSecretTokenKey: "token"
  allowInsecureSsl: false
```

#### Pros:
- ✅ Simple and reliable
- ✅ No additional API calls needed
- ✅ Works with service accounts
- ✅ No token expiry (if configured)
- ✅ Best for production

#### Cons:
- ❌ Manual token creation required
- ❌ Token rotation requires manual update

---

### Method 2: Username/Password with Automatic Token Management

This method automatically creates and manages API tokens.

#### How it works:
- You provide Rancher username and password
- The operator calls the Rancher login API to create a token
- Tokens are created with 12-hour TTL
- Tokens are cached in memory and automatically renewed when expired
- Thread-safe implementation prevents concurrent token creation

#### Configuration:

**Helm values.yaml:**
```yaml
rancher:
  url: "https://rancher.example.com"
  username: "admin"
  password: "your-secure-password"
  allowInsecureSsl: false
```

**Environment variables:**
```bash
export Rancher__Url="https://rancher.example.com"
export Rancher__Username="admin"
export Rancher__Password="your-secure-password"
export Rancher__AllowInsecureSsl=false
```

**Kubernetes Secret:**
```bash
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=username=admin \
  --from-literal=password=your-secure-password
```

Then in Helm values:
```yaml
rancher:
  url: "https://rancher.example.com"
  existingSecret: "rancher-creds"
  existingSecretUsernameKey: "username"
  existingSecretPasswordKey: "password"
  allowInsecureSsl: false
```

#### Token Management Features:
- **Automatic Creation**: First API call triggers token creation
- **Caching**: Token stored in memory for reuse
- **Expiry Tracking**: Token expiry time tracked (12 hours)
- **Auto-Renewal**: New token created automatically when expired
- **Thread Safety**: Semaphore ensures only one token creation at a time
- **Token Description**: Auto-generated with timestamp

#### Pros:
- ✅ No manual token creation needed
- ✅ Automatic token rotation
- ✅ Good for development environments
- ✅ Works with regular user accounts

#### Cons:
- ❌ Additional API call on first use and token renewal
- ❌ Requires storing password in configuration
- ❌ Token is not persisted (recreated on pod restart)
- ❌ Requires user account (not service account)

---

## Security Best Practices

### 1. Use Kubernetes Secrets (Production)

**Never** store credentials in Helm values files or version control. Always use Kubernetes secrets:

```bash
# Create secret
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=token=your-token-here

# Or for username/password
kubectl create secret generic rancher-creds \
  --namespace rancher-devops-system \
  --from-literal=username=admin \
  --from-literal=password=your-password

# Reference in Helm
helm install rancher-devops-operator ./helm/rancher-devops-operator \
  --namespace rancher-devops-system \
  --set rancher.url="https://rancher.example.com" \
  --set rancher.existingSecret="rancher-creds"
```

### 2. Use RBAC and Service Accounts

Create a dedicated service account in Rancher with minimal required permissions:
- Manage projects
- Manage namespaces
- Manage project role bindings
- View clusters

### 3. Enable SSL/TLS

Always use HTTPS with valid certificates in production. Only use `allowInsecureSsl: true` for development:

```yaml
rancher:
  url: "https://rancher.example.com"  # Always use https://
  allowInsecureSsl: false              # Never true in production
```

### 4. Token Rotation

For long-running deployments:
- **Static Token**: Rotate manually by updating the secret
- **Username/Password**: Automatic rotation every 12 hours

### 5. Audit and Monitoring

Monitor operator logs for authentication events:
```bash
kubectl logs -n rancher-devops-system deployment/rancher-devops-operator -f | grep -i "auth\|token\|login"
```

---

## Troubleshooting

### "Rancher authentication not configured"

**Cause**: Neither token nor username/password provided

**Solution**: Provide either:
- `Rancher__Token` environment variable
- OR both `Rancher__Username` and `Rancher__Password`

### "Failed to login to Rancher: 401"

**Cause**: Invalid username or password

**Solution**: 
- Verify credentials are correct
- Check user has login permissions
- Ensure user account is not locked

### "Failed to login to Rancher: certificate verify failed"

**Cause**: Self-signed SSL certificate

**Solution**: 
- Add valid SSL certificate to Rancher
- OR for development: set `allowInsecureSsl: true`

### Token creation creates many tokens in Rancher

**Cause**: Pod is restarting frequently or multiple replicas

**Solution**:
- Tokens are not persisted, so each pod restart creates a new one
- Old tokens expire after 12 hours automatically
- Use static token method if this is a concern
- Ensure pod stability (check resource limits, health checks)

---

## Implementation Details

### Token Caching Architecture

```
┌─────────────────────────────────────────────────────┐
│ RancherAuthService                                  │
│                                                     │
│  ┌──────────────────────────────────────────────┐  │
│  │ GetOrCreateTokenAsync()                      │  │
│  │                                              │  │
│  │  1. Static token? → Return immediately      │  │
│  │  2. Cached token valid? → Return cached     │  │
│  │  3. Acquire semaphore lock                  │  │
│  │  4. Double-check cache (race condition)     │  │
│  │  5. Call Rancher login API                  │  │
│  │  6. Cache new token + expiry                │  │
│  │  7. Release semaphore                       │  │
│  │  8. Return token                            │  │
│  └──────────────────────────────────────────────┘  │
│                                                     │
│  Cache:                                             │
│  • _cachedToken (string)                           │
│  • _tokenExpiry (DateTime)                         │
│  • _tokenLock (SemaphoreSlim)                      │
└─────────────────────────────────────────────────────┘
```

### Thread Safety

- Uses `SemaphoreSlim` for lock-free concurrent access
- Double-check locking pattern prevents race conditions
- Only one token creation API call at a time
- Multiple threads can read cached token simultaneously

---

## Examples

See the `helm/rancher-devops-operator/examples/` directory for complete examples:

- `values-token.yaml` - Static token authentication
- `values-username-password.yaml` - Username/password authentication
- `values-existing-secret.yaml` - Using Kubernetes secrets

## Recommendations

| Environment | Method | Reason |
|-------------|--------|--------|
| **Production** | Static Token + K8s Secret | Most reliable, no automatic API calls, best security |
| **Staging** | Static Token + K8s Secret | Same as production |
| **Development** | Username/Password | Easier setup, automatic rotation |
| **CI/CD** | Static Token | Predictable, no expiry surprises |
| **Demo** | Username/Password | Quick setup, no manual token creation |

---

## Questions?

For issues or questions, please open a GitHub issue or check the [DOCUMENTATION.md](DOCUMENTATION.md) file.
