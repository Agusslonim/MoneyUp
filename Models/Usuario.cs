namespace MoneyUp.Models;

public class Usuario
{
    public int IdUsuario { get; set; }
    public string NombreCompleto { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ContrasenaHash { get; set; } = string.Empty;
    public DateTime? FechaNacimiento { get; set; }
    public DateTime FechaRegistro { get; set; }
}
