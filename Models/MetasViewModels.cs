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
    public CrearMetaFormViewModel MetaForm { get; set; } = new();
}

public class CrearMetaFormViewModel
{
    public string NombreMeta { get; set; } = string.Empty;
    public string? MontoObjetivo { get; set; }
    public string? AhorroInicial { get; set; }
    public DateTime? FechaObjetivo { get; set; }
    public string Frecuencia { get; set; } = "Mensual";
    public string? IconoMeta { get; set; } = "meta";
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
    public string PorcentajeTexto =>
        Porcentaje <= 0m ? "0%" :
        Porcentaje > 0m && Porcentaje < 0.01m ? "<0,01%" :
        Porcentaje.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("es-AR")) + "%";
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
