namespace MoneyUp.Models;

public class MetasIndexViewModel
{
    public string EstadoFiltro { get; set; } = "Activa";
    public decimal TotalAhorrado { get; set; }
    public int MetasActivas { get; set; }
    public decimal ProgresoPromedio { get; set; }
    public decimal AporteEsteMes { get; set; }
    public List<MetaItemViewModel> Metas { get; set; } = new();
    public List<AporteProgramadoViewModel> ProximosAportes { get; set; } = new();
}

public class MetaItemViewModel
{
    public int IdMeta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public decimal MontoObjetivo { get; set; }
    public decimal AhorroActual { get; set; }
    public decimal Porcentaje { get; set; }
    public decimal PorcentajeVisual => Math.Clamp(Porcentaje, 0m, 100m);
    public string Estado { get; set; } = "Activa";
    public string Icono { get; set; } = "meta";
}

public class AporteProgramadoViewModel
{
    public int IdAporteProgramado { get; set; }
    public int IdMeta { get; set; }
    public string NombreMeta { get; set; } = string.Empty;
    public string Icono { get; set; } = "meta";
    public DateTime FechaProgramada { get; set; }
    public decimal MontoProgramado { get; set; }
}
