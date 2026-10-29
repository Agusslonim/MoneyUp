using Dapper;
using Microsoft.Data.SqlClient;
using MoneyUp.Models;

namespace MoneyUp.Data;

public sealed class UsuarioDataService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public UsuarioDataService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<bool> ExisteEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim();

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return false;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var exists = await connection.QuerySingleAsync<int>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Usuario WHERE Email = @Email) THEN 1 ELSE 0 END;",
            new { Email = normalizedEmail });

        return exists == 1;
    }

    public async Task<int> RegistrarAsync(CuentaRegistroViewModel model, string passwordHash, CancellationToken cancellationToken = default)
    {
        var nombre = model.NombreCompleto.Trim();
        var email = model.Email.Trim();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO dbo.Usuario (NombreCompleto, Email, ContrasenaHash)
            OUTPUT INSERTED.IdUsuario
            VALUES (@NombreCompleto, @Email, @ContrasenaHash);";

        return await connection.QuerySingleAsync<int>(sql, new
        {
            NombreCompleto = nombre,
            Email = email,
            ContrasenaHash = passwordHash
        });
    }
}
