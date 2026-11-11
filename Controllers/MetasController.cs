using System.Data;
using System.Globalization;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MoneyUp.Data;
using MoneyUp.Models;

namespace MoneyUp.Controllers;

[Authorize]
public class MetasController : Controller
{
    private readonly MetaDataService _metaDataService;
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly ILogger<MetasController> _logger;

    public MetasController(MetaDataService metaDataService, SqlConnectionFactory connectionFactory, ILogger<MetasController> logger)
    {
        _metaDataService = metaDataService ?? throw new ArgumentNullException(nameof(metaDataService));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? estado)
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        var filtroValido = string.Equals(estado, "Cumplida", StringComparison.OrdinalIgnoreCase) ? "Cumplida" : "Activa";

        try
        {
            var model = await _metaDataService.ObtenerDashboardAsync(userId.Value, filtroValido);
            model.MetaForm ??= new CrearMetaFormViewModel();
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar el dashboard de metas para el usuario {UserId}.", userId.Value);
            return Problem("No pudimos obtener tus metas en este momento.", statusCode: 500);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Crear(CrearMetaFormViewModel model)
    {
        var userId = ObtenerUsuarioAutenticadoId();
        if (userId is null)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        var dashboard = await _metaDataService.ObtenerDashboardAsync(userId.Value, "Activa");
        if (model is null)
        {
            ModelState.AddModelError(string.Empty, "No se pudo procesar el formulario de la meta.");
            dashboard.MetaForm = new CrearMetaFormViewModel();
            ViewData["OpenMetaModal"] = true;
            ViewData["HasMetaFormErrors"] = true;
            return View("Index", dashboard);
        }

        dashboard.MetaForm = model;
        var nombreMeta = model.NombreMeta?.Trim();
        if (string.IsNullOrWhiteSpace(nombreMeta))
        {
            ModelState.AddModelError(nameof(model.NombreMeta), "El nombre de la meta es obligatorio.");
        }
        else if (nombreMeta.Length > 100)
        {
            ModelState.AddModelError(nameof(model.NombreMeta), "El nombre no puede superar 100 caracteres.");
        }

        if (!TryParseMontoArgentina(model.MontoObjetivo, out var montoObjetivo, allowZero: false))
        {
            ModelState.AddModelError(nameof(model.MontoObjetivo), "Ingresá un monto objetivo mayor a cero, con máximo dos decimales.");
        }

        decimal ahorroInicial = 0m;
        if (!string.IsNullOrWhiteSpace(model.AhorroInicial))
        {
            if (!TryParseMontoArgentina(model.AhorroInicial, out var parsedAhorroInicial, allowZero: true) || parsedAhorroInicial < 0m)
            {
                ModelState.AddModelError(nameof(model.AhorroInicial), "El ahorro inicial debe ser un número válido, con máximo dos decimales.");
            }
            else
            {
                ahorroInicial = parsedAhorroInicial;
            }
        }

        if (montoObjetivo > 0m && ahorroInicial > montoObjetivo)
        {
            ModelState.AddModelError(nameof(model.AhorroInicial), "El ahorro inicial no puede superar el monto objetivo.");
        }

        if (model.FechaObjetivo.HasValue && model.FechaObjetivo.Value.Date < DateTime.Today)
        {
            ModelState.AddModelError(nameof(model.FechaObjetivo), "La fecha objetivo debe ser hoy o posterior.");
        }

        if (ahorroInicial > 0m && montoObjetivo > 0m)
        {
            var saldoDisponible = await ObtenerSaldoDisponibleAsync(userId.Value);
            if (ahorroInicial > saldoDisponible)
            {
                ModelState.AddModelError(nameof(model.AhorroInicial), "El ahorro inicial no puede superar tu saldo disponible actual.");
            }
        }

        ViewData["OpenMetaModal"] = true;
        ViewData["HasMetaFormErrors"] = ModelState.ErrorCount > 0;

        if (!ModelState.IsValid)
        {
            return View("Index", dashboard);
        }

        var estadoMeta = ahorroInicial >= montoObjetivo ? "Cumplida" : "Activa";

        try
        {
            await CrearMetaConTransaccionAsync(
                userId: userId.Value,
                nombreMeta: nombreMeta!,
                montoObjetivo: montoObjetivo,
                fechaObjetivo: model.FechaObjetivo?.Date,
                ahorroInicial: ahorroInicial,
                estadoMeta: estadoMeta,
                iconoMeta: string.IsNullOrWhiteSpace(model.IconoMeta) ? "meta" : model.IconoMeta);

            TempData["MetaExito"] = "Meta creada correctamente";
            return RedirectToAction(nameof(Index), new { estado = estadoMeta });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error SQL al crear meta para el usuario {UserId}.", userId.Value);
            ModelState.AddModelError(string.Empty, "No pudimos crear la meta en este momento. Intentalo más tarde.");
            ViewData["OpenMetaModal"] = true;
            ViewData["HasMetaFormErrors"] = true;
            return View("Index", dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al crear meta para el usuario {UserId}.", userId.Value);
            ModelState.AddModelError(string.Empty, "No pudimos crear la meta en este momento. Intentalo más tarde.");
            ViewData["OpenMetaModal"] = true;
            ViewData["HasMetaFormErrors"] = true;
            return View("Index", dashboard);
        }
    }

    private int? ObtenerUsuarioAutenticadoId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(userIdValue, out var userId) ? userId : null;
    }

    private static bool TryParseMontoArgentina(string? rawValue, out decimal monto, bool allowZero)
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

        var decimalPlaces = (decimal.GetBits(monto)[3] >> 16) & 0xFF;
        if (decimalPlaces > 2)
        {
            return false;
        }

        if (monto > 9999999999999999.99m)
        {
            return false;
        }

        if (monto < 0m || (!allowZero && monto <= 0m))
        {
            return false;
        }

        return true;
    }

