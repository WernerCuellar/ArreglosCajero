-- Ejecutar con una cuenta autorizada para crear bases. Nunca elimina una base existente.
USE master;
GO
IF DB_ID(N'BD_CajeroRed_Fase2') IS NULL CREATE DATABASE BD_CajeroRed_Fase2;
GO
USE BD_CajeroRed_Fase2;
GO
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO
IF OBJECT_ID('dbo.Usuario') IS NOT NULL THROW 50001, 'El esquema ya existe. No vuelva a aplicar los scripts sobre datos existentes.', 1;
GO
BEGIN TRAN;
CREATE TABLE dbo.Usuario(
 Id int IDENTITY PRIMARY KEY, Nombre nvarchar(120) NOT NULL,
 Saldo decimal(18,2) NOT NULL CHECK(Saldo>=0), LimiteDiario decimal(18,2) NOT NULL CHECK(LimiteDiario>=0),
 Activo bit NOT NULL DEFAULT 1, Bloqueado bit NOT NULL DEFAULT 0,
 Intentos int NOT NULL DEFAULT 0 CHECK(Intentos BETWEEN 0 AND 3), Creado datetime2 NOT NULL DEFAULT SYSDATETIME());
CREATE TABLE dbo.Tarjeta(
 Id int IDENTITY PRIMARY KEY, UsuarioId int NOT NULL REFERENCES dbo.Usuario(Id),
 Numero varchar(16) NOT NULL UNIQUE CHECK(LEN(Numero)=16 AND Numero NOT LIKE '%[^0-9]%'),
 PinHash varbinary(32) NOT NULL, Salt varchar(64) NOT NULL,
 Activa bit NOT NULL DEFAULT 1, PinTemporal bit NOT NULL DEFAULT 0,
 Asignada datetime2 NOT NULL DEFAULT SYSDATETIME(), Baja datetime2 NULL);
CREATE UNIQUE INDEX UX_TarjetaActiva ON dbo.Tarjeta(UsuarioId) WHERE Activa=1;
CREATE TABLE dbo.Administrador(
 Id int IDENTITY PRIMARY KEY, Usuario nvarchar(60) NOT NULL UNIQUE,
 Hash varbinary(32) NOT NULL, Salt varbinary(32) NOT NULL, Iteraciones int NOT NULL DEFAULT 210000,
 Rol varchar(20) NOT NULL DEFAULT 'Administrador' CHECK(Rol IN('Administrador','Supervisor')), Activo bit NOT NULL DEFAULT 1);
CREATE TABLE dbo.Cajero(
 Codigo varchar(24) PRIMARY KEY, Nombre nvarchar(100) NOT NULL, Ubicacion nvarchar(120) NOT NULL DEFAULT '',
 IP varchar(45) NULL, Equipo nvarchar(100) NULL, Version nvarchar(40) NULL,
 Activo bit NOT NULL DEFAULT 1, Conectado bit NOT NULL DEFAULT 0, Habilitado bit NOT NULL DEFAULT 1,
 FechaInicializacion date NULL, UltimoHeartbeat datetime2 NULL, UltimaConexion datetime2 NULL);
CREATE TABLE dbo.Denominacion(Valor int PRIMARY KEY CHECK(Valor IN(200,100,50,20,10,5,1)));
INSERT dbo.Denominacion VALUES(200),(100),(50),(20),(10),(5),(1);
CREATE TABLE dbo.InventarioCajero(
 Cajero varchar(24) NOT NULL REFERENCES dbo.Cajero(Codigo), Denominacion int NOT NULL REFERENCES dbo.Denominacion(Valor),
 Cantidad int NOT NULL DEFAULT 0 CHECK(Cantidad BETWEEN 0 AND 30000), PRIMARY KEY(Cajero,Denominacion));
CREATE TABLE dbo.CargaEfectivo(
 Id bigint IDENTITY PRIMARY KEY, Cajero varchar(24) NOT NULL REFERENCES dbo.Cajero(Codigo),
 Tipo varchar(20) NOT NULL CHECK(Tipo IN('INICIALIZACION','CARGA')), Total decimal(18,2) NOT NULL CHECK(Total>0),
 EfectivoAnterior decimal(18,2) NOT NULL, AdministradorId int NOT NULL REFERENCES dbo.Administrador(Id), Fecha datetime2 NOT NULL DEFAULT SYSDATETIME());
CREATE TABLE dbo.CargaEfectivoDetalle(
 CargaId bigint NOT NULL REFERENCES dbo.CargaEfectivo(Id), Denominacion int NOT NULL REFERENCES dbo.Denominacion(Valor),
 Cantidad int NOT NULL CHECK(Cantidad>=0), PRIMARY KEY(CargaId,Denominacion));
CREATE TABLE dbo.Sesion(
 Id varchar(36) PRIMARY KEY DEFAULT CONVERT(varchar(36),NEWID()), UsuarioId int NULL REFERENCES dbo.Usuario(Id),
 TarjetaId int NULL REFERENCES dbo.Tarjeta(Id), Cajero varchar(24) NOT NULL REFERENCES dbo.Cajero(Codigo),
 Inicio datetime2 NOT NULL DEFAULT SYSDATETIME(), UltimaActividad datetime2 NOT NULL DEFAULT SYSDATETIME(),
 Fin datetime2 NULL, Resultado varchar(32) NOT NULL);
