using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MoneyUp.Data;
using MoneyUp.Models;
using System.Globalization;

namespace MoneyUp.Controllers;

[Authorize]
public class MovimientosController : Controller
{
    private const int PageSize = 5;
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly ILogger<MovimientosController> _logger;
    private readonly MovimientoDataService _movimientoDataService;

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

    public MovimientosController(SqlConnectionFactory connectionFactory, MovimientoDataService movimientoDataService, ILogger<MovimientosController> logger)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _movimientoDataService = movimientoDataService ?? throw new ArgumentNullException(nameof(movimientoDataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private int? ObtenerUsuarioAutenticadoId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(userIdValue, out var userId) ? userId : null;
    }

    private static bool TryParseMontoArgentina(string? rawValue, out decimal monto)
    {
        monto = 0m;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim();
        normalized = normalized.Replace("$", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Length == 0)
        {
            return false;
        }

        var hasDot = normalized.Contains('.');
        var hasComma = normalized.Contains(',');

        if (hasDot && hasComma)
        {
            var lastDotIndex = normalized.LastIndexOf('.');
            var lastCommaIndex = normalized.LastIndexOf(',');
            var decimalSeparator = lastDotIndex > lastCommaIndex ? '.' : ',';
            var groupingSeparator = decimalSeparator == '.' ? ',' : '.';
            normalized = normalized.Replace(groupingSeparator.ToString(), string.Empty, StringComparison.Ordinal);
            normalized = normalized.Replace(decimalSeparator, '.');
        }
        else if (hasComma)
        {
            normalized = normalized.Replace(",", ".", StringComparison.Ordinal);
        }

        if (normalized.Count(c => c == '.') > 1)
        {
            return false;
        }

        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out monto))
        {
            return false;
        }

        if (monto <= 0m)
        {
            return false;
        }

        var decimalPlaces = BitConverter.GetBytes(decimal.GetBits(monto)[3])[2];
        if (decimalPlaces > 2)
        {
            return false;
        }

