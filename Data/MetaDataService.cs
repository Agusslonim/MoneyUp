using Dapper;
using Microsoft.Data.SqlClient;
using MoneyUp.Models;

namespace MoneyUp.Data;

public sealed class MetaDataService
{
    private readonly SqlConnectionFactory _connectionFactory;

    public MetaDataService(SqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<MetasIndexViewModel> ObtenerDashboardAsync(int idUsuario, string? estadoFiltro, CancellationToken cancellationToken = default)
    {
        var filtroValido = string.Equals(estadoFiltro, "Cumplida", StringComparison.OrdinalIgnoreCase) ? "Cumplida" : "Activa";

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var metas = (await connection.QueryAsync<MetaRow>(@"
            SELECT
                m.IdMeta,
                m.IdUsuario,
                m.NombreMeta AS Nombre,
                m.Descripcion,
                m.MontoObjetivo,
                m.FechaCreacion,
                m.FechaLimite,
                m.EstadoMeta AS Estado,
                m.IconoMeta AS Icono
            FROM dbo.Meta m
            WHERE m.IdUsuario = @IdUsuario
            ORDER BY CASE WHEN m.EstadoMeta = 'Activa' THEN 0 ELSE 1 END, m.IdMeta;",
            new { IdUsuario = idUsuario })).ToList();

        var movimientosPorMeta = (await connection.QueryAsync<MetaMovimientoAggregate>(@"
            SELECT
                mm.IdMeta,
                SUM(CASE WHEN mm.TipoOperacion = 'Aporte' THEN mm.Monto ELSE 0 END) AS TotalAportes,
                SUM(CASE WHEN mm.TipoOperacion = 'Retiro' THEN mm.Monto ELSE 0 END) AS TotalRetiros
            FROM dbo.MovimientoMeta mm
            INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta
            WHERE m.IdUsuario = @IdUsuario
            GROUP BY mm.IdMeta;",
            new { IdUsuario = idUsuario })).ToDictionary(x => x.IdMeta, x => x);

        var metasItems = new List<MetaItemViewModel>();
        foreach (var meta in metas)
        {
            var totalAportes = movimientosPorMeta.TryGetValue(meta.IdMeta, out var movimientoMeta) ? movimientoMeta.TotalAportes : 0m;
            var totalRetiros = movimientosPorMeta.TryGetValue(meta.IdMeta, out var movimientoMetaRetiro) ? movimientoMetaRetiro.TotalRetiros : 0m;
            var ahorroActual = totalAportes - totalRetiros;
            var porcentaje = meta.MontoObjetivo > 0m ? (ahorroActual / meta.MontoObjetivo) * 100m : 0m;

            metasItems.Add(new MetaItemViewModel
            {
                IdMeta = meta.IdMeta,
                Nombre = meta.Nombre,
                Descripcion = meta.Descripcion,
                MontoObjetivo = meta.MontoObjetivo,
                AhorroActual = ahorroActual,
                Porcentaje = porcentaje,
                Estado = string.IsNullOrWhiteSpace(meta.Estado) ? "Activa" : meta.Estado,
                Icono = string.IsNullOrWhiteSpace(meta.Icono) ? "meta" : meta.Icono
            });
        }

        var activeMetas = metasItems.Where(meta => string.Equals(meta.Estado, "Activa", StringComparison.OrdinalIgnoreCase)).ToList();
        var activeProgressList = activeMetas.Select(meta => Math.Clamp(meta.Porcentaje, 0m, 100m)).ToList();

        var fechaInicioMes = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var fechaFinMes = fechaInicioMes.AddMonths(1);

        var aporteEsteMes = await connection.QuerySingleOrDefaultAsync<decimal?>(@"
            SELECT COALESCE(SUM(mm.Monto), 0)
            FROM dbo.MovimientoMeta mm
            INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta
            WHERE m.IdUsuario = @IdUsuario
              AND mm.TipoOperacion = 'Aporte'
              AND mm.FechaMovimientoMeta >= @FechaInicioMes
              AND mm.FechaMovimientoMeta < @FechaFinMes;",
            new { IdUsuario = idUsuario, FechaInicioMes = fechaInicioMes, FechaFinMes = fechaFinMes });

        var proximoAportes = (await connection.QueryAsync<AporteProgramadoRow>(@"
            SELECT
                ap.IdAporteProgramado,
                ap.IdMeta,
                m.NombreMeta AS NombreMeta,
                m.IconoMeta AS Icono,
                ap.FechaProgramada,
                ap.MontoProgramado
            FROM dbo.AporteProgramado ap
            INNER JOIN dbo.Meta m ON m.IdMeta = ap.IdMeta
            WHERE m.IdUsuario = @IdUsuario
              AND m.EstadoMeta = 'Activa'
              AND ap.FechaProgramada >= CAST(GETDATE() AS date)
            ORDER BY ap.FechaProgramada ASC, ap.IdAporteProgramado ASC;",
            new { IdUsuario = idUsuario })).ToList();

        var filteredMetas = filtroValido == "Cumplida"
            ? metasItems.Where(meta => string.Equals(meta.Estado, "Cumplida", StringComparison.OrdinalIgnoreCase)).ToList()
            : metasItems.Where(meta => string.Equals(meta.Estado, "Activa", StringComparison.OrdinalIgnoreCase)).ToList();

        return new MetasIndexViewModel
        {
            EstadoFiltro = filtroValido,
            TotalAhorrado = metasItems.Sum(meta => meta.AhorroActual),
            MetasActivas = activeMetas.Count,
            ProgresoPromedio = activeMetas.Count == 0 ? 0m : activeProgressList.Average(),
            AporteEsteMes = aporteEsteMes ?? 0m,
            Metas = filteredMetas
                .Select(meta => new MetaItemViewModel
                {
                    IdMeta = meta.IdMeta,
                    Nombre = meta.Nombre,
                    Descripcion = meta.Descripcion,
                    MontoObjetivo = meta.MontoObjetivo,
                    AhorroActual = meta.AhorroActual,
                    Porcentaje = meta.Porcentaje,
                    Estado = meta.Estado,
                    Icono = meta.Icono
                }).ToList(),
            ProximosAportes = proximoAportes
                .Select(item => new AporteProgramadoViewModel
                {
                    IdAporteProgramado = item.IdAporteProgramado,
                    IdMeta = item.IdMeta,
                    NombreMeta = item.NombreMeta,
                    Icono = string.IsNullOrWhiteSpace(item.Icono) ? "meta" : item.Icono,
                    FechaProgramada = item.FechaProgramada,
                    MontoProgramado = item.MontoProgramado
                }).ToList()
        };
    }

    private sealed class MetaRow
    {
        public int IdMeta { get; set; }
        public int IdUsuario { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string? Descripcion { get; set; }
        public decimal MontoObjetivo { get; set; }
        public DateTime FechaCreacion { get; set; }
        public DateTime? FechaLimite { get; set; }
        public string Estado { get; set; } = "Activa";
        public string? Icono { get; set; }
    }

    private sealed class MetaMovimientoAggregate
    {
        public int IdMeta { get; set; }
        public decimal TotalAportes { get; set; }
        public decimal TotalRetiros { get; set; }
    }

    private sealed class AporteProgramadoRow
    {
        public int IdAporteProgramado { get; set; }
        public int IdMeta { get; set; }
        public string NombreMeta { get; set; } = string.Empty;
        public string? Icono { get; set; }
        public DateTime FechaProgramada { get; set; }
        public decimal MontoProgramado { get; set; }
    }
}
