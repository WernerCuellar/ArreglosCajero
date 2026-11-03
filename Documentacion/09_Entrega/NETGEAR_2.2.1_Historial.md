# NETGEAR Servidor 2.2.1 — historial de transferencias

Se corrige la presentación de transferencias en el servidor. No se modifican saldos ni se reconstruyen movimientos.

- Transferencias aparece después de Usuarios en el menú lateral y también tiene acceso desde Resumen.
- Muestra envíos internos y entre bancos, con origen, destino, nombres de bancos, cuentas enmascaradas, monto, estado y token.
- El listado global muestra una fila por envío interno. El historial de cada cuenta conserva su débito o abono.
- Transacciones incluye también TRANSFERENCIA_ENVIADA y TRANSFERENCIA_RECIBIDA.
- Usuarios → seleccionar cuenta → Historial del usuario → Movimientos incluye los movimientos tradicionales y transferencias. La pestaña Transferencias conserva su detalle.
- Transacciones y Transferencias abren los últimos 30 días. Todo el historial amplía el rango desde 1900 hasta hoy; también se puede elegir fechas.
- Los totales de Transferencias suman solo aprobadas y cuentan por separado las pendientes. Los rechazos no suman al monto aprobado.
- Consultar / conciliar se limita a transferencias interbancarias salientes pendientes.

No requiere una migración SQL nueva: utiliza el esquema 08–11 de la versión 2.2.0.
No requiere actualizar el cajero 2.2.0.
Los registros históricos existentes se consultan directamente; no volver a enviar dinero para que aparezcan.

Validación: 42 comprobaciones de transferencias con dos bancos SQL aislados y 7 de interfaz del servidor.
Se verifican transferencias internas sin duplicación en el listado global, movimientos por cuenta en ambos sentidos, abonos interbancarios, fechas y totales.
Las otras 114 comprobaciones de 2.2.0 siguen como validación previa; no se presentan como repetidas en esta corrección.

Actualizar: cerrar normalmente el servidor cuando no haya operaciones en curso y abrir Iniciar-Servidor.cmd actualizado,
o descargar NETGEAR_Servidor_2.2.1.exe desde Releases. Iniciar sesión y luego Iniciar servicio.
Si el servidor se descarga en una ruta nueva, configurar SQL y autorizar el ejecutable real en el firewall.
Conservar configuración SQL y referencias pendientes. No reinstalar la base.

El servidor solo puede mostrar transferencias registradas en nuestro banco, incluidas sus operaciones con otros bancos.
No accede al historial privado completo de cuentas de otro grupo.

Fuente y descarga: https://github.com/WernerCuellar/ArreglosCajero
