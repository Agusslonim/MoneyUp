using Microsoft.AspNetCore.Mvc;

namespace MoneyUp.Controllers;

// Estas pantallas explican el estado de la próxima etapa. Todavía no autentican ni guardan cuentas.
public class CuentaController : Controller
{
    public IActionResult Registro()
    {
        ViewData["Title"] = "Crear una cuenta";
        ViewData["Mensaje"] = "El registro de usuarios es el próximo paso de MoneyUp. Por ahora podés conocer la propuesta desde nuestra página de inicio.";
        return View("Proximamente");
    }

    public IActionResult IniciarSesion()
    {
        ViewData["Title"] = "Iniciar sesión";
        ViewData["Mensaje"] = "Estamos preparando el acceso a tu cuenta. El inicio de sesión estará disponible cuando conectemos los usuarios con la base de datos.";
        return View("Proximamente");
    }
}
