using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace MhM.UI.Data;

/// <summary>
/// Interceptor that provides Azure AD authentication tokens for SQL connections.
/// This ensures fresh tokens are obtained for each connection, avoiding token expiration issues.
/// </summary>
public class AzureAdAuthenticationDbConnectionInterceptor : DbConnectionInterceptor
{
    private static readonly string[] AzureSqlScopes = new[] { "https://database.windows.net/.default" };
    private readonly TokenCredential _credential;

    public AzureAdAuthenticationDbConnectionInterceptor(TokenCredential credential)
    {
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        SetAccessToken(connection);
        return base.ConnectionOpening(connection, eventData, result);
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        await SetAccessTokenAsync(connection, cancellationToken);
        return await base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    private void SetAccessToken(DbConnection connection)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var tokenRequestContext = new TokenRequestContext(AzureSqlScopes);
            var accessToken = _credential.GetToken(tokenRequestContext, default);
            sqlConnection.AccessToken = accessToken.Token;
        }
    }

    private async Task SetAccessTokenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is SqlConnection sqlConnection)
        {
            var tokenRequestContext = new TokenRequestContext(AzureSqlScopes);
            var accessToken = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);
            sqlConnection.AccessToken = accessToken.Token;
        }
    }
}
