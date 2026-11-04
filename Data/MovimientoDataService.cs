using Dapper;
using Microsoft.Data.SqlClient;
using MoneyUp.Models;

namespace MoneyUp.Data;

public sealed class MovimientoDataService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public MovimientoDataService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<List<CategoriaFiltroViewModel>> ObtenerCategoriasIngresoAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT IdCategoria, NombreCategoria, TipoCategoria
            FROM dbo.Categoria
            WHERE TipoCategoria = 'Ingreso'
              AND (IdUsuario IS NULL OR IdUsuario = @IdUsuario)
            ORDER BY NombreCategoria;";

        return (await connection.QueryAsync<CategoriaFiltroViewModel>(sql, new { IdUsuario = idUsuario })).AsList();
    }

    public async Task<CategoriaFiltroViewModel?> ObtenerCategoriaIngresoAutorizadaAsync(int idUsuario, int idCategoria, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT IdCategoria, NombreCategoria, TipoCategoria
            FROM dbo.Categoria
            WHERE IdCategoria = @IdCategoria
              AND TipoCategoria = 'Ingreso'
              AND (IdUsuario IS NULL OR IdUsuario = @IdUsuario);";

        return await connection.QuerySingleOrDefaultAsync<CategoriaFiltroViewModel>(sql, new
        {
            IdCategoria = idCategoria,
            IdUsuario = idUsuario
        });
    }

    public async Task InsertarIngresoAsync(int idUsuario, int idCategoria, decimal monto, DateTime fechaMovimiento, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO dbo.Movimiento (IdUsuario, IdCategoria, TipoMovimiento, Monto, Descripcion, FechaMovimiento)
            VALUES (@IdUsuario, @IdCategoria, 1, @Monto, NULL, @FechaMovimiento);";

        await connection.ExecuteAsync(sql, new
        {
            IdUsuario = idUsuario,
            IdCategoria = idCategoria,
            Monto = monto,
            FechaMovimiento = fechaMovimiento
        });
    }

    public async Task<List<CategoriaFiltroViewModel>> ObtenerCategoriasGastoAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT IdCategoria, NombreCategoria, TipoCategoria
            FROM dbo.Categoria
            WHERE TipoCategoria = 'Gasto'
              AND (IdUsuario IS NULL OR IdUsuario = @IdUsuario)
            ORDER BY NombreCategoria;";

        return (await connection.QueryAsync<CategoriaFiltroViewModel>(sql, new { IdUsuario = idUsuario })).AsList();
    }

    public async Task<CategoriaFiltroViewModel?> ObtenerCategoriaGastoAutorizadaAsync(int idUsuario, int idCategoria, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT IdCategoria, NombreCategoria, TipoCategoria
            FROM dbo.Categoria
            WHERE IdCategoria = @IdCategoria
              AND TipoCategoria = 'Gasto'
              AND (IdUsuario IS NULL OR IdUsuario = @IdUsuario);";

        return await connection.QuerySingleOrDefaultAsync<CategoriaFiltroViewModel>(sql, new
        {
            IdCategoria = idCategoria,
            IdUsuario = idUsuario
        });
    }

    public async Task InsertarGastoAsync(int idUsuario, int idCategoria, decimal monto, DateTime fechaMovimiento, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO dbo.Movimiento (IdUsuario, IdCategoria, IdMovimientoFijo, TipoMovimiento, Monto, Descripcion, FechaMovimiento)
            VALUES (@IdUsuario, @IdCategoria, NULL, 0, @Monto, NULL, @FechaMovimiento);";

        await connection.ExecuteAsync(sql, new
        {
            IdUsuario = idUsuario,
            IdCategoria = idCategoria,
            Monto = monto,
            FechaMovimiento = fechaMovimiento
        });
    }

    public async Task<MovimientoEliminarDetalleViewModel?> ObtenerMovimientoParaEliminarAsync(int idUsuario, int idMovimiento, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT m.IdMovimiento,
                   m.IdUsuario,
                   m.TipoMovimiento,
                   m.Monto,
                   m.Descripcion,
                   c.NombreCategoria AS Categoria
            FROM dbo.Movimiento m
            LEFT JOIN dbo.Categoria c ON c.IdCategoria = m.IdCategoria
            WHERE m.IdMovimiento = @IdMovimiento
              AND m.IdUsuario = @IdUsuario;";

        return await connection.QuerySingleOrDefaultAsync<MovimientoEliminarDetalleViewModel>(sql, new
        {
            IdMovimiento = idMovimiento,
            IdUsuario = idUsuario
        });
    }

    public async Task<int> EliminarMovimientoAsync(int idUsuario, int idMovimiento, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            DELETE FROM dbo.Movimiento
            WHERE IdMovimiento = @IdMovimiento
              AND IdUsuario = @IdUsuario;";

        return await connection.ExecuteAsync(sql, new
        {
            IdMovimiento = idMovimiento,
            IdUsuario = idUsuario
        });
    }
}
