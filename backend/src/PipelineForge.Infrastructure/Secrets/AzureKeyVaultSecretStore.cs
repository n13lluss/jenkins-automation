using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace PipelineForge.Infrastructure.Secrets;

/// <summary>
/// Azure Key Vault using <see cref="DefaultAzureCredential"/> (managed identity in Azure,
/// environment/CLI credentials locally). References look like <c>akv:pf-{workspace}-{id}</c>.
/// </summary>
public sealed class AzureKeyVaultSecretStore : ISecretStore
{
    private const string Prefix = "akv:";
    private readonly SecretClient _client;

    public AzureKeyVaultSecretStore(IOptions<SecretStoreOptions> options)
    {
        var uri = options.Value.AzureKeyVault.VaultUri;
        if (string.IsNullOrEmpty(uri)) throw new InvalidOperationException("SecretStore:AzureKeyVault:VaultUri is required.");
        _client = new SecretClient(new Uri(uri), new DefaultAzureCredential());
    }

    public string Name => "azure-key-vault";

    public async Task<string> SetAsync(Guid workspaceId, string value, CancellationToken ct)
    {
        var name = $"pf-{workspaceId:N}-{Guid.NewGuid():N}";
        await _client.SetSecretAsync(name, value, ct);
        return Prefix + name;
    }

    public async Task<string> GetAsync(string reference, CancellationToken ct) =>
        (await _client.GetSecretAsync(NameOf(reference), cancellationToken: ct)).Value.Value;

    public Task DeleteAsync(string reference, CancellationToken ct) =>
        _client.StartDeleteSecretAsync(NameOf(reference), ct);

    private static string NameOf(string reference)
    {
        if (!reference.StartsWith(Prefix, StringComparison.Ordinal)) throw new ArgumentException("Not a Key Vault secret reference.");
        var name = reference[Prefix.Length..];
        if (name.Length != 68 || !name.StartsWith("pf-") || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new ArgumentException("Malformed Key Vault secret reference.");
        return name;
    }
}
