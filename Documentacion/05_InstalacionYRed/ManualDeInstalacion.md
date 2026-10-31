# Instalación y conexión en red

## Equipo servidor

Windows con Visual Studio 2022 y Desarrollo de escritorio .NET para compilar. SDK 9.0.3xx fijado en global.json; destino de las apps .NET 8. SQL Server 2019 o posterior (Express/Developer) y SSMS. En esta PC: Visual Studio 17.14.39, SDK 9.0.317, SQL Server Express 2022 16.0.1210.3, instancia `.\SQLEXPRESS`, SSMS 22.9.2. Estos son resultados locales, no requisitos exactos para todos los equipos.

Abrir PowerShell en la raíz de la solución y ejecutar `./Instalar-BaseDeDatos.ps1 -Servidor '.\SQLEXPRESS'`. Usa autenticación Windows y aplica scripts por orden. Se detiene si ya existe el esquema para evitar reinicializar datos. Para una instancia distinta, editar el ejemplo de conexión o usar la pantalla Configurar SQL Server del servidor. El nombre de la base de esta entrega es BD_CajeroRed_Fase2.

Compilar con `dotnet restore CajeroRed.sln` y `dotnet build CajeroRed.sln -c Release`. Visual Studio puede realizar esos pasos al abrir y compilar la solución. En esta PC se restauraron las bibliotecas disponibles localmente; en una instalación nueva NuGet puede necesitar Internet.

El servidor puede iniciarse por `Iniciar-Servidor.cmd` o desde Visual Studio. Login admin / GrupoCajero#2026 para la demostración. Pulsar Iniciar servicio. Inicializar cada cajero para el día; no se inicializa automáticamente al conectar un cliente.

## Usuario de SQL con permisos limitados

El script 07 crea el rol `rol_CajeroServidor` y el usuario de base `CajeroServidor WITHOUT LOGIN`. Cada conexión del proceso C# entra con la identidad configurada y ejecuta `EXECUTE AS USER='CajeroServidor' WITH NO REVERT`. El contexto efectivo puede consultar y ejecutar procedimientos, registrar bitácora y actualizar monitoreo; no puede escribir saldos directamente. Se comprobó que UPDATE Usuario falla con ese contexto. Pooling=False cierra físicamente la conexión al terminar. No usa sa.

Para desplegar con una identidad Windows dedicada existente, un administrador de SQL ejecuta, sustituyendo el nombre real:

```sql
USE master;
CREATE LOGIN [EQUIPO\CuentaServidor] FROM WINDOWS;
GO
USE BD_CajeroRed_Fase2;
CREATE USER [EQUIPO\CuentaServidor] FOR LOGIN [EQUIPO\CuentaServidor];
ALTER ROLE rol_CajeroServidor ADD MEMBER [EQUIPO\CuentaServidor];
```

Ejecutar el servidor bajo esa cuenta y mantener Integrated Security=True. Si el login ya existe, omitir CREATE LOGIN. No agregarlo a sysadmin ni db_owner. La cuenta con la que se instalan scripts y se hace el respaldo puede tener permisos mayores; debe distinguirse de la cuenta de ejecución.

Si el DBA ya habilitó autenticación SQL, también puede crear un login propio con contraseña y asignarlo al rol. No hace falta cambiar el modo Windows instalado en esta PC. La utilidad de prueba admite `--crear-login` solo cuando el modo mixto está habilitado y se solicita expresamente; guarda una configuración privada sin imprimir la clave. Nunca copiar esa configuración al cajero ni a GitHub.

## Configuración y cambio de router o teléfono

Todos los equipos deben estar en la misma LAN o Wi-Fi. El servidor identifica la IP por adaptador activo con gateway IPv4; omite loopback y direcciones 169.254.*. Elegir la interfaz conectada a los demás equipos. `127.0.0.1` solo sirve cuando servidor y cajero están en el mismo equipo.