CREATE TABLE dbo.Token(
 Id bigint IDENTITY PRIMARY KEY, UsuarioId int NOT NULL REFERENCES dbo.Usuario(Id), TarjetaId int NOT NULL REFERENCES dbo.Tarjeta(Id),
 Cajero varchar(24) NOT NULL REFERENCES dbo.Cajero(Codigo), CodigoHash varbinary(32) NOT NULL,
 Expira datetime2 NOT NULL, Usado bit NOT NULL DEFAULT 0, Creado datetime2 NOT NULL DEFAULT SYSDATETIME());
CREATE TABLE dbo.Transaccion(
 Id bigint IDENTITY PRIMARY KEY, UsuarioId int NULL REFERENCES dbo.Usuario(Id), TarjetaId int NULL REFERENCES dbo.Tarjeta(Id),
 Cajero varchar(24) NOT NULL REFERENCES dbo.Cajero(Codigo), Solicitud varchar(64) NOT NULL,
 Tipo varchar(20) NOT NULL CHECK(Tipo IN('RETIRO','DEPOSITO')), Monto decimal(18,2) NOT NULL CHECK(Monto>=0),
 CodigoRespuesta varchar(2) NOT NULL, Motivo nvarchar(240) NOT NULL, Fecha datetime2 NOT NULL DEFAULT SYSDATETIME(),
 UNIQUE(Cajero,Solicitud));
CREATE INDEX IX_TransaccionUsuarioFecha ON dbo.Transaccion(UsuarioId,Fecha) INCLUDE(Tipo,Monto,CodigoRespuesta);
CREATE TABLE dbo.TransaccionDetalle(
 TransaccionId bigint NOT NULL REFERENCES dbo.Transaccion(Id), Denominacion int NOT NULL REFERENCES dbo.Denominacion(Valor),
 Cantidad int NOT NULL CHECK(Cantidad>=0), PRIMARY KEY(TransaccionId,Denominacion));
CREATE TABLE dbo.CambioPin(
 Id bigint IDENTITY PRIMARY KEY, UsuarioId int NOT NULL REFERENCES dbo.Usuario(Id), Cajero varchar(24) NULL REFERENCES dbo.Cajero(Codigo),
 Fecha datetime2 NOT NULL DEFAULT SYSDATETIME(), Origen varchar(20) NOT NULL, Resultado nvarchar(120) NOT NULL);
CREATE TABLE dbo.HistorialLimite(
 Id bigint IDENTITY PRIMARY KEY, UsuarioId int NOT NULL REFERENCES dbo.Usuario(Id), Anterior decimal(18,2) NOT NULL,
 Nuevo decimal(18,2) NOT NULL, AdministradorId int NOT NULL REFERENCES dbo.Administrador(Id), Fecha datetime2 NOT NULL DEFAULT SYSDATETIME());
CREATE TABLE dbo.Alerta(
 Id bigint IDENTITY PRIMARY KEY, Tipo varchar(40) NOT NULL, Cajero varchar(24) NULL REFERENCES dbo.Cajero(Codigo),
 Descripcion nvarchar(250) NOT NULL, Fecha datetime2 NOT NULL DEFAULT SYSDATETIME(), Atendida bit NOT NULL DEFAULT 0,
 AdministradorId int NULL REFERENCES dbo.Administrador(Id), FechaAtencion datetime2 NULL);
CREATE TABLE dbo.Bitacora(
 Id bigint IDENTITY PRIMARY KEY, Origen nvarchar(60) NOT NULL, Accion nvarchar(80) NOT NULL,
 Detalle nvarchar(500) NOT NULL, Fecha datetime2 NOT NULL DEFAULT SYSDATETIME());
CREATE TABLE dbo.Parametro(Clave varchar(40) PRIMARY KEY, Valor decimal(18,2) NOT NULL CHECK(Valor>0));
INSERT dbo.Parametro VALUES('Puerto',5000),('UmbralEfectivo',1000),('VigenciaToken',120),('MaxInicializacion',10000),('MaxEfectivo',30000);
CREATE TABLE dbo.SolicitudOperacion(
 Cajero varchar(24) NOT NULL REFERENCES dbo.Cajero(Codigo), Solicitud varchar(64) NOT NULL, UsuarioId int NOT NULL REFERENCES dbo.Usuario(Id),
 Tipo varchar(40) NOT NULL, Huella varbinary(32) NOT NULL, Codigo varchar(2) NOT NULL,
 Mensaje nvarchar(240) NOT NULL, Datos nvarchar(max) NOT NULL CHECK(ISJSON(Datos)=1), Fecha datetime2 NOT NULL DEFAULT SYSDATETIME(),
 PRIMARY KEY(Cajero,Solicitud));
COMMIT;
GO
