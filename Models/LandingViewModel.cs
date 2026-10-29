namespace MoneyUp.Models;

// Textos públicos de la landing. No representan datos de usuarios ni usan SQL.
public class LandingViewModel
{
    public List<FuncionViewModel> Funciones { get; set; } = new();
}

public class FuncionViewModel
{
    // Nombre del ícono en wwwroot/images/icons (sin la extensión).
    public string Icono { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public string Etiqueta { get; set; } = "";
    public string Color { get; set; } = "violeta";
}
