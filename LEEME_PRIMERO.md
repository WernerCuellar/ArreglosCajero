# CajeroRed · paquete del servidor

Revisión para pruebas del 29 de octubre de 2026. Aplicación portable Windows x64 con .NET incluido. SQL Server se instala por separado.

## Primera instalación en la PC que será servidor

1. Extraer TODO el RAR en una carpeta local con permiso de escritura. No ejecutar dentro de WinRAR.
2. Tener instalado SQL Server 2022 o posterior y SQL Server Management Studio (SSMS). SSMS es el administrador: no sustituye al motor SQL Server. No se necesita Visual Studio para usar los ejecutables.
3. Abrir SSMS y conectarse a SU instancia mediante Autenticación de Windows. El nombre habitual de Express es .\SQLEXPRESS, pero debe comprobarse en esa PC.
4. Clic derecho en Bases de datos > Restaurar base de datos > Dispositivo > ... > Agregar. Seleccionar BaseDeDatos\Respaldos\BD_CajeroRed_Fase2.bak. Destino: BD_CajeroRed_Fase2.
5. En Archivos revisar/reubicar las rutas MDF y LDF en carpetas de datos válidas de esa instancia. Confirmar la restauración. Si SQL no puede leer el respaldo desde la carpeta del usuario, copiar el BAK a la carpeta de respaldos de su instancia y seleccionarlo allí. No sobrescribir una base existente sin revisarla y respaldarla antes.
6. NO ejecutar Instalar-BaseDeDatos.ps1 después de restaurar. El respaldo ya contiene tablas, procedimientos, vistas, permisos y datos. Los scripts se conservan para consultar el proyecto o crear otra instalación vacía.
7. Abrir Iniciar-Servidor.cmd. Antes del login, usar Configurar SQL Server y guardar la cadena de SU instancia. Ejemplo para Express:

   Server=.\SQLEXPRESS;Database=BD_CajeroRed_Fase2;Integrated Security=True;Encrypt=True;TrustServerCertificate=True

   Si la instancia cambia, sustituir el valor de Server. La cuenta Windows que abre la aplicación necesita acceso autorizado a la base. El usuario interno y su rol ya viajan en el respaldo; los logins Windows de otra PC no viajan. El manual de instalación explica cómo mapear la identidad si fuera necesario.
8. Acceso administrativo ficticio de demostración: usuario admin; contraseña GrupoCajero#2026. Pulsar Iniciar servicio.
9. Consultar ipconfig y compartir la IPv4 del adaptador usado por los clientes. No usar la puerta de enlace, 127.0.0.1 ni la IP de un adaptador virtual para clientes de otra PC. Puerto de la aplicación: 5000 por defecto.
10. Permitir TCP 5000 en el firewall del servidor para la red de pruebas según Documentacion/05_InstalacionYRed/ManualDeInstalacion.md. SQL permanece local: los clientes no requieren conexión al puerto SQL ni instalación de SQL Server.
11. Abrir los cajeros del otro RAR con códigos distintos. En Cajeros, seleccionar cada uno > Inicializar hoy, una vez por día, hasta Q10,000. Esta operación establece el inventario inicial, no se suma al anterior. Habilitar si corresponde.
12. Comprobar saldo, un depósito, un retiro y su registro en Transacciones/Arqueo. Después completar el acta de pruebas físicas con los integrantes.

Ejemplo de inicialización Q10,000: Q200 x20; Q100 x20; Q50 x20; Q20 x50; Q10 x50; Q5 x100; Q1 x1000.

## Respaldo y credenciales

BaseDeDatos/Respaldos/ESTADO_DEL_RESPALDO.md informa fecha y verificación del BAK incluido. Se conserva el estado actual de las pruebas, sin reinicializar usuarios ni movimientos.

Los datos de 06_DatosPrueba.sql son la semilla de una instalación NUEVA. En el respaldo algunas tarjetas y PIN cambiaron. Si no se conoce el PIN, usar Usuarios > Restablecer PIN. El usuario deberá cambiar el PIN temporal al ingresar. El PIN no se puede recuperar de su hash.

La contraseña anterior es de la aplicación, no de SQL Server. La cadena de ejemplo usa la identidad Windows. Nunca copiar una configuración SQL al cajero.

Para nuevas copias, Respaldar.ps1 crea y verifica un BAK. Si la instancia o la cuenta del servicio son distintas, usar sus parámetros documentados.

## Localizar archivos y explicar el proyecto

Abrir Documentacion/00_Indice/GuiaRapidaDeRevision.md. Se incluyen scripts, documentación PDF, código fuente y pruebas como referencia. Para operar usar Iniciar-Servidor.cmd; no hace falta compilar la solución.

El fuente del cajero forma parte de la solución y está actualizado a NETGEAR 2.1.0. Para entregar el portable a las PCs cliente, ejecutar Publicar-Cajero.ps1 y usar el paquete CajeroRed_Cajero.

La documentación de resultados conserva las fechas reales de pruebas anteriores. No demuestra que esta instalación nueva ya fue validada. La integración NETGEAR se implementó conforme al MD recibido el 31/10/2026. Aplicar la migración 08 después de restaurar el respaldo antiguo y consultar Documentacion/08_IntegracionEntreGrupos. La prueba física con otros grupos sigue pendiente.
Actualización 31/10/2026: después de restaurar el BAK anterior, aplicar Actualizar-Interbancario.ps1 para 08/09. Nunca reinstalar los datos sobre una base restaurada.
