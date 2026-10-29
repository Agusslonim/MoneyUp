using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MoneyUp.Data;
using MoneyUp.Models;

namespace MoneyUp.Controllers;

[Authorize]
public class MovimientosController : Controller
{
    private const int PageSize = 5;
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly ILogger<MovimientosController> _logger;

    private sealed class CategoriaPrincipalResultado
    {
        public int IdCategoria { get; set; }
        public string NombreCategoria { get; set; } = string.Empty;
        public decimal MontoTotal { get; set; }
    }

    private sealed class MovimientoListadoRow
    {
        public int IdMovimiento { get; set; }
        public DateTime FechaMovimiento { get; set; }
        public string? Descripcion { get; set; }
        public string? Categoria { get; set; }
        public bool TipoMovimiento { get; set; }
        public decimal Monto { get; set; }
        public string? IconoCategoria { get; set; }
    }

    public MovimientosController(SqlConnectionFactory connectionFactory, ILogger<MovimientosController> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? mes, int? anio, string? busqueda, int? categoria, int pagina = 1)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        if (mes is < 1 or > 12)
        {
            return BadRequest("El mes debe estar entre 1 y 12.");
        }

        if (anio is < 1 or > 9999)
        {
            return BadRequest("El año debe estar entre 1 y 9999.");
        }

        var selectedMonth = mes ?? DateTime.Today.Month;
        var selectedYear = anio ?? DateTime.Today.Year;
        var currentPage = pagina > 0 ? pagina : 1;

