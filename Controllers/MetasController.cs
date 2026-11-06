using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using MoneyUp.Data;
using MoneyUp.Models;

namespace MoneyUp.Controllers;

[Authorize]
public class MetasController : Controller
{
    private readonly MetaDataService _metaDataService;
    private readonly ILogger<MetasController> _logger;

    public MetasController(MetaDataService metaDataService, ILogger<MetasController> logger)
    {
        _metaDataService = metaDataService ?? throw new ArgumentNullException(nameof(metaDataService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? estado)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        var filtroValido = string.Equals(estado, "Cumplida", StringComparison.OrdinalIgnoreCase) ? "Cumplida" : "Activa";

        try
        {
            var model = await _metaDataService.ObtenerDashboardAsync(userId, filtroValido);
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar el dashboard de metas para el usuario {UserId}.", userId);
            return Problem("No pudimos obtener tus metas en este momento.", statusCode: 500);
        }
    }
}
