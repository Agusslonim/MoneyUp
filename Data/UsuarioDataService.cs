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

    public async Task<Usuario?> ObtenerPorEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim();

        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return null;
        }

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT IdUsuario, NombreCompleto, Email, ContrasenaHash, FechaNacimiento, FechaRegistro
            FROM dbo.Usuario
            WHERE Email = @Email;";

        return await connection.QuerySingleOrDefaultAsync<Usuario>(sql, new { Email = normalizedEmail });
    }

    public async Task<Usuario?> ObtenerPorIdAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT IdUsuario, NombreCompleto, Email, ContrasenaHash, FechaNacimiento, FechaRegistro
            FROM dbo.Usuario
            WHERE IdUsuario = @IdUsuario;";

        return await connection.QuerySingleOrDefaultAsync<Usuario>(sql, new { IdUsuario = idUsuario });
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

    public async Task ActualizarHashAsync(int idUsuario, string passwordHash, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        const string sql = @"
            UPDATE dbo.Usuario
            SET ContrasenaHash = @ContrasenaHash
            WHERE IdUsuario = @IdUsuario;";

        await connection.ExecuteAsync(sql, new { IdUsuario = idUsuario, ContrasenaHash = passwordHash });
    }

    public async Task<(decimal SaldoDisponible, List<MovimientoResumenViewModel> UltimosMovimientos, List<MetaResumenViewModel> Metas)> ObtenerResumenAsync(int idUsuario, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var movimientos = await connection.QueryAsync<MovimientoBaseDto>(@"
            SELECT
                m.IdMovimiento,
                m.IdUsuario,
                m.TipoMovimiento,
                m.Monto,
                m.FechaMovimiento,
                m.IdCategoria,
                c.NombreCategoria AS Categoria,
                m.Descripcion
            FROM dbo.Movimiento m
            LEFT JOIN dbo.Categoria c ON c.IdCategoria = m.IdCategoria
            WHERE m.IdUsuario = @IdUsuario
            ORDER BY m.FechaMovimiento DESC, m.IdMovimiento DESC;", new { IdUsuario = idUsuario });

        var ingresos = await connection.QuerySingleAsync<decimal?>(@"
            SELECT COALESCE(SUM(Monto), 0)
            FROM dbo.Movimiento
            WHERE IdUsuario = @IdUsuario AND TipoMovimiento = 1;", new { IdUsuario = idUsuario });

        var gastos = await connection.QuerySingleAsync<decimal?>(@"
            SELECT COALESCE(SUM(Monto), 0)
            FROM dbo.Movimiento
            WHERE IdUsuario = @IdUsuario AND TipoMovimiento = 0;", new { IdUsuario = idUsuario });

        var aportes = await connection.QuerySingleAsync<decimal?>(@"
            SELECT COALESCE(SUM(CASE WHEN mm.TipoOperacion = 'Aporte' THEN mm.Monto ELSE 0 END), 0)
            FROM dbo.MovimientoMeta mm
            INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta
            WHERE m.IdUsuario = @IdUsuario;", new { IdUsuario = idUsuario });

        var retiros = await connection.QuerySingleAsync<decimal?>(@"
            SELECT COALESCE(SUM(CASE WHEN mm.TipoOperacion = 'Retiro' THEN mm.Monto ELSE 0 END), 0)
            FROM dbo.MovimientoMeta mm
            INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta
            WHERE m.IdUsuario = @IdUsuario;", new { IdUsuario = idUsuario });

        var saldoDisponible = (ingresos ?? 0m) - (gastos ?? 0m) - (aportes ?? 0m) + (retiros ?? 0m);

        var ultimosMovimientos = movimientos
            .OrderByDescending(x => x.FechaMovimiento)
            .ThenByDescending(x => x.IdMovimiento)
            .Take(5)
            .Select(m => new MovimientoResumenViewModel
            {
                IdMovimiento = m.IdMovimiento,
                FechaMovimiento = m.FechaMovimiento,
                Descripcion = string.IsNullOrWhiteSpace(m.Descripcion) ? (m.Categoria ?? "General") : m.Descripcion,
                Categoria = string.IsNullOrWhiteSpace(m.Categoria) ? "General" : m.Categoria,
                Monto = m.Monto,
                TipoMovimiento = m.TipoMovimiento,
                Icono = IconoPorCategoria(m.Categoria),
                Color = "gris"
            }).ToList();

        var metas = await connection.QueryAsync<MetaDto>(@"
            SELECT
                m.IdMeta,
                m.NombreMeta AS Nombre,
                m.MontoObjetivo,
                m.IdUsuario,
                m.EstadoMeta AS Estado
            FROM dbo.Meta m
            WHERE m.IdUsuario = @IdUsuario
            ORDER BY CASE WHEN m.EstadoMeta = 'Activa' THEN 0 ELSE 1 END, m.IdMeta;", new { IdUsuario = idUsuario });

        var metasResumen = new List<MetaResumenViewModel>();

        foreach (var meta in metas.Take(3))
        {
            var ahorroBruto = await connection.QuerySingleAsync<decimal?>(@"
                SELECT COALESCE(SUM(CASE WHEN mm.TipoOperacion = 'Aporte' THEN mm.Monto ELSE -mm.Monto END), 0)
                FROM dbo.MovimientoMeta mm
                INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta
                WHERE mm.IdMeta = @IdMeta AND m.IdUsuario = @IdUsuario;", new { IdMeta = meta.IdMeta, IdUsuario = idUsuario });

            var objetivo = meta.MontoObjetivo;
            var porcentaje = objetivo == 0 ? 0m : Math.Min(100m, Math.Max(0m, (ahorroBruto ?? 0m) / objetivo * 100m));

            metasResumen.Add(new MetaResumenViewModel
            {
                IdMeta = meta.IdMeta,
                Nombre = meta.Nombre,
                MontoObjetivo = meta.MontoObjetivo,
                AhorroActual = ahorroBruto ?? 0m,
                Porcentaje = porcentaje,
                Estado = meta.Estado,
                Icono = "meta"
            });
        }

        return (saldoDisponible, ultimosMovimientos, metasResumen);
    }

    private static string IconoPorCategoria(string? categoria)
    {
        if (string.IsNullOrWhiteSpace(categoria))
        {
            return "tarjeta";
        }

        return categoria.Trim().ToLowerInvariant() switch
        {
            "cocina" => "carrito",
            "comida" => "carrito",
            "transporte" => "carrito",
            "salud" => "salud",
            "vivienda" => "alquiler",
            "ocio" => "idea",
            "educacion" => "libro",
            "sueldo" => "dinero",
            _ => "tarjeta"
        };
    }

    private sealed class MovimientoBaseDto
    {
        public int IdMovimiento { get; set; }
        public int IdUsuario { get; set; }
        public int TipoMovimiento { get; set; }
        public decimal Monto { get; set; }
        public DateTime FechaMovimiento { get; set; }
        public int? IdCategoria { get; set; }
        public string? Categoria { get; set; }
        public string? Descripcion { get; set; }
    }

    private sealed class MetaDto
    {
        public int IdMeta { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public decimal MontoObjetivo { get; set; }
        public int IdUsuario { get; set; }
        public string Estado { get; set; } = string.Empty;
    }
}
