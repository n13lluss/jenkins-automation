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
        IAuthMethodInfo auth = _options.RoleId is { Length: > 0 } roleId
            ? new AppRoleAuthMethodInfo(roleId, _options.SecretId)
            : new TokenAuthMethodInfo(_options.Token ?? throw new InvalidOperationException("Vault needs AppRole (RoleId/SecretId) or a token."));
        _client = new VaultClient(new VaultClientSettings(_options.Address, auth));
    }

    public string Name => "vault";

    public async Task<string> SetAsync(Guid workspaceId, string value, CancellationToken ct)
    {
        var path = $"{_options.PathPrefix}/{workspaceId:N}/{Guid.NewGuid():N}";
        await _client.V1.Secrets.KeyValue.V2.WriteSecretAsync(path, new Dictionary<string, object> { ["value"] = value }, mountPoint: _options.MountPoint);
        return Prefix + path;
    }

    public async Task<string> GetAsync(string reference, CancellationToken ct)
    {
        var secret = await _client.V1.Secrets.KeyValue.V2.ReadSecretAsync(PathOf(reference), mountPoint: _options.MountPoint);
        return secret.Data.Data["value"]?.ToString() ?? throw new InvalidOperationException("Secret has no value.");
    }

    public Task DeleteAsync(string reference, CancellationToken ct) =>
        _client.V1.Secrets.KeyValue.V2.DeleteMetadataAsync(PathOf(reference), _options.MountPoint);

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
