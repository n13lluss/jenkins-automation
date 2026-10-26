namespace PipelineForge.Infrastructure.Secrets;

/// <summary>
/// External secret storage. The database keeps only the opaque reference returned by
/// <see cref="SetAsync"/>; secret names are generated here, never taken from user input.
/// </summary>
public interface ISecretStore
{
    string Name { get; }
    Task<string> SetAsync(Guid workspaceId, string value, CancellationToken ct);
    Task<string> GetAsync(string reference, CancellationToken ct);
    Task DeleteAsync(string reference, CancellationToken ct);
}

public sealed class SecretStoreOptions
{
    /// <summary>"vault" or "azure-key-vault".</summary>
    public string Provider { get; set; } = "vault";
    public VaultOptions Vault { get; set; } = new();
    public AzureKeyVaultOptions AzureKeyVault { get; set; } = new();
}

public sealed class VaultOptions
{
    public string Address { get; set; } = "http://vault:8200";
    public string MountPoint { get; set; } = "secret";
    public string PathPrefix { get; set; } = "pipelineforge";
    /// <summary>AppRole is preferred; a token is accepted for local development.</summary>
    public string? RoleId { get; set; }
    public string? SecretId { get; set; }
    /// <summary>Files holding the AppRole credentials, e.g. written by an init container or a secrets mount.</summary>
    public string? RoleIdFile { get; set; }
    public string? SecretIdFile { get; set; }
    public string? Token { get; set; }
}

public sealed class AzureKeyVaultOptions
{
    public string VaultUri { get; set; } = "";
}