El cajero tiene Configuración de conexión: dirección, puerto y código único. Guarda `cajero.local.json` junto al ejecutable, fuera del control de versiones. El archivo de ejemplo no contiene cadena SQL. Para varias instancias en una PC, usar argumentos `--cajero ATM-02 --servidor IP --puerto 5000`; cada código debe estar dado de alta. No ejecutar dos instancias con el mismo código.

Al cambiar de router, la dirección IP asignada por DHCP puede cambiar:

1. Conectar el servidor y los clientes a la nueva red.
2. Iniciar el servicio y consultar la IP mostrada en el servidor.
3. Cambiar la dirección en cada cajero y pulsar Probar conexión.
4. Confirmar el registro en el monitor del dashboard.

Un punto de acceso de teléfono funciona únicamente si permite comunicación entre sus clientes. Algunos routers/redes de invitados aíslan dispositivos. Si Probar conexión falla, revisar IP, aislamiento de clientes, puerto y firewall antes de cambiar código. Una reserva DHCP en el router puede mantener una IP estable, si se tiene acceso a administrarlo.

## Firewall

Abrir solo el puerto TCP de la aplicación en el equipo servidor. Ejemplo en PowerShell con permisos administrativos, para una LAN marcada como Privada:

```powershell
New-NetFirewallRule -DisplayName 'CajeroRed Fase II TCP 5000' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5000 -Profile Private
```

Si se cambia el puerto en Configuración del dashboard, detener/iniciar el servicio, actualizar los cajeros y ajustar la regla. No es necesario exponer SQL Server a los equipos cliente: SQL permanece local al servidor. La creación de esta regla se deja para la red real; no se abre automáticamente el firewall al compilar.

## Distribución y respaldo

`Publicar.ps1` genera ejecutables portables Windows x64 con .NET incluido. Cada cajero recibe únicamente `Distribucion/Cajero`; el servidor recibe su carpeta y los scripts/manuales necesarios. Mantener la carpeta completa y permiso de escritura local para configuración. Configurar IP y código en cada PC antes de la demostración.

Restaurar el `.bak` desde SSMS, Tareas/Restaurar base de datos, con una cuenta autorizada y rutas de archivos ajustadas al equipo destino. No sobrescribir una base existente sin un respaldo previo y una decisión explícita. Un respaldo SQL Server 2022 necesita SQL Server 2022 o posterior para restaurarse; para SQL 2019 usar los scripts y datos de prueba.

La cuenta WITHOUT LOGIN y sus permisos viajan dentro del respaldo. Los logins de servidor externos no viajan con el `.bak`; mapearlos en la nueva instalación si se usan. La fecha de inicialización guardada corresponde al día del respaldo; inicializar nuevamente cuando corresponda.

Respaldar.ps1 escribe directamente en BaseDeDatos/Respaldos y concede Modify a la cuenta del servicio SQL únicamente en esa carpeta propia. Para otra instancia, pasar -CuentaServicio con su identidad real. Así el servicio puede crear el archivo y el propietario del proyecto puede leerlo sin copiar desde Program Files.

## Actualización NETGEAR del 31/10/2026
La versión del SDK se selecciona desde 9.0.300 con latestFeature; en este equipo se verifica con 9.0.311. Los datos de versión anteriores de este manual son históricos.
Después de restaurar el respaldo anterior, aplicar SOLO Actualizar-Interbancario.ps1 con la instancia correcta, sin reinstalar la base.
Configurar-Firewall.ps1 permite TCP 5000 y UDP 5005 para el ejecutable y LocalSubnet, incluso si el perfil de la red de pruebas es Público.
Leer RevisionServidor_2026-10-31.md para reservas DHCP independientes de Wi-Fi/Ethernet y desactivar MAC aleatoria del perfil Wi-Fi antes de reservarla.
Bancos de la red → Mi banco NETGEAR: confirmar código único; BANC02 es provisional. Configurar otros bancos con su IP real, confirmar confianza y realizar pruebas de conectividad antes de transferir.
