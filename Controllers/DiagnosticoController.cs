using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using MoneyUp.Data;

namespace MoneyUp.Controllers;

public sealed class DiagnosticoController : Controller
{
    private readonly SqlConnectionFactory _connectionFactory;
    private readonly ILogger<DiagnosticoController> _logger;

    public DiagnosticoController(SqlConnectionFactory connectionFactory, ILogger<DiagnosticoController> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    [HttpGet("/Diagnostico/BaseDeDatos")]
    public async Task<IActionResult> BaseDeDatos()
    {
        if (!HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment())
        {
            return NotFound();
        }

        try
        {
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync();

            var databaseName = await connection.QuerySingleAsync<string>("SELECT DB_NAME();");
            var tableExists = await connection.QuerySingleAsync<int>(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Usuario') THEN 1 ELSE 0 END;");

            var userCount = 0;
            if (tableExists == 1)
            {
                userCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Usuario;");
            }

            var isValid = string.Equals(databaseName, "MoneyUp", StringComparison.OrdinalIgnoreCase)
                && tableExists == 1;

            var result = new
            {
                ok = isValid,
                baseDatos = databaseName,
                tablaUsuarioExiste = tableExists == 1,
                totalUsuarios = userCount,
                mensaje = isValid
                    ? "La conexión con la base de datos de MoneyUp está funcionando correctamente."
                    : "La base de datos no coincide con MoneyUp o la tabla dbo.Usuario no está disponible."
            };

            if (!isValid)
            {
                _logger.LogError("Diagnóstico de base de datos falló. DB={DatabaseName}, UsuarioExiste={UsuarioExiste}, TotalUsuarios={TotalUsuarios}", databaseName, tableExists == 1, userCount);
                return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No se pudo validar la base de datos de MoneyUp." });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar la conexión con la base de datos MoneyUp.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { ok = false, mensaje = "No se pudo validar la base de datos de MoneyUp." });
        }
    }
}
