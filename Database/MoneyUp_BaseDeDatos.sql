/* MONEY UP - SQL SERVER
   Ejecutar TODO el archivo en SSMS (F5), conectado al servidor local.
   Requiere la base MoneyUp ya creada. No borra tablas ni registros.
   Compatible con Usuario creada mediante el script anterior del chat.
   Crea las tablas faltantes; no migra estructuras existentes diferentes.

   Alcance: las 10 tablas del DLR enviado. Up queda pendiente de modelado.
   Ajuste al DLR: Categoria.IdUsuario NULL para categorias predeterminadas.
   Agregar antes esa relacion al DER y ese campo al DLR de la entrega.

   Convenciones:
   - Todos los importes son pesos argentinos y positivos.
   - Movimiento.TipoMovimiento / MovimientoFijo.EsIngreso: 1 ingreso, 0 gasto.
   - Categoria.TipoCategoria: Ingreso o Gasto.
   - MovimientoMeta.TipoOperacion: Aporte o Retiro.
   - MovimientoMeta.Origen: texto como Manual o Up; NO ejecuta al chatbot.
   - FechaRegistro/FechaCreacion usan la hora local del servidor.
   - Frecuencia y estados de Meta quedan como texto, sin catalogo cerrado.
*/
USE MoneyUp;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Usuario', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Usuario (
            IdUsuario INT IDENTITY(1,1) CONSTRAINT PK_Usuario PRIMARY KEY,
            NombreCompleto NVARCHAR(150) NOT NULL,
            Email NVARCHAR(254) NOT NULL CONSTRAINT UQ_Usuario_Email UNIQUE,
            ContrasenaHash NVARCHAR(500) NOT NULL,
            FechaNacimiento DATE NULL,
            FechaRegistro DATETIME2 NOT NULL CONSTRAINT DF_Usuario_FechaRegistro DEFAULT SYSDATETIME()
        );
    END;

    IF OBJECT_ID(N'dbo.Categoria', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Categoria (
            IdCategoria INT IDENTITY(1,1) CONSTRAINT PK_Categoria PRIMARY KEY,
            IdUsuario INT NULL,
            NombreCategoria NVARCHAR(80) NOT NULL,
            TipoCategoria NVARCHAR(20) NOT NULL,
            Descripcion NVARCHAR(250) NULL,
            Icono NVARCHAR(100) NULL,
            Color CHAR(7) NULL,
            CONSTRAINT FK_Categoria_Usuario FOREIGN KEY (IdUsuario) REFERENCES dbo.Usuario(IdUsuario),
            CONSTRAINT CK_Categoria_Tipo CHECK (TipoCategoria IN (N'Ingreso', N'Gasto')),
            CONSTRAINT CK_Categoria_Nombre CHECK (LEN(LTRIM(RTRIM(NombreCategoria))) > 0),
            CONSTRAINT UQ_Categoria_PropietarioNombreTipo UNIQUE (IdUsuario, NombreCategoria, TipoCategoria)
        );
    END;

    IF OBJECT_ID(N'dbo.MovimientoFijo', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.MovimientoFijo (
            IdMovimientoFijo INT IDENTITY(1,1) CONSTRAINT PK_MovimientoFijo PRIMARY KEY,
            IdUsuario INT NOT NULL,
            IdCategoria INT NOT NULL,
            EsIngreso BIT NOT NULL,
            Monto DECIMAL(18,2) NOT NULL,
            Frecuencia NVARCHAR(20) NOT NULL,
            FechaInicio DATE NOT NULL,
            ProximaFecha DATE NOT NULL,
            FechaFin DATE NULL,
            Activo BIT NOT NULL CONSTRAINT DF_MovimientoFijo_Activo DEFAULT (1),
            CONSTRAINT FK_MovimientoFijo_Usuario FOREIGN KEY (IdUsuario) REFERENCES dbo.Usuario(IdUsuario),
            CONSTRAINT FK_MovimientoFijo_Categoria FOREIGN KEY (IdCategoria) REFERENCES dbo.Categoria(IdCategoria),
            CONSTRAINT CK_MovimientoFijo_Monto CHECK (Monto > 0),
            CONSTRAINT CK_MovimientoFijo_Fechas CHECK (ProximaFecha >= FechaInicio AND (FechaFin IS NULL OR FechaFin >= FechaInicio)),
            CONSTRAINT CK_MovimientoFijo_Frecuencia CHECK (LEN(LTRIM(RTRIM(Frecuencia))) > 0)
        );
        CREATE INDEX IX_MovimientoFijo_UsuarioActivoFecha ON dbo.MovimientoFijo(IdUsuario, Activo, ProximaFecha);
    END;

    IF OBJECT_ID(N'dbo.Movimiento', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Movimiento (
            IdMovimiento INT IDENTITY(1,1) CONSTRAINT PK_Movimiento PRIMARY KEY,
            IdUsuario INT NOT NULL,
            IdCategoria INT NOT NULL,
            IdMovimientoFijo INT NULL,
            TipoMovimiento BIT NOT NULL,
            Monto DECIMAL(18,2) NOT NULL,
            Descripcion NVARCHAR(500) NULL,
            FechaMovimiento DATETIME2 NOT NULL CONSTRAINT DF_Movimiento_Fecha DEFAULT SYSDATETIME(),
            CONSTRAINT FK_Movimiento_Usuario FOREIGN KEY (IdUsuario) REFERENCES dbo.Usuario(IdUsuario),
            CONSTRAINT FK_Movimiento_Categoria FOREIGN KEY (IdCategoria) REFERENCES dbo.Categoria(IdCategoria),
            CONSTRAINT FK_Movimiento_MovimientoFijo FOREIGN KEY (IdMovimientoFijo) REFERENCES dbo.MovimientoFijo(IdMovimientoFijo),
            CONSTRAINT CK_Movimiento_Monto CHECK (Monto > 0)
        );
        CREATE INDEX IX_Movimiento_UsuarioFecha ON dbo.Movimiento(IdUsuario, FechaMovimiento) INCLUDE (TipoMovimiento, Monto, IdCategoria);
        CREATE INDEX IX_Movimiento_Fijo ON dbo.Movimiento(IdMovimientoFijo);
    END;

    IF OBJECT_ID(N'dbo.Meta', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Meta (
            IdMeta INT IDENTITY(1,1) CONSTRAINT PK_Meta PRIMARY KEY,
            IdUsuario INT NOT NULL,
            NombreMeta NVARCHAR(100) NOT NULL,
            Descripcion NVARCHAR(500) NULL,
            MontoObjetivo DECIMAL(18,2) NOT NULL,
            FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_Meta_FechaCreacion DEFAULT SYSDATETIME(),
            FechaLimite DATE NULL,
            EstadoMeta NVARCHAR(20) NOT NULL CONSTRAINT DF_Meta_Estado DEFAULT N'Activa',
            IconoMeta NVARCHAR(100) NULL,
            CONSTRAINT FK_Meta_Usuario FOREIGN KEY (IdUsuario) REFERENCES dbo.Usuario(IdUsuario),
            CONSTRAINT CK_Meta_MontoObjetivo CHECK (MontoObjetivo > 0),
            CONSTRAINT CK_Meta_Nombre CHECK (LEN(LTRIM(RTRIM(NombreMeta))) > 0),
            CONSTRAINT CK_Meta_FechaLimite CHECK (FechaLimite IS NULL OR FechaLimite >= CONVERT(DATE, FechaCreacion))
        );
        CREATE INDEX IX_Meta_Usuario ON dbo.Meta(IdUsuario);
    END;

    IF OBJECT_ID(N'dbo.MovimientoMeta', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.MovimientoMeta (
            IdMovimientoMeta INT IDENTITY(1,1) CONSTRAINT PK_MovimientoMeta PRIMARY KEY,
            IdMeta INT NOT NULL,
            Monto DECIMAL(18,2) NOT NULL,
            FechaMovimientoMeta DATETIME2 NOT NULL CONSTRAINT DF_MovimientoMeta_Fecha DEFAULT SYSDATETIME(),
            Descripcion NVARCHAR(500) NULL,
            Origen NVARCHAR(20) NOT NULL CONSTRAINT DF_MovimientoMeta_Origen DEFAULT N'Manual',
            TipoOperacion NVARCHAR(10) NOT NULL,
            CONSTRAINT FK_MovimientoMeta_Meta FOREIGN KEY (IdMeta) REFERENCES dbo.Meta(IdMeta),
            CONSTRAINT CK_MovimientoMeta_Monto CHECK (Monto > 0),
            CONSTRAINT CK_MovimientoMeta_Tipo CHECK (TipoOperacion IN (N'Aporte', N'Retiro'))
        );
        CREATE INDEX IX_MovimientoMeta_MetaFecha ON dbo.MovimientoMeta(IdMeta, FechaMovimientoMeta) INCLUDE (Monto, TipoOperacion);
    END;

    IF OBJECT_ID(N'dbo.AporteProgramado', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AporteProgramado (
            IdAporteProgramado INT IDENTITY(1,1) CONSTRAINT PK_AporteProgramado PRIMARY KEY,
            IdMeta INT NOT NULL,
            MontoProgramado DECIMAL(18,2) NOT NULL,
            FechaProgramada DATE NOT NULL,
            Frecuencia NVARCHAR(20) NOT NULL,
            CONSTRAINT FK_AporteProgramado_Meta FOREIGN KEY (IdMeta) REFERENCES dbo.Meta(IdMeta),
            CONSTRAINT CK_AporteProgramado_Monto CHECK (MontoProgramado > 0),
            CONSTRAINT CK_AporteProgramado_Frecuencia CHECK (LEN(LTRIM(RTRIM(Frecuencia))) > 0)
        );
        CREATE INDEX IX_AporteProgramado_MetaFecha ON dbo.AporteProgramado(IdMeta, FechaProgramada);
    END;

    IF OBJECT_ID(N'dbo.PresupuestoGeneral', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.PresupuestoGeneral (
            IdPresupuesto INT IDENTITY(1,1) CONSTRAINT PK_PresupuestoGeneral PRIMARY KEY,
            IdUsuario INT NOT NULL,
            Mes TINYINT NOT NULL,
            Anio SMALLINT NOT NULL,
            MontoLimite DECIMAL(18,2) NOT NULL,
            FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_PresupuestoGeneral_Fecha DEFAULT SYSDATETIME(),
            CONSTRAINT FK_PresupuestoGeneral_Usuario FOREIGN KEY (IdUsuario) REFERENCES dbo.Usuario(IdUsuario),
            CONSTRAINT UQ_PresupuestoGeneral_Periodo UNIQUE (IdUsuario, Anio, Mes),
            CONSTRAINT CK_PresupuestoGeneral_Mes CHECK (Mes BETWEEN 1 AND 12),
            CONSTRAINT CK_PresupuestoGeneral_Anio CHECK (Anio BETWEEN 1 AND 9999),
            CONSTRAINT CK_PresupuestoGeneral_Monto CHECK (MontoLimite >= 0)
        );
    END;

    IF OBJECT_ID(N'dbo.PresupuestoCategoria', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.PresupuestoCategoria (
            IdPresupuestoCategoria INT IDENTITY(1,1) CONSTRAINT PK_PresupuestoCategoria PRIMARY KEY,
            IdPresupuesto INT NOT NULL,
            IdCategoria INT NOT NULL,
            MontoLimite DECIMAL(18,2) NOT NULL,
            CONSTRAINT FK_PresupuestoCategoria_General FOREIGN KEY (IdPresupuesto) REFERENCES dbo.PresupuestoGeneral(IdPresupuesto),
            CONSTRAINT FK_PresupuestoCategoria_Categoria FOREIGN KEY (IdCategoria) REFERENCES dbo.Categoria(IdCategoria),
            CONSTRAINT UQ_PresupuestoCategoria_Categoria UNIQUE (IdPresupuesto, IdCategoria),
            CONSTRAINT CK_PresupuestoCategoria_Monto CHECK (MontoLimite >= 0)
        );
    END;

    IF OBJECT_ID(N'dbo.Notificacion', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Notificacion (
            IdNotificacion INT IDENTITY(1,1) CONSTRAINT PK_Notificacion PRIMARY KEY,
            IdUsuario INT NOT NULL,
            Titulo NVARCHAR(150) NOT NULL,
            Mensaje NVARCHAR(1000) NOT NULL,
            TipoNotificacion NVARCHAR(30) NOT NULL,
            FechaCreacion DATETIME2 NOT NULL CONSTRAINT DF_Notificacion_Fecha DEFAULT SYSDATETIME(),
            FechaLectura DATETIME2 NULL,
            CONSTRAINT FK_Notificacion_Usuario FOREIGN KEY (IdUsuario) REFERENCES dbo.Usuario(IdUsuario),
            CONSTRAINT CK_Notificacion_Fechas CHECK (FechaLectura IS NULL OR FechaLectura >= FechaCreacion)
        );
        CREATE INDEX IX_Notificacion_UsuarioFecha ON dbo.Notificacion(IdUsuario, FechaCreacion);
    END;

    -- Categorias iniciales sugeridas. No se crean usuarios ni movimientos ficticios.
    INSERT INTO dbo.Categoria (IdUsuario, NombreCategoria, TipoCategoria)
    SELECT NULL, datos.Nombre, datos.Tipo
    FROM (VALUES
        (N'Sueldo', N'Ingreso'),
        (N'Otros ingresos', N'Ingreso'),
        (N'Alimentacion', N'Gasto'),
        (N'Transporte', N'Gasto'),
        (N'Servicios', N'Gasto'),
        (N'Salud', N'Gasto'),
        (N'Entretenimiento', N'Gasto'),
        (N'Otros gastos', N'Gasto')
    ) AS datos(Nombre, Tipo)
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.Categoria AS c
        WHERE c.IdUsuario IS NULL AND c.NombreCategoria = datos.Nombre AND c.TipoCategoria = datos.Tipo
    );

    COMMIT TRANSACTION;
    PRINT N'MoneyUp: creacion finalizada correctamente.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

-- Verificacion: deben aparecer las 10 tablas del modelo.
SELECT name AS Tabla
FROM sys.tables
WHERE schema_id = SCHEMA_ID(N'dbo')
ORDER BY name;

SELECT IdCategoria, NombreCategoria, TipoCategoria, IdUsuario
FROM dbo.Categoria
ORDER BY TipoCategoria, NombreCategoria;
GO

/* REGLAS PARA LA CAPA C# (estas reglas NO las garantizan las FK solas):
   1. Obtener el IdUsuario de la sesion; nunca confiar en el enviado por el formulario.
   2. Solo permitir categorias predeterminadas o propias y compatibles con ingreso/gasto.
      PresupuestoCategoria solo usa categorias de gasto disponibles para ese usuario.
   3. Si Movimiento tiene IdMovimientoFijo, comprobar mismo usuario, categoria y tipo.
   4. Guardar/verificar ContrasenaHash con un hasher de contrasenas de ASP.NET Core.
   5. Los movimientos fijos y aportes programados NO cambian saldos automaticamente.
      Crear el movimiento real solamente cuando el usuario confirme.
   6. Aportes y retiros de metas solo se guardan en MovimientoMeta, sin duplicarlos
      en Movimiento. Saldo disponible = ingresos - gastos - aportes + retiros.
      Saldo ahorrado por meta = aportes - retiros.
   7. Validar retiros <= ahorro de la meta dentro de una transaccion con bloqueo,
      para impedir que dos retiros simultaneos superen el ahorro disponible.
   8. La suma de presupuestos por categoria no debe superar el general si se usan
      como subdivisiones. Validar tambien al reducir el presupuesto general.
   9. Guardar fechas del sitio con una convencion horaria consistente.

   PENDIENTES DE MODELO:
   - Up: conversaciones y mensajes del DER aun no trasladados al DLR.
   - Historial de confirmacion/omision POR OCURRENCIA de planes recurrentes.
     AporteProgramado representa por ahora la configuracion, no ese historial.
     MovimientoFijo tambien necesita esta definicion para evitar confirmaciones duplicadas.
   - Nombres y opciones definitivos de frecuencias, estados, iconos y categorias.

   No se agregaron columnas de saldo, edad ni progreso: se calculan al consultar.
   Sin borrado en cascada: las FK protegen registros relacionados.
   Conservar este archivo en el repositorio. La base local vive en esta computadora;
   otros integrantes ejecutan el mismo script en sus servidores locales.
*/