    private async Task<decimal> ObtenerSaldoDisponibleAsync(int idUsuario)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        var saldo = await connection.QuerySingleAsync<decimal?>(@"
            SELECT (
                COALESCE((
                    SELECT SUM(mov.Monto)
                    FROM dbo.Movimiento AS mov WITH (UPDLOCK, HOLDLOCK)
                    WHERE mov.IdUsuario = @IdUsuario
                      AND mov.TipoMovimiento = 1
                ), 0)
                - COALESCE((
                    SELECT SUM(mov.Monto)
                    FROM dbo.Movimiento AS mov WITH (UPDLOCK, HOLDLOCK)
                    WHERE mov.IdUsuario = @IdUsuario
                      AND mov.TipoMovimiento = 0
                ), 0)
                - COALESCE((
                    SELECT SUM(CASE WHEN mm.TipoOperacion = 'Aporte' THEN mm.Monto ELSE 0 END)
                    FROM dbo.MovimientoMeta AS mm WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.Meta AS m WITH (UPDLOCK, HOLDLOCK) ON m.IdMeta = mm.IdMeta
                    WHERE m.IdUsuario = @IdUsuario
                ), 0)
                + COALESCE((
                    SELECT SUM(CASE WHEN mm.TipoOperacion = 'Retiro' THEN mm.Monto ELSE 0 END)
                    FROM dbo.MovimientoMeta AS mm WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.Meta AS m WITH (UPDLOCK, HOLDLOCK) ON m.IdMeta = mm.IdMeta
                    WHERE m.IdUsuario = @IdUsuario
                ), 0)
            );",
            new { IdUsuario = idUsuario });

        return saldo ?? 0m;
    }

    private async Task CrearMetaConTransaccionAsync(int userId, string nombreMeta, decimal montoObjetivo, DateTime? fechaObjetivo, decimal ahorroInicial, string estadoMeta, string? iconoMeta)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();

        var saldoDisponible = await ObtenerSaldoDisponibleParaTransaccionAsync(connection, userId, transaction);
        if (ahorroInicial > 0m && ahorroInicial > saldoDisponible)
        {
            throw new InvalidOperationException("Saldo insuficiente para el ahorro inicial.");
        }

        var idMeta = await connection.QuerySingleAsync<int>(@"
            INSERT INTO dbo.Meta (IdUsuario, NombreMeta, Descripcion, MontoObjetivo, FechaLimite, EstadoMeta, IconoMeta)
            OUTPUT INSERTED.IdMeta
            VALUES (@IdUsuario, @NombreMeta, NULL, @MontoObjetivo, @FechaLimite, @EstadoMeta, @IconoMeta);",
            new
            {
                IdUsuario = userId,
                NombreMeta = nombreMeta,
                MontoObjetivo = montoObjetivo,
                FechaLimite = fechaObjetivo,
                EstadoMeta = estadoMeta,
                IconoMeta = string.IsNullOrWhiteSpace(iconoMeta) ? "meta" : iconoMeta
            }, transaction: transaction);

        if (ahorroInicial > 0m)
        {
            await connection.ExecuteAsync(@"
                INSERT INTO dbo.MovimientoMeta (IdMeta, Monto, TipoOperacion)
                VALUES (@IdMeta, @Monto, @TipoOperacion);",
                new
                {
                    IdMeta = idMeta,
                    Monto = ahorroInicial,
                    TipoOperacion = "Aporte"
                }, transaction: transaction);
        }

        transaction.Commit();
    }

    private static async Task<decimal> ObtenerSaldoDisponibleParaTransaccionAsync(SqlConnection connection, int idUsuario, IDbTransaction transaction)
    {
        var saldo = await connection.QuerySingleAsync<decimal?>(@"
            SELECT (
                COALESCE((
                    SELECT SUM(mov.Monto)
                    FROM dbo.Movimiento AS mov WITH (UPDLOCK, HOLDLOCK)
                    WHERE mov.IdUsuario = @IdUsuario
                      AND mov.TipoMovimiento = 1
                ), 0)
                - COALESCE((
                    SELECT SUM(mov.Monto)
                    FROM dbo.Movimiento AS mov WITH (UPDLOCK, HOLDLOCK)
                    WHERE mov.IdUsuario = @IdUsuario
                      AND mov.TipoMovimiento = 0
                ), 0)
                - COALESCE((
                    SELECT SUM(CASE WHEN mm.TipoOperacion = 'Aporte' THEN mm.Monto ELSE 0 END)
                    FROM dbo.MovimientoMeta AS mm WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.Meta AS m WITH (UPDLOCK, HOLDLOCK) ON m.IdMeta = mm.IdMeta
                    WHERE m.IdUsuario = @IdUsuario
                ), 0)
                + COALESCE((
                    SELECT SUM(CASE WHEN mm.TipoOperacion = 'Retiro' THEN mm.Monto ELSE 0 END)
                    FROM dbo.MovimientoMeta AS mm WITH (UPDLOCK, HOLDLOCK)
                    INNER JOIN dbo.Meta AS m WITH (UPDLOCK, HOLDLOCK) ON m.IdMeta = mm.IdMeta
                    WHERE m.IdUsuario = @IdUsuario
                ), 0)
            );",
            new { IdUsuario = idUsuario }, transaction: transaction);

        return saldo ?? 0m;
    }
}
