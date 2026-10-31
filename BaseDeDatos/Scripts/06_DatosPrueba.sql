USE BD_CajeroRed_Fase2;
GO
-- Solo datos ficticios. Clave administrativa de demostración: GrupoCajero#2026.
-- PBKDF2-SHA256, 210000 iteraciones, salt aleatorio de 32 bytes.
IF EXISTS(SELECT 1 FROM dbo.Usuario) THROW 50020,'Este script requiere tablas de usuarios vacías.',1;
INSERT dbo.Administrador(Usuario,Hash,Salt) VALUES(N'admin',0x0909f886ed78fb6dc48980f5e9706b8233d5f1d5a8e35f545ba2337167ff3830,0xe165d0c008403ac5c90a6553ca763b05cfa3535320c9122a9fa53900fc19f69a);
DECLARE @i int=1,@c varchar(24),@salt varchar(64);
WHILE @i<=8
BEGIN
 INSERT dbo.Usuario(Nombre,Saldo,LimiteDiario) VALUES(CONCAT(N'Usuario de prueba ',@i),5000,CASE WHEN @i=1 THEN 500 ELSE 2000 END);
 SET @salt=CONVERT(varchar(64),CRYPT_GEN_RANDOM(32),2);
 INSERT dbo.Tarjeta(UsuarioId,Numero,PinHash,Salt) VALUES(SCOPE_IDENTITY(),CONCAT('400000000000',RIGHT('0000'+CONVERT(varchar(4),@i),4)),HASHBYTES('SHA2_256',CONVERT(varchar(200),@salt+'1234')),@salt);
 SET @i+=1;
END;
SET @i=1;
WHILE @i<=8
BEGIN
 SET @c=CONCAT('ATM-',RIGHT('00'+CONVERT(varchar(3),@i),2));
 INSERT dbo.Cajero(Codigo,Nombre,Ubicacion) VALUES(@c,CONCAT(N'Cajero ',@i),N'Laboratorio');
 INSERT dbo.InventarioCajero SELECT @c,Valor,0 FROM dbo.Denominacion;
 SET @i+=1;
END;
GO
-- Por diseño se inicializan desde el dashboard cada día, no al arrancar el cliente.
