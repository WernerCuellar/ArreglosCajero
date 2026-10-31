# Manual del servidor y dashboard

1. Ejecutar Servidor.exe o Iniciar-Servidor.cmd. Si SQL está en otra instancia, pulsar Configurar SQL Server antes de iniciar sesión. La cadena se guarda únicamente en el equipo servidor.
2. Ingresar usuario y contraseña administrativa. Los datos de demostración están en LEEME.md. Los intentos fallidos quedan registrados sin contraseña.
3. Pulsar Iniciar servicio. El encabezado muestra estado, número de conexiones, direcciones de red y puerto. Elegir la IP de la LAN correcta si aparecen adaptadores virtuales. Detener cierra las conexiones y permite reiniciar la escucha.

## Resumen

Indicadores del día: cajeros en línea/total, efectivo de la red, retirado, depositado, promedio depositado por usuario, cantidad de operaciones, rechazos, cambios de PIN y alertas activas. También muestra cuántos usuarios cambiaron PIN y el último acceso con fecha completa y cajero. Gráficas de retiros/depósitos por hora, montos por cajero y billetes por denominación. La selección del cajero se conserva entre refrescos. Actualización cada cuatro segundos.

Captura: `Capturas/servidor_resumen.png`.

## Cajeros

Seleccione una fila antes de usar acciones sobre un cajero. Nuevo cajero solicita código único, nombre y ubicación. Editar conserva el código. Baja lógica conserva su historial y deshabilita operaciones. Inicializar hoy recibe cantidades por denominación y confirma el monto; máximo Q10000, una sola inicialización al día. Agregar efectivo requiere inicialización del día y no puede superar Q30000. Toda carga se registra con administrador y detalle.

Habilitar/Fuera de servicio actualiza el estado autoritativo en SQL y envía el comando al cliente conectado. Un cliente desconectado recibe el estado correcto al registrarse o en el siguiente heartbeat. Enviar mensaje muestra una ventana en el cajero; actualizar inventario provoca una consulta del estado. Los estados están identificados por texto y color, de modo que no dependen solo del color.

Captura: `Capturas/servidor_cajeros.png`.

## Usuarios

Crear usuario abre un formulario breve: nombre, tarjeta, PIN, confirmación, saldo y límite. Modificar nombre no afecta su dinero. Cambiar límite registra anterior/nuevo y no reinicia lo retirado hoy. Cambiar tarjeta desactiva la anterior, guarda historial y conserva el límite consumido. El PIN asignado en un reemplazo o restablecimiento es temporal y debe cambiarse al ingresar.

Bloquear/Desbloquear son acciones globales. La baja es lógica. Historial del usuario abre tres pestañas: movimientos, tarjetas enmascaradas y cambios de límite. La tabla principal muestra saldo, límite, disponible, máximo retirable, estado e información del último acceso.

Captura: `Capturas/servidor_usuarios.png`.

## Consultas, alertas y reportes

Transacciones reúne movimientos aprobados y rechazados con motivo, desglose y referencia. Depósitos y Retiros permiten elegir fechas, buscar texto por usuario/cajero y ver totales y promedio. Cambios de PIN muestra quién, cuándo, origen y resultado; nunca el PIN. Auditoría permite filtrar por fechas/texto. Alertas incluye efectivo bajo, denominaciones agotadas, desconexiones, bloqueos y retiros sin billetes; seleccionar y Marcar atendida conserva quién la atendió.

Arqueo compara inventario actual con apertura + cargas + depósitos - retiros desde la última inicialización. Exportar el cierre antes de inicializar un día nuevo. Resumen diario agrupa por fecha, cajero, tipo y resultado. Exportar Excel genera un archivo .xlsx con la vista y filtros actuales; no contiene hashes ni PIN. La exportación se registra en auditoría.

## Configuración

Puerto, umbral, vigencia del token y máximos se guardan en Parametro. El puerto nuevo se aplica al detener e iniciar el servicio. El token no puede superar 120 s, la apertura Q10000 y la capacidad Q30000; se permiten valores menores que no contradigan inventarios vigentes. El umbral predeterminado es Q1000.

## Token del usuario

Al aprobar tarjeta/PIN se abre una ventana no modal en el servidor con el código de seis dígitos y el cajero. Simula el envío al celular requerido por el documento. El usuario lo escribe en el cajero. Vence en SQL según el parámetro y no puede reutilizarse; la ventana se cierra automáticamente a los dos minutos como máximo. No compartir el token con otros usuarios.

## NETGEAR: login y bancos de la red
El ojo permite mostrar/ocultar la contraseña; se oculta al cambiar de ventana y se limpia al finalizar el intento.
Bancos de la red → Mi banco NETGEAR configura código único y límite. BANC02 es provisional; puede cambiarse mientras no haya historial y no esté ocupado por otro registro.
Escanear red local muestra anuncios UDP y prueba por TCP los bancos guardados. Registrar un anuncio no implica confiar en él: confirmar IP/código con el responsable.
Registrar / editar banco solicita su IP, puerto, límite y autorización 1/0. Solo bancos autorizados pueden enviar transferencias.
Verificar cuenta muestra el titular remoto. Transferir solicita ID de usuario local, cuenta destino, monto y concepto; confirmar con autorización del usuario. Guardar el token del comprobante.
Transferencias permite filtrar/exportar y Consultar / conciliar las salidas. INCIERTA o RESERVADA requieren conciliación; no generar otra transferencia ni devolver fondos por un timeout.
Usuarios → Asignar cuenta permite un número único de 1–20 dígitos; Historial del usuario añade Transferencias con el mismo token.
Logs/conexiones-YYYYMMDD.jsonl junto al ejecutable registra causas técnicas sin PIN ni payload. La fecha del archivo y sus eventos es UTC.