        var model = new MovimientosIndexViewModel
        {
            Mes = selectedMonth,
            Anio = selectedYear,
            Busqueda = busqueda,
            CategoriaSeleccionadaId = categoria,
            PaginaActual = currentPage,
            TienePresupuesto = false
        };

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var categorias = await connection.QueryAsync<CategoriaFiltroViewModel>(@"
                SELECT IdCategoria, NombreCategoria, TipoCategoria
                FROM dbo.Categoria
                WHERE IdUsuario IS NULL OR IdUsuario = @IdUsuario
                ORDER BY TipoCategoria, NombreCategoria;", new { IdUsuario = userId });

            model.Categorias = categorias.ToList();

            int? categoriaValidada = categoria.HasValue && model.Categorias.Any(x => x.IdCategoria == categoria.Value)
                ? categoria.Value
                : null;

            model.CategoriaSeleccionadaId = categoriaValidada;

            var dateStart = new DateTime(selectedYear, selectedMonth, 1);
            var dateEnd = dateStart.AddMonths(1);

            var ingresosMes = await connection.QuerySingleOrDefaultAsync<decimal?>(@"
                SELECT COALESCE(SUM(Monto), 0)
                FROM dbo.Movimiento
                WHERE IdUsuario = @IdUsuario
                  AND TipoMovimiento = 1
                  AND FechaMovimiento >= @InicioMes
                  AND FechaMovimiento < @FinMes;",
                new { IdUsuario = userId, InicioMes = dateStart, FinMes = dateEnd });
            model.IngresosMes = ingresosMes ?? 0m;

            var gastosMes = await connection.QuerySingleOrDefaultAsync<decimal?>(@"
                SELECT COALESCE(SUM(Monto), 0)
                FROM dbo.Movimiento
                WHERE IdUsuario = @IdUsuario
                  AND TipoMovimiento = 0
                  AND FechaMovimiento >= @InicioMes
                  AND FechaMovimiento < @FinMes;",
                new { IdUsuario = userId, InicioMes = dateStart, FinMes = dateEnd });
            model.GastosMes = gastosMes ?? 0m;

            var aportes = await connection.QuerySingleOrDefaultAsync<decimal?>(@"
                SELECT COALESCE(SUM(mm.Monto), 0)
                FROM dbo.MovimientoMeta mm
                INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta
                WHERE m.IdUsuario = @IdUsuario
                  AND mm.TipoOperacion = 'Aporte'
                  AND mm.FechaMovimientoMeta >= @InicioMes
                  AND mm.FechaMovimientoMeta < @FinMes;",
                new { IdUsuario = userId, InicioMes = dateStart, FinMes = dateEnd });

            var retiros = await connection.QuerySingleOrDefaultAsync<decimal?>(@"
                SELECT COALESCE(SUM(mm.Monto), 0)
                FROM dbo.MovimientoMeta mm
                INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta
                WHERE m.IdUsuario = @IdUsuario
                  AND mm.TipoOperacion = 'Retiro'
                  AND mm.FechaMovimientoMeta >= @InicioMes
                  AND mm.FechaMovimientoMeta < @FinMes;",
                new { IdUsuario = userId, InicioMes = dateStart, FinMes = dateEnd });

            var saldoHistorico = await connection.QuerySingleOrDefaultAsync<decimal?>(@"
                SELECT 
                    (COALESCE((SELECT SUM(Monto) FROM dbo.Movimiento WHERE IdUsuario = @IdUsuario AND TipoMovimiento = 1), 0)
                    - COALESCE((SELECT SUM(Monto) FROM dbo.Movimiento WHERE IdUsuario = @IdUsuario AND TipoMovimiento = 0), 0)
                    - COALESCE((SELECT SUM(mm.Monto) FROM dbo.MovimientoMeta mm INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta WHERE m.IdUsuario = @IdUsuario AND mm.TipoOperacion = 'Aporte'), 0)
                    + COALESCE((SELECT SUM(mm.Monto) FROM dbo.MovimientoMeta mm INNER JOIN dbo.Meta m ON m.IdMeta = mm.IdMeta WHERE m.IdUsuario = @IdUsuario AND mm.TipoOperacion = 'Retiro'), 0));",
                new { IdUsuario = userId });

            model.SaldoActual = saldoHistorico ?? 0m;

            var categoriaPrincipal = await connection.QuerySingleOrDefaultAsync<CategoriaPrincipalResultado>(@"
                SELECT TOP 1 c.IdCategoria, c.NombreCategoria, SUM(m.Monto) AS MontoTotal
                FROM dbo.Movimiento m
                INNER JOIN dbo.Categoria c ON c.IdCategoria = m.IdCategoria
                WHERE m.IdUsuario = @IdUsuario
                  AND m.TipoMovimiento = 0
                  AND m.FechaMovimiento >= @InicioMes
                  AND m.FechaMovimiento < @FinMes
                GROUP BY c.IdCategoria, c.NombreCategoria
                ORDER BY SUM(m.Monto) DESC, c.NombreCategoria ASC;",
                new { IdUsuario = userId, InicioMes = dateStart, FinMes = dateEnd });

            if (categoriaPrincipal is null || model.GastosMes == 0)
            {
                model.CategoriaPrincipalNombre = "Sin gastos";
                model.CategoriaPrincipalMonto = 0m;
                model.CategoriaPrincipalPorcentaje = 0m;
            }
            else
            {
                model.CategoriaPrincipalNombre = categoriaPrincipal.NombreCategoria;
                model.CategoriaPrincipalMonto = categoriaPrincipal.MontoTotal;
                model.CategoriaPrincipalPorcentaje = model.GastosMes == 0 ? 0m : Math.Round((model.CategoriaPrincipalMonto / model.GastosMes) * 100m, 2);
            }

            var presupuesto = await connection.QuerySingleOrDefaultAsync<dynamic>(@"
                SELECT TOP 1 IdPresupuesto, Mes, Anio, MontoLimite
                FROM dbo.PresupuestoGeneral
                WHERE IdUsuario = @IdUsuario AND Mes = @Mes AND Anio = @Anio
                ORDER BY FechaCreacion DESC;",
                new { IdUsuario = userId, Mes = selectedMonth, Anio = selectedYear });

            if (presupuesto != null)
            {
                model.TienePresupuesto = true;
                model.PresupuestoLimite = (decimal)presupuesto.MontoLimite;
                model.PresupuestoGastado = model.GastosMes;
                model.PresupuestoRestante = model.PresupuestoLimite - model.PresupuestoGastado;

                if (model.PresupuestoLimite == 0m)
                {
                    model.PresupuestoUtilizadoPorcentaje = model.PresupuestoGastado > 0m ? 0m : 0m;
                    if (model.PresupuestoGastado > 0m)
                    {
                        model.PresupuestoMensaje = $"Superaste tu presupuesto por {model.PresupuestoGastado:C}.";
                    }
                    else
                    {
                        model.PresupuestoMensaje = "No registraste gastos para este mes.";
                    }
                }
                else
                {
                    model.PresupuestoUtilizadoPorcentaje = (model.PresupuestoGastado / model.PresupuestoLimite) * 100m;
                    model.PresupuestoMensaje = model.PresupuestoRestante >= 0
                        ? $"Te quedan {model.PresupuestoRestante:C} para este mes."
                        : $"Superaste tu presupuesto por {Math.Abs(model.PresupuestoRestante):C}.";
                }
            }
            else
            {
                model.TienePresupuesto = false;
                model.PresupuestoMensaje = "No definiste un presupuesto para este mes.";
            }

            var categoriasGasto = await connection.QueryAsync<dynamic>(@"
                SELECT c.IdCategoria, c.NombreCategoria, COALESCE(SUM(m.Monto), 0) AS MontoTotal
                FROM dbo.Categoria c
                LEFT JOIN dbo.Movimiento m ON m.IdCategoria = c.IdCategoria
                    AND m.IdUsuario = @IdUsuario
                    AND m.TipoMovimiento = 0
                    AND m.FechaMovimiento >= @InicioMes
                    AND m.FechaMovimiento < @FinMes
                WHERE (c.IdUsuario IS NULL OR c.IdUsuario = @IdUsuario)
                  AND (c.TipoCategoria = 'Gasto')
                GROUP BY c.IdCategoria, c.NombreCategoria
                HAVING COALESCE(SUM(m.Monto), 0) > 0
                ORDER BY MontoTotal DESC, c.NombreCategoria ASC;",
                new { IdUsuario = userId, InicioMes = dateStart, FinMes = dateEnd });

            var gastoTotalMes = model.GastosMes;
            var resumenGasto = categoriasGasto.Select((x, index) =>
            {
                var monto = (decimal)x.MontoTotal;
                var porcentaje = gastoTotalMes == 0 ? 0m : Math.Round((monto / gastoTotalMes) * 100m, 2);
                var palette = new[] { "#7C3AED", "#7ADFF0", "#FFB067", "#A78BFA", "#60D5ED", "#C4B5FD", "#FBBF24", "#F59E0B" };
                return new MovimientoCategoriaResumenViewModel
                {
                    IdCategoria = (int)x.IdCategoria,
                    NombreCategoria = (string)x.NombreCategoria,
                    Monto = monto,
                    Porcentaje = porcentaje,
                    Color = palette[index % palette.Length]
                };
            }).ToList();
            model.GastosPorCategoria = resumenGasto;

            var searchText = string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim();
            var sqlWhereParts = new List<string>
            {
                " WHERE m.IdUsuario = @IdUsuario",
                " AND m.FechaMovimiento >= @InicioMes",
                " AND m.FechaMovimiento < @FinMes"
            };
            var sqlParameters = new DynamicParameters();
            sqlParameters.Add("@IdUsuario", userId);
            sqlParameters.Add("@InicioMes", dateStart);
            sqlParameters.Add("@FinMes", dateEnd);

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                sqlWhereParts.Add(" AND (m.Descripcion LIKE @Busqueda OR c.NombreCategoria LIKE @Busqueda)");
                sqlParameters.Add("@Busqueda", $"%{searchText}%");
            }

