using System.Data.Common;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace Stocky.Web.Mvc.Internal;

/// <summary>
/// Mirrors the SQL auth setup in <c>src/Api/Program.cs</c>. Builds a
/// <see cref="TokenCredential"/> for SQL (SP cert preferred, MI fallback) and
/// pre-acquires a token at startup so EF Core's retry storm during ACA cold
/// start doesn't hammer IMDS. The token is then injected per-connection by
/// <see cref="SqlTokenInterceptor"/>.
/// </summary>
public static class SqlAuthSetup
{
    public static void Register(WebApplicationBuilder builder, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var azureClientId = builder.Configuration["AZURE_CLIENT_ID"];
        var sqlMiClientId = builder.Configuration["Sql:ManagedIdentityClientId"] ?? azureClientId;
        var spClientId = builder.Configuration["Sql:SpClientId"];
        var spTenantId = builder.Configuration["Sql:TenantId"];
        var kvUri = builder.Configuration["KeyVaultUri"];
        var certBase64 = builder.Configuration["Sql:CertBase64"];

        TokenCredential sqlCredential;
        if (!string.IsNullOrWhiteSpace(spClientId) && !string.IsNullOrWhiteSpace(spTenantId)
            && (!string.IsNullOrWhiteSpace(certBase64) || !string.IsNullOrWhiteSpace(kvUri)))
        {
            byte[] certBytes;
            if (!string.IsNullOrWhiteSpace(certBase64))
            {
                Console.WriteLine("Web.Mvc SQL auth: loading SP cert from Sql__CertBase64 env var.");
                certBytes = Convert.FromBase64String(certBase64);
            }
            else
            {
                Console.WriteLine("Web.Mvc SQL auth: fetching SP cert from Key Vault.");
                TokenCredential kvCred = string.IsNullOrWhiteSpace(azureClientId)
                    ? new DefaultAzureCredential()
                    : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(azureClientId));
                var secretClient = new SecretClient(new Uri(kvUri!), kvCred);
                KeyVaultSecret certSecret = null!;
                for (var attempt = 1; attempt <= 10; attempt++)
                {
                    try { certSecret = secretClient.GetSecret("stocky-api-sql-cert"); break; }
                    catch (Exception ex) when (attempt < 10)
                    {
                        Console.WriteLine($"KV/IMDS attempt {attempt}/10 failed: {ex.Message}. Retrying in 10s...");
                        Thread.Sleep(10_000);
                    }
                }
                certBytes = Convert.FromBase64String(certSecret.Value);
            }
            var certificate = X509CertificateLoader.LoadPkcs12(
                certBytes, (string?)null,
                X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
            sqlCredential = new ClientCertificateCredential(spTenantId, spClientId, certificate);
        }
        else
        {
            sqlCredential = string.IsNullOrWhiteSpace(sqlMiClientId)
                ? new DefaultAzureCredential()
                : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(sqlMiClientId));
        }

        builder.Services.AddSingleton<TokenCredential>(sqlCredential);
        builder.Services.AddSingleton<SqlTokenInterceptor>();
    }

    public static string StripAuthFromConnectionString(string connectionString)
    {
        var csb = new SqlConnectionStringBuilder(connectionString)
        {
            Authentication = SqlAuthenticationMethod.NotSpecified
        };
        csb.Remove("User ID");
        return csb.ConnectionString;
    }

    public static async Task WarmupAsync(WebApplication app)
    {
        var cred = app.Services.GetService<TokenCredential>();
        if (cred is null) return;
        Console.WriteLine("Web.Mvc: warming up SQL credential via IMDS...");
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            try
            {
                await cred.GetTokenAsync(
                    new TokenRequestContext(["https://database.windows.net/.default"]),
                    CancellationToken.None);
                Console.WriteLine($"Web.Mvc: SQL credential warm-up complete (attempt {attempt}/12).");
                return;
            }
            catch (Exception ex) when (attempt < 12)
            {
                Console.WriteLine($"Web.Mvc: SQL token attempt {attempt}/12 failed: {ex.GetType().Name}: {ex.Message}");
                for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                {
                    Console.WriteLine($"  inner: {inner.GetType().Name}: {inner.Message}");
                }
                await Task.Delay(10_000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Web.Mvc: SQL token warm-up exhausted: {ex}. Starting anyway.");
            }
        }
    }
}

/// <summary>
/// Per-connection SQL bearer token injector. MSAL caches the token (~55 min)
/// so only the first call hits IMDS.
/// </summary>
public sealed class SqlTokenInterceptor(TokenCredential credential) : DbConnectionInterceptor
{
    private static readonly string[] SqlScope = ["https://database.windows.net/.default"];

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (connection is SqlConnection sqlConn && string.IsNullOrEmpty(sqlConn.AccessToken))
        {
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(SqlScope), cancellationToken);
            sqlConn.AccessToken = token.Token;
        }
        return result;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        if (connection is SqlConnection sqlConn && string.IsNullOrEmpty(sqlConn.AccessToken))
        {
            var token = credential.GetTokenAsync(
                new TokenRequestContext(SqlScope), CancellationToken.None).GetAwaiter().GetResult();
            sqlConn.AccessToken = token.Token;
        }
        return result;
    }
}
