namespace MoneyUp.Models;

public class InicioViewModel
{
    public string NombreUsuario { get; set; } = string.Empty;
    public decimal SaldoDisponible { get; set; }
    public List<MovimientoResumenViewModel> UltimosMovimientos { get; set; } = new();
    public List<MetaResumenViewModel> Metas { get; set; } = new();
    public string MensajeUp { get; set; } = "¡Buen trabajo!";
    public string MensajeUpDetalle { get; set; } = "Los consejos personalizados estarán disponibles próximamente.";
}

public class MovimientoResumenViewModel
{
    public int IdMovimiento { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public int TipoMovimiento { get; set; }
    public string Icono { get; set; } = "tarjeta";
    public string Color { get; set; } = "gris";
    public string Origen { get; set; } = "Movimiento";
    public bool TieneAccionesDisponibles { get; set; }
}

public class MetaResumenViewModel
{
    public int IdMeta { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public decimal MontoObjetivo { get; set; }
    public decimal AhorroActual { get; set; }
    public decimal Porcentaje { get; set; }
    public decimal PorcentajeVisual => Math.Clamp(Porcentaje, 0m, 100m);
    public string Estado { get; set; } = "Activa";
    public string Icono { get; set; } = "meta";
    public string PorcentajeTexto =>
        Porcentaje <= 0m ? "0%" :
        Porcentaje > 0m && Porcentaje < 0.01m ? "<0,01%" :
        Porcentaje.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