            if (categoriaValidada.HasValue)
            {
                sqlWhereParts.Add(" AND m.IdCategoria = @IdCategoria");
                sqlParameters.Add("@IdCategoria", categoriaValidada.Value);
            }

            var sqlWhere = string.Concat(sqlWhereParts);

            var totalCount = await connection.ExecuteScalarAsync<int>($@"
                SELECT COUNT(*)
                FROM dbo.Movimiento m
                LEFT JOIN dbo.Categoria c ON c.IdCategoria = m.IdCategoria
                {sqlWhere};", sqlParameters);

            var totalPaginas = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)PageSize);
            var paginaAjustada = Math.Clamp(currentPage, 1, totalPaginas);
            if (paginaAjustada != currentPage)
            {
                currentPage = paginaAjustada;
                model.PaginaActual = currentPage;
            }

            var pageSkip = (currentPage - 1) * PageSize;
            sqlParameters.Add("@Offset", pageSkip);
            sqlParameters.Add("@PageSize", PageSize);

            var items = await connection.QueryAsync<MovimientoListadoRow>($@"
                SELECT m.IdMovimiento, m.FechaMovimiento, m.Descripcion, c.NombreCategoria AS Categoria,
                       m.TipoMovimiento, m.Monto, c.Icono AS IconoCategoria
                FROM dbo.Movimiento m
                LEFT JOIN dbo.Categoria c ON c.IdCategoria = m.IdCategoria
                {sqlWhere}
                ORDER BY m.FechaMovimiento DESC, m.IdMovimiento DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;",
                sqlParameters);

            var listado = items.Select(x =>
            {
                var esIngreso = x.TipoMovimiento;
                var tipoValor = esIngreso ? 1 : 0;

                return new MovimientoListadoItemViewModel
                {
                    IdMovimiento = x.IdMovimiento,
                    FechaMovimiento = x.FechaMovimiento,
                    Descripcion = string.IsNullOrWhiteSpace(x.Descripcion) ? (string.IsNullOrWhiteSpace(x.Categoria) ? "General" : x.Categoria) : x.Descripcion,
                    Categoria = string.IsNullOrWhiteSpace(x.Categoria) ? "General" : x.Categoria,
                    TipoMovimiento = tipoValor,
                    TipoMovimientoTexto = esIngreso ? "Ingreso" : "Gasto",
                    Monto = x.Monto,
                    IconoCategoria = string.IsNullOrWhiteSpace(x.IconoCategoria) ? "tarjeta" : x.IconoCategoria,
                    TieneAccionesDisponibles = false
                };
            }).ToList();

            model.Movimientos = listado;
            model.TotalResultados = totalCount;
            model.TotalPaginas = totalPaginas;
            model.MostrandoDesde = totalCount == 0 ? 0 : pageSkip + 1;
            model.MostrandoHasta = totalCount == 0 ? 0 : Math.Min(pageSkip + PageSize, totalCount);

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar movimientos del usuario {UserId}.", userId);
            return Problem("No pudimos cargar tus movimientos en este momento.", statusCode: 500);
        }
    }
}
