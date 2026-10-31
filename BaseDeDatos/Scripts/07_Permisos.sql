USE BD_CajeroRed_Fase2;
GO
IF DATABASE_PRINCIPAL_ID('rol_CajeroServidor') IS NULL CREATE ROLE rol_CajeroServidor;
-- Solo lectura de tablas del esquema, procedimientos y las escrituras de monitoreo.
GRANT SELECT ON SCHEMA::dbo TO rol_CajeroServidor;
GRANT EXECUTE ON SCHEMA::dbo TO rol_CajeroServidor;
GRANT INSERT ON dbo.Bitacora TO rol_CajeroServidor;
GRANT UPDATE ON dbo.Cajero TO rol_CajeroServidor;
GRANT UPDATE ON dbo.Sesion TO rol_CajeroServidor;
IF DATABASE_PRINCIPAL_ID('CajeroServidor') IS NULL CREATE USER CajeroServidor WITHOUT LOGIN;
ALTER ROLE rol_CajeroServidor ADD MEMBER CajeroServidor;
GRANT IMPERSONATE ON USER::CajeroServidor TO rol_CajeroServidor;
-- En autenticación Windows el servidor abre la conexión y adopta este usuario
-- de base de datos con EXECUTE AS USER; cada conexión se cierra sin pooling.
-- No cambia el modo de autenticación de SQL ni requiere reiniciar el servicio.
-- El instalador configura un login propio. Nunca se incluye ni se usa sa.
-- Ejemplo para un login YA CREADO por el DBA:
-- CREATE USER CajeroServidor FOR LOGIN CajeroServidor;
-- ALTER ROLE rol_CajeroServidor ADD MEMBER CajeroServidor;
GO