        return monto <= 9999999999999999.99m;
    }

    [HttpGet]
    public async Task<IActionResult> RegistrarIngreso()
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        var viewModel = await ConstruirModeloIndexAsync(userId.Value, DateTime.Today.Month, DateTime.Today.Year, null, null, 1);
        var ingresoModel = new RegistrarIngresoFormViewModel
        {
            FechaMovimiento = DateTime.Today,
            Repeticion = "unico",
            CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value)
        };

        ViewData["IngresoForm"] = ingresoModel;
        ViewData["OpenIngresoForm"] = true;
        return View("Index", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarIngreso(RegistrarIngresoFormViewModel model)
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        model.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);

        if (string.IsNullOrWhiteSpace(model.Repeticion))
        {
            model.Repeticion = "unico";
        }

        decimal? montoParseado = null;
        if (!TryParseMontoArgentina(model.Monto, out var parsedMonto))
        {
            ModelState.AddModelError(nameof(model.Monto), "Ingresá un monto mayor a cero, con máximo dos decimales.");
        }
        else
        {
            montoParseado = parsedMonto;
            model.Monto = parsedMonto.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (model.IdCategoria is null || model.IdCategoria <= 0)
        {
            ModelState.AddModelError(nameof(model.IdCategoria), "Seleccioná una categoría de ingreso.");
        }
        else
        {
            var categoriaValida = await _movimientoDataService.ObtenerCategoriaIngresoAutorizadaAsync(userId.Value, model.IdCategoria.Value);
            if (categoriaValida is null)
            {
                ModelState.AddModelError(nameof(model.IdCategoria), "La categoría seleccionada no está disponible para este usuario.");
            }
        }

        if (!model.FechaMovimiento.HasValue)
        {
            ModelState.AddModelError(nameof(model.FechaMovimiento), "La fecha del ingreso es obligatoria.");
        }
        else if (model.FechaMovimiento.Value.Date > DateTime.Today)
        {
            ModelState.AddModelError(nameof(model.FechaMovimiento), "La fecha del ingreso no puede ser futura.");
        }

        if (model.Repeticion != "unico")
        {
            ModelState.AddModelError(nameof(model.Repeticion), "La repetición de ingresos estará disponible próximamente.");
        }

        ViewData["IngresoForm"] = model;
        ViewData["OpenIngresoForm"] = true;

        if (!ModelState.IsValid)
        {
            var fechaReferencia = model.FechaMovimiento ?? DateTime.Today;
            var viewModel = await ConstruirModeloIndexAsync(userId.Value, fechaReferencia.Month, fechaReferencia.Year, null, null, 1);

            var ingresoModel = ViewData["IngresoForm"] as RegistrarIngresoFormViewModel ?? model;
            ingresoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);
            ViewData["IngresoForm"] = ingresoModel;

            var gastoModel = ViewData["GastoForm"] as RegistrarGastoFormViewModel ?? new RegistrarGastoFormViewModel
            {
                FechaMovimiento = DateTime.Today,
                Repeticion = "unico"
            };
            gastoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);
            ViewData["GastoForm"] = gastoModel;
            ViewData["OpenGastoForm"] = true;
            return View("Index", viewModel);
        }

        try
        {
            await _movimientoDataService.InsertarIngresoAsync(userId.Value, model.IdCategoria!.Value, montoParseado!.Value, model.FechaMovimiento!.Value.Date);
            TempData["MovimientoExito"] = "Ingreso registrado correctamente";
            return RedirectToAction(nameof(Index), new { mes = model.FechaMovimiento.Value.Month, anio = model.FechaMovimiento.Value.Year, pagina = 1 });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al registrar ingreso para el usuario {UserId}.", userId.Value);
            ModelState.AddModelError(string.Empty, "No pudimos registrar el ingreso en este momento. Intentalo más tarde.");
            var fechaReferencia = model.FechaMovimiento ?? DateTime.Today;
            var viewModel = await ConstruirModeloIndexAsync(userId.Value, fechaReferencia.Month, fechaReferencia.Year, null, null, 1);

            var ingresoModel = ViewData["IngresoForm"] as RegistrarIngresoFormViewModel ?? model;
            ingresoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);
            ViewData["IngresoForm"] = ingresoModel;

            var gastoModel = ViewData["GastoForm"] as RegistrarGastoFormViewModel ?? new RegistrarGastoFormViewModel
            {
                FechaMovimiento = DateTime.Today,
                Repeticion = "unico"
            };
            gastoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);
            ViewData["GastoForm"] = gastoModel;
            ViewData["OpenGastoForm"] = true;
            ViewData["OpenIngresoForm"] = true;
            return View("Index", viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al registrar ingreso para el usuario {UserId}.", userId.Value);
            ModelState.AddModelError(string.Empty, "No pudimos registrar el ingreso en este momento. Intentalo más tarde.");
            var fechaReferencia = model.FechaMovimiento ?? DateTime.Today;
            var viewModel = await ConstruirModeloIndexAsync(userId.Value, fechaReferencia.Month, fechaReferencia.Year, null, null, 1);

            var ingresoModel = ViewData["IngresoForm"] as RegistrarIngresoFormViewModel ?? model;
            ingresoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);
            ViewData["IngresoForm"] = ingresoModel;

            var gastoModel = ViewData["GastoForm"] as RegistrarGastoFormViewModel ?? new RegistrarGastoFormViewModel
            {
                FechaMovimiento = DateTime.Today,
                Repeticion = "unico"
            };
            gastoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);
            ViewData["GastoForm"] = gastoModel;
            ViewData["OpenGastoForm"] = true;
            ViewData["OpenIngresoForm"] = true;
            return View("Index", viewModel);
        }
    }

    [HttpGet]
    public async Task<IActionResult> RegistrarGasto()
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        var viewModel = await ConstruirModeloIndexAsync(userId.Value, DateTime.Today.Month, DateTime.Today.Year, null, null, 1);
        var gastoModel = new RegistrarGastoFormViewModel
        {
            FechaMovimiento = DateTime.Today,
            Repeticion = "unico",
            CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value)
        };

        ViewData["GastoForm"] = gastoModel;
        ViewData["OpenGastoForm"] = true;
        return View("Index", viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegistrarGasto(RegistrarGastoFormViewModel model)
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        model.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);

        if (string.IsNullOrWhiteSpace(model.Repeticion))
        {
            model.Repeticion = "unico";
        }

        decimal? montoParseado = null;
        if (!TryParseMontoArgentina(model.Monto, out var parsedMonto))
        {
            ModelState.AddModelError(nameof(model.Monto), "Ingresá un monto mayor a cero, con máximo dos decimales.");
        }
        else
        {
            montoParseado = parsedMonto;
            model.Monto = parsedMonto.ToString("0.##", CultureInfo.InvariantCulture);
        }

        if (model.IdCategoria is null || model.IdCategoria <= 0)
        {
            ModelState.AddModelError(nameof(model.IdCategoria), "Seleccioná una categoría de gasto.");
        }
        else
        {
            var categoriaValida = await _movimientoDataService.ObtenerCategoriaGastoAutorizadaAsync(userId.Value, model.IdCategoria.Value);
            if (categoriaValida is null)
            {
                ModelState.AddModelError(nameof(model.IdCategoria), "La categoría seleccionada no está disponible para este usuario.");
            }
        }

        if (!model.FechaMovimiento.HasValue)
        {
            ModelState.AddModelError(nameof(model.FechaMovimiento), "La fecha del gasto es obligatoria.");
        }
        else if (model.FechaMovimiento.Value.Date > DateTime.Today)
        {
            ModelState.AddModelError(nameof(model.FechaMovimiento), "La fecha del gasto no puede ser futura.");
        }

        if (model.Repeticion != "unico")
        {
            ModelState.AddModelError(nameof(model.Repeticion), "La repetición de gastos estará disponible próximamente.");
        }

        ViewData["GastoForm"] = model;
        ViewData["OpenGastoForm"] = true;

        if (!ModelState.IsValid)
        {
            var fechaReferencia = model.FechaMovimiento ?? DateTime.Today;
            var viewModel = await ConstruirModeloIndexAsync(userId.Value, fechaReferencia.Month, fechaReferencia.Year, null, null, 1);

            var gastoModel = ViewData["GastoForm"] as RegistrarGastoFormViewModel ?? model;
            gastoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);
            ViewData["GastoForm"] = gastoModel;

            var ingresoModel = ViewData["IngresoForm"] as RegistrarIngresoFormViewModel ?? new RegistrarIngresoFormViewModel
            {
                FechaMovimiento = DateTime.Today,
                Repeticion = "unico"
            };
            ingresoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);
            ViewData["IngresoForm"] = ingresoModel;
            ViewData["OpenIngresoForm"] = false;
            ViewData["OpenGastoForm"] = true;
            return View("Index", viewModel);
        }

        try
        {
            await _movimientoDataService.InsertarGastoAsync(userId.Value, model.IdCategoria!.Value, montoParseado!.Value, model.FechaMovimiento!.Value.Date);
            TempData["MovimientoExito"] = "Gasto registrado correctamente";
            return RedirectToAction(nameof(Index), new { mes = model.FechaMovimiento.Value.Month, anio = model.FechaMovimiento.Value.Year, pagina = 1 });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al registrar gasto para el usuario {UserId}.", userId.Value);
            ModelState.AddModelError(string.Empty, "No pudimos registrar el gasto en este momento. Intentalo más tarde.");
            var fechaReferencia = model.FechaMovimiento ?? DateTime.Today;
            var viewModel = await ConstruirModeloIndexAsync(userId.Value, fechaReferencia.Month, fechaReferencia.Year, null, null, 1);

            var gastoModel = ViewData["GastoForm"] as RegistrarGastoFormViewModel ?? model;
            gastoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);
            ViewData["GastoForm"] = gastoModel;

            var ingresoModel = ViewData["IngresoForm"] as RegistrarIngresoFormViewModel ?? new RegistrarIngresoFormViewModel
            {
                FechaMovimiento = DateTime.Today,
                Repeticion = "unico"
            };
            ingresoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);
            ViewData["IngresoForm"] = ingresoModel;
            ViewData["OpenIngresoForm"] = false;
            ViewData["OpenGastoForm"] = true;
            return View("Index", viewModel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al registrar gasto para el usuario {UserId}.", userId.Value);
            ModelState.AddModelError(string.Empty, "No pudimos registrar el gasto en este momento. Intentalo más tarde.");
            var fechaReferencia = model.FechaMovimiento ?? DateTime.Today;
            var viewModel = await ConstruirModeloIndexAsync(userId.Value, fechaReferencia.Month, fechaReferencia.Year, null, null, 1);

            var gastoModel = ViewData["GastoForm"] as RegistrarGastoFormViewModel ?? model;
            gastoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);
            ViewData["GastoForm"] = gastoModel;

            var ingresoModel = ViewData["IngresoForm"] as RegistrarIngresoFormViewModel ?? new RegistrarIngresoFormViewModel
            {
                FechaMovimiento = DateTime.Today,
                Repeticion = "unico"
            };
            ingresoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);
            ViewData["IngresoForm"] = ingresoModel;
            ViewData["OpenIngresoForm"] = false;
            ViewData["OpenGastoForm"] = true;
            return View("Index", viewModel);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int idMovimiento, int? mes, int? anio, string? busqueda, int? categoria, int pagina = 1)
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        if (idMovimiento <= 0)
        {
            TempData["MovimientoError"] = "No pudimos eliminar este movimiento en este momento.";
            return RedirectToAction(nameof(Index), new { mes, anio, busqueda, categoria, pagina });
        }

        try
        {
            var movimiento = await _movimientoDataService.ObtenerMovimientoParaEliminarAsync(userId.Value, idMovimiento);
            if (movimiento is null)
            {
                TempData["MovimientoError"] = "No pudimos eliminar este movimiento en este momento.";
                return RedirectToAction(nameof(Index), new { mes, anio, busqueda, categoria, pagina });
            }

            var filasAfectadas = await _movimientoDataService.EliminarMovimientoAsync(userId.Value, idMovimiento);
            if (filasAfectadas <= 0)
            {
                TempData["MovimientoError"] = "No pudimos eliminar este movimiento en este momento.";
                return RedirectToAction(nameof(Index), new { mes, anio, busqueda, categoria, pagina });
            }

            var mesDestino = mes ?? DateTime.Today.Month;
            var anioDestino = anio ?? DateTime.Today.Year;
            var paginaActualizada = pagina > 0 ? pagina : 1;
            var estadoActual = await ConstruirModeloIndexAsync(userId.Value, mesDestino, anioDestino, busqueda, categoria, paginaActualizada);
            if (estadoActual.TotalResultados > 0)
            {
                paginaActualizada = Math.Clamp(paginaActualizada, 1, estadoActual.TotalPaginas);
            }
            else
            {
                paginaActualizada = 1;
            }

            TempData["MovimientoExito"] = "Movimiento eliminado correctamente";
            return RedirectToAction(nameof(Index), new { mes = mesDestino, anio = anioDestino, busqueda, categoria, pagina = paginaActualizada });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al eliminar movimiento {IdMovimiento} para el usuario {UserId}.", idMovimiento, userId.Value);
            TempData["MovimientoError"] = "No pudimos eliminar este movimiento en este momento.";
            return RedirectToAction(nameof(Index), new { mes, anio, busqueda, categoria, pagina });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al eliminar movimiento {IdMovimiento} para el usuario {UserId}.", idMovimiento, userId.Value);
            TempData["MovimientoError"] = "No pudimos eliminar este movimiento en este momento.";
            return RedirectToAction(nameof(Index), new { mes, anio, busqueda, categoria, pagina });
        }
    }

    private async Task<MovimientosIndexViewModel> ConstruirModeloIndexAsync(int userId, int? mes, int? anio, string? busqueda, int? categoria, int pagina)
    {
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
                    model.PresupuestoUtilizadoPorcentaje = 0m;
                    model.PresupuestoMensaje = model.PresupuestoGastado > 0m ? $"Superaste tu presupuesto por {model.PresupuestoGastado:C}." : "No registraste gastos para este mes.";
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

            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar movimientos del usuario {UserId}.", userId);
            throw;
        }
    }

    [HttpGet]
    public async Task<IActionResult> Index(int? mes, int? anio, string? busqueda, int? categoria, int pagina = 1)
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
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

        if (TempData["MovimientoExito"] is string exito)
        {
            ViewData["MovimientoExito"] = exito;
        }

        ViewData["IngresoForm"] ??= new RegistrarIngresoFormViewModel
        {
            FechaMovimiento = DateTime.Today,
            Repeticion = "unico"
        };

        ViewData["OpenIngresoForm"] ??= false;

        var ingresoModel = ViewData["IngresoForm"] as RegistrarIngresoFormViewModel ?? new RegistrarIngresoFormViewModel
        {
            FechaMovimiento = DateTime.Today,
            Repeticion = "unico"
        };
        ingresoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasIngresoAsync(userId.Value);
        ViewData["IngresoForm"] = ingresoModel;

        var gastoModel = ViewData["GastoForm"] as RegistrarGastoFormViewModel ?? new RegistrarGastoFormViewModel
        {
            FechaMovimiento = DateTime.Today,
            Repeticion = "unico"
        };
        gastoModel.CategoriasDisponibles = await _movimientoDataService.ObtenerCategoriasGastoAsync(userId.Value);
        ViewData["GastoForm"] = gastoModel;

        try
        {
            var model = await ConstruirModeloIndexAsync(userId.Value, mes, anio, busqueda, categoria, pagina);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar movimientos del usuario {UserId}.", userId.Value);
            return Problem("No pudimos cargar tus movimientos en este momento.", statusCode: 500);
        }
    }
}
