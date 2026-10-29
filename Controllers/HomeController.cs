using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MoneyUp.Models;

namespace MoneyUp.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        var modelo = new LandingViewModel
        {
            Funciones = new()
            {
                new() { Icono = "carrito", Titulo = "Registrar gastos", Descripcion = "Anotá tus compras y movimientos para saber cuánto estás gastando.", Etiqueta = "Monto · categoría · fecha" },
                new() { Icono = "dinero", Titulo = "Registrar ingresos", Descripcion = "Sumá sueldo, mensualidad, beca o cualquier dinero que recibas.", Etiqueta = "Conocé tu saldo", Color = "celeste" },
                new() { Icono = "meta", Titulo = "Crear metas", Descripcion = "Definí objetivos y visualizá cuánto te falta para alcanzarlos.", Etiqueta = "Seguí tu progreso", Color = "cyan" },
                new() { Icono = "estadisticas", Titulo = "Ver estadísticas", Descripcion = "Compará períodos, categorías y la evolución de tus gastos.", Etiqueta = "Analizá tus hábitos", Color = "celeste" },
                new() { Icono = "idea", Titulo = "Recibir recomendaciones", Descripcion = "Consejos relacionados con tus hábitos para ayudarte a mejorar.", Etiqueta = "Mejorá de a poco" },
                new() { Icono = "resumen", Titulo = "Ver tu resumen", Descripcion = "Saldo, ingresos, gastos y metas visibles apenas entrás.", Etiqueta = "Todo de un vistazo", Color = "cyan" }
            }
        };
        return View(modelo);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
    });
}
