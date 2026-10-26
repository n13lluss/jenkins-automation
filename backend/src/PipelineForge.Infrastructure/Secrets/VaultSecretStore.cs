using Microsoft.Extensions.Options;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.AppRole;
using VaultSharp.V1.AuthMethods.Token;

namespace PipelineForge.Infrastructure.Secrets;

/// <summary>HashiCorp Vault KV v2. References look like <c>vault:pipelineforge/{workspace}/{id}</c>.</summary>
public sealed class VaultSecretStore : ISecretStore
{
    private const string Prefix = "vault:";
    private readonly VaultOptions _options;
    private readonly IVaultClient _client;

    public VaultSecretStore(IOptions<SecretStoreOptions> options)
    {
        _options = options.Value.Vault;
        var roleId = _options.RoleId is { Length: > 0 } r ? r : ReadFile(_options.RoleIdFile);
        var secretId = _options.SecretId is { Length: > 0 } s ? s : ReadFile(_options.SecretIdFile);
        IAuthMethodInfo auth = roleId is { Length: > 0 }
            ? new AppRoleAuthMethodInfo(roleId, secretId)
            : new TokenAuthMethodInfo(_options.Token ?? throw new InvalidOperationException("Vault needs AppRole (RoleId/SecretId) or a token."));
        _client = new VaultClient(new VaultClientSettings(_options.Address, auth));
    }

    public string Name => "vault";

    private static string? ReadFile(string? path) =>
        string.IsNullOrEmpty(path) || !File.Exists(path) ? null : File.ReadAllText(path).Trim();

    public async Task<string> SetAsync(Guid workspaceId, string value, CancellationToken ct)
    {
        var path = $"{_options.PathPrefix}/{workspaceId:N}/{Guid.NewGuid():N}";
        await Retry(() => _client.V1.Secrets.KeyValue.V2.WriteSecretAsync(path, new Dictionary<string, object> { ["value"] = value }, mountPoint: _options.MountPoint));
        return Prefix + path;
    }

    public async Task<string> GetAsync(string reference, CancellationToken ct)
    {
        var path = PathOf(reference);
        var secret = await Retry(() => _client.V1.Secrets.KeyValue.V2.ReadSecretAsync(path, mountPoint: _options.MountPoint));
        return secret.Data.Data["value"]?.ToString() ?? throw new InvalidOperationException("Secret has no value.");
    }

    public async Task DeleteAsync(string reference, CancellationToken ct)
    {
        var path = PathOf(reference);
        await Retry(async () =>
        {
            await _client.V1.Secrets.KeyValue.V2.DeleteMetadataAsync(path, _options.MountPoint);
            return true;
        });
    }

    /// <summary>AppRole tokens expire; on 403 the client logs in again once.</summary>
    private async Task<T> Retry<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (VaultSharp.Core.VaultApiException e) when (e.HttpStatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _client.V1.Auth.ResetVaultToken();
            return await call();
        }
    }

    private string PathOf(string reference)
    {
        if (!reference.StartsWith(Prefix, StringComparison.Ordinal)) throw new ArgumentException("Not a Vault secret reference.");
        var path = reference[Prefix.Length..];
        var parts = path.Split('/');
        if (parts.Length != 3 || parts[0] != _options.PathPrefix || !Guid.TryParseExact(parts[1], "N", out _) || !Guid.TryParseExact(parts[2], "N", out _))
            throw new ArgumentException("Malformed Vault secret reference.");
        return path;
    }
}
