using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoneyUp.Data;
using MoneyUp.Models;

namespace MoneyUp.Controllers;

[Authorize]
public class InicioController : Controller
{
    private readonly UsuarioDataService _usuarioDataService;
    private readonly ILogger<InicioController> _logger;

    public InicioController(UsuarioDataService usuarioDataService, ILogger<InicioController> logger)
    {
        _usuarioDataService = usuarioDataService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdValue, out var userId))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("IniciarSesion", "Cuenta");
        }

        try
        {
            var usuario = await _usuarioDataService.ObtenerPorIdAsync(userId);
            if (usuario == null)
            {
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return RedirectToAction("IniciarSesion", "Cuenta");
            }

            var resumen = await _usuarioDataService.ObtenerResumenAsync(userId);

            var model = new InicioViewModel
            {
                NombreUsuario = usuario.NombreCompleto,
                SaldoDisponible = resumen.SaldoDisponible,
                UltimosMovimientos = resumen.UltimosMovimientos,
                Metas = resumen.Metas,
                MensajeUp = "¡Buen trabajo!",
                MensajeUpDetalle = "Los consejos personalizados estarán disponibles próximamente."
            };

            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar el inicio privado del usuario {UserId}.", userId);
            return Problem("No pudimos obtener tu resumen financiero en este momento.", statusCode: 500);
        }
    }
}
