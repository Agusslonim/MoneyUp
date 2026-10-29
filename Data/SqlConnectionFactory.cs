using Microsoft.Data.SqlClient;

namespace MoneyUp.Data;

public sealed class SqlConnectionFactory
{
    private readonly IConfiguration _configuration;

    public SqlConnectionFactory(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public SqlConnection CreateConnection()
    {
        var connectionString = _configuration.GetConnectionString("MoneyUp");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("No se encontró la cadena de conexión 'MoneyUp'.");
        }

        return new SqlConnection(connectionString);
    }
}
