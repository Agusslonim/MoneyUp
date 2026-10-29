using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MoneyUp.Data;
using MoneyUp.Models;
using Microsoft.Data.SqlClient;

namespace MoneyUp.Controllers;

// Estas pantallas explican el estado de la próxima etapa. Todavía no autentican ni guardan cuentas.
public class CuentaController : Controller
{
    private readonly UsuarioDataService _usuarioDataService;
    private readonly ILogger<CuentaController> _logger;

    public CuentaController(UsuarioDataService usuarioDataService, ILogger<CuentaController> logger)
    {
        _usuarioDataService = usuarioDataService;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Registro()
    {
        ViewData["Title"] = "Registrarse";
        return View(new CuentaRegistroViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registro(CuentaRegistroViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.NombreCompleto = model.NombreCompleto.Trim();
        model.Email = model.Email.Trim();

        if (string.IsNullOrWhiteSpace(model.NombreCompleto))
        {
            ModelState.AddModelError(nameof(model.NombreCompleto), "El nombre es obligatorio.");
        }

        if (model.NombreCompleto.Length > 150)
        {
            ModelState.AddModelError(nameof(model.NombreCompleto), "El nombre no puede superar los 150 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(model.Email))
        {
            ModelState.AddModelError(nameof(model.Email), "El correo electrónico es obligatorio.");
        }

        if (model.Email.Length > 254)
        {
            ModelState.AddModelError(nameof(model.Email), "El correo electrónico no puede superar los 254 caracteres.");
        }

        if (model.Contrasena.Length < 8)
        {
            ModelState.AddModelError(nameof(model.Contrasena), "La contraseña debe tener al menos 8 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(model.ConfirmarContrasena))
        {
            ModelState.AddModelError(nameof(model.ConfirmarContrasena), "Confirmá tu contraseña.");
        }

        if (!string.Equals(model.Contrasena, model.ConfirmarContrasena, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(model.ConfirmarContrasena), "Las contraseñas no coinciden.");
        }

        if (ModelState.ContainsKey(nameof(model.Contrasena)) && ModelState[nameof(model.Contrasena)]?.Errors.Count > 0)
        {
            model.Contrasena = string.Empty;
            model.ConfirmarContrasena = string.Empty;
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            if (await _usuarioDataService.ExisteEmailAsync(model.Email))
            {
                ModelState.AddModelError(nameof(model.Email), "Ese correo electrónico ya está registrado.");
                return View(model);
            }

            var passwordHasher = new PasswordHasher<Usuario>();
            var newUser = new Usuario { NombreCompleto = model.NombreCompleto, Email = model.Email };
            var passwordHash = passwordHasher.HashPassword(newUser, model.Contrasena);

            await _usuarioDataService.RegistrarAsync(model, passwordHash);

            TempData["CuentaMensaje"] = "Tu cuenta fue creada. Ya podés iniciar sesión.";
            TempData["CuentaDetalle"] = "La función de inicio de sesión sigue en desarrollo, pero podés continuar con el flujo de acceso cuando esté lista.";
            return RedirectToAction(nameof(IniciarSesion));
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            _logger.LogWarning(ex, "Intento de registro con email duplicado.");
            ModelState.AddModelError(nameof(model.Email), "Ese correo electrónico ya está registrado.");
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al registrar usuario.");
            ModelState.AddModelError(string.Empty, "Ocurrió un error inesperado. Intentalo más tarde.");
            return View(model);
        }
    }

    [HttpGet]
    public IActionResult IniciarSesion(string? mensaje = null)
    {
        ViewData["Title"] = "Inicio de sesión";
        if (!string.IsNullOrWhiteSpace(mensaje))
        {
            TempData["CuentaMensaje"] = mensaje;
        }

        return View(new CuentaLoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult IniciarSesion(CuentaLoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        TempData["CuentaMensaje"] = "La función de inicio de sesión aún está en desarrollo.";
        TempData["CuentaDetalle"] = "Podés seguir revisando la navegación y la creación de cuenta mientras se completa la autenticación.";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RecuperarContrasena()
    {
        TempData["CuentaMensaje"] = "La recuperación de contraseña aún está en desarrollo.";
        TempData["CuentaDetalle"] = "Esta funcionalidad se incorporará en una próxima etapa del proyecto.";
        return RedirectToAction(nameof(IniciarSesion));
    }
}
