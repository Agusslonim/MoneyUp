using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
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
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Inicio");
        }

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
            TempData["CuentaDetalle"] = "Bienvenido a MoneyUp.";
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
    public IActionResult IniciarSesion(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Inicio");
        }

        ViewData["Title"] = "Inicio de sesión";
        ViewData["ReturnUrl"] = returnUrl;

        if (TempData["CuentaMensaje"] is string mensaje)
        {
            ViewData["CuentaMensaje"] = mensaje;
        }

        return View(new CuentaLoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IniciarSesion(CuentaLoginViewModel model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        model.Email = model.Email.Trim();

        try
        {
            var usuario = await _usuarioDataService.ObtenerPorEmailAsync(model.Email);
            if (usuario == null)
            {
                ModelState.AddModelError(string.Empty, "El correo o la contraseña son incorrectos.");
                model.Contrasena = string.Empty;
                return View(model);
            }

            var passwordHasher = new PasswordHasher<Usuario>();
            var verificationResult = passwordHasher.VerifyHashedPassword(usuario, usuario.ContrasenaHash, model.Contrasena);

            if (verificationResult == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(string.Empty, "El correo o la contraseña son incorrectos.");
                model.Contrasena = string.Empty;
                return View(model);
            }

            if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                var newHash = passwordHasher.HashPassword(usuario, model.Contrasena);
                await _usuarioDataService.ActualizarHashAsync(usuario.IdUsuario, newHash);
                usuario.ContrasenaHash = newHash;
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, usuario.IdUsuario.ToString()),
                new(ClaimTypes.Name, usuario.NombreCompleto)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = false,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(60)
                });

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Inicio");
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error de SQL al iniciar sesión para el email {Email}.", model.Email);
            ModelState.AddModelError(string.Empty, "No pudimos iniciar sesión en este momento. Intentalo más tarde.");
            model.Contrasena = string.Empty;
            return View(model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al iniciar sesión para el email {Email}.", model.Email);
            ModelState.AddModelError(string.Empty, "No pudimos iniciar sesión en este momento. Intentalo más tarde.");
            model.Contrasena = string.Empty;
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarSesion()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(IniciarSesion));
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
