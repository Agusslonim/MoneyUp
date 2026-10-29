namespace MoneyUp.Models;

public class MovimientosIndexViewModel
{
    public int Mes { get; set; }
    public int Anio { get; set; }
    public string? Busqueda { get; set; }
    public int? CategoriaSeleccionadaId { get; set; }

    public decimal SaldoActual { get; set; }
    public decimal IngresosMes { get; set; }
    public decimal GastosMes { get; set; }
    public string CategoriaPrincipalNombre { get; set; } = "Sin gastos";
    public decimal CategoriaPrincipalMonto { get; set; }
    public decimal CategoriaPrincipalPorcentaje { get; set; }
    public bool TieneCategoriaPrincipal => !string.IsNullOrWhiteSpace(CategoriaPrincipalNombre) && CategoriaPrincipalNombre != "Sin gastos";

    public decimal PresupuestoLimite { get; set; }
    public decimal PresupuestoGastado { get; set; }
    public decimal PresupuestoRestante { get; set; }
    public decimal PresupuestoUtilizadoPorcentaje { get; set; }
    public bool TienePresupuesto { get; set; }
    public string PresupuestoMensaje { get; set; } = "No definiste un presupuesto para este mes.";

    public List<CategoriaFiltroViewModel> Categorias { get; set; } = new();
    public List<MovimientoCategoriaResumenViewModel> GastosPorCategoria { get; set; } = new();
    public List<MovimientoListadoItemViewModel> Movimientos { get; set; } = new();

    public int PaginaActual { get; set; } = 1;
    public int TotalPaginas { get; set; }
    public int TotalResultados { get; set; }
    public int MostrandoDesde { get; set; }
    public int MostrandoHasta { get; set; }
    public string MostrandoTexto => TotalResultados == 0
        ? "No hay movimientos para mostrar."
        : $"Mostrando {MostrandoDesde} de {TotalResultados} movimientos";
    public bool HayResultados => TotalResultados > 0;
}

public class CategoriaFiltroViewModel
{
    public int IdCategoria { get; set; }
    public string NombreCategoria { get; set; } = string.Empty;
    public string TipoCategoria { get; set; } = string.Empty;
}

public class MovimientoCategoriaResumenViewModel
{
    public int IdCategoria { get; set; }
    public string NombreCategoria { get; set; } = string.Empty;
    public decimal Monto { get; set; }
    public decimal Porcentaje { get; set; }
    public string Color { get; set; } = "#7C3AED";
}

public class MovimientoListadoItemViewModel
{
    public int IdMovimiento { get; set; }
    public DateTime FechaMovimiento { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;
    public string TipoMovimientoTexto { get; set; } = "Gasto";
    public int TipoMovimiento { get; set; }
    public decimal Monto { get; set; }
    public string IconoCategoria { get; set; } = "tarjeta";
    public bool TieneAccionesDisponibles { get; set; }
}
