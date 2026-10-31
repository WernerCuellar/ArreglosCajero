# Protocolo implementado — NETGEAR

Referencia: [MD recibido](PROTOCOLO_CONEXION_BANCOS_LLM.md), entregado el 31/10/2026. Se conserva intacto; esta página identifica implementación y extensiones.

## Transporte
TCP IPv4 en IPAddress.Any, mismo listener que los cajeros, puerto configurable 5000 por defecto.
Antes de registrar un ATM se inspecciona tipo sin distinguir mayúsculas. Una petición IB recibe una respuesta RESP_OK correlacionada por idMensaje y se cierra. Los cajeros conservan su conexión persistente.
UTF-8, JSON por línea con LF, máximo 32 KiB; espera del cliente IB de diez segundos.
UDP 5005 recibe DISCOVER_BANCO_ATM y responde HERE_BANCO_ATM al remitente, con importes invariantes e IP local de la ruta hacia ese remitente.
Escanear usa broadcast limitado y por adaptador, y ping TCP a bancos registrados. No barre una /8 de Radmin. Si no hay broadcast, registrar IP y hacer ping. UDP no descubre otra subred automáticamente.

## Operaciones
IB_PING devuelve identidad y cupo propio.
IB_VERIFICAR_CUENTA acepta numeroCuenta o cuentaDestino; busca NumeroCuenta (único, 1–20 dígitos) o tarjeta activa de usuario activo y no bloqueado.
IB_TRANSFERIR valida token TRF-YYYYMMDD-XXXXXX, monto positivo de hasta dos decimales, cuenta y banco destino. Solo confirma después del COMMIT SQL.
BANC02 es provisional para NETGEAR; confirmar código único. BANC01 pertenece a NEXUSBANK según el MD.

## Autorización
Los anuncios no conceden permiso. Confirmar IP y código con el otro grupo y registrar Confiable=1.
IB_TRANSFERIR e IB_ESTADO_TRANSFERENCIA exigen bancoOrigen registrado, autorizado, activo y con IP coincidente con el TCP remitente.
El MD no define firmas, secretos ni TLS. El control por IP mantiene compatibilidad con los campos acordados; no equivale a identidad criptográfica. Usar la red de pruebas con datos ficticios. No compartir PIN, hashes ni acceso SQL.

## Datos, cupos e historial
08_Interbancario.sql añade BancoRed, TransferenciaBancaria, NumeroCuenta en Usuario y vw_TransferenciasUsuario. No reinicializa usuarios o inventarios.
Un bloqueo de aplicación transaccional ordena operaciones; saldo, registro y ambos contadores bancarios locales cambian en una transacción. Fecha y reinicio diario proceden de SQL.
Cada institución es autoridad de su saldo y cupo propio. El registro local del otro banco refleja operaciones conocidas y no puede conocer sus transferencias con terceros. El destino vuelve a comprobar su cupo al acreditar. La coordinación global de toda la red requiere un acuerdo adicional.
Las transferencias tienen historial separado de retiros y depósitos físicos; no alteran billetes ni arqueo. El token une el movimiento del usuario y su registro bancario. Consultar Usuarios → Historial → Transferencias o la página Transferencias, exportable a Excel.
El límite de retiro del usuario no se consume: el MD define cupos de banco para transferencias. Cualquier regla adicional debe acordarse.
Se recomienda cuenta de longitud distinta a tarjeta para evitar ambigüedades futuras. La asignación rechaza cuentas que ya sean tarjetas de otro usuario.

## Duplicados y respuesta perdida
Clave única: (bancoOrigen, tokenTransferencia). Repetir una entrada aprobada con iguales datos devuelve su confirmación sin duplicar saldo; otro contenido devuelve 94.
El origen reserva saldo y cupos en SQL antes del envío. No mantiene una transacción SQL abierta esperando a la red.
Confirmación con token y monto correctos: APROBADA. Rechazos definitivos 14/65/57/30: libera reserva. Timeout, corte, error 96 o confirmación no correlacionada: INCIERTA.
Un reinicio puede dejar RESERVADA; también requiere conciliación. No devolver dinero ni generar otro token solo por un timeout.
El MD no define transacción distribuida, reversos ni consulta de estado.

Extensión opcional IB_ESTADO_TRANSFERENCIA: idMensaje, bancoOrigen, bancoDestino y tokenTransferencia. Devuelve confirmación de entrada persistida o código 25 si no la encuentra.
Consultar / conciliar confirma una salida solo si el par devuelve mismo token y monto. No encontrarla, no soportar la extensión o no responder no autoriza devolución. Conciliar con el otro grupo.
No se reenvía automáticamente a un par cuya idempotencia no esté comprobada. Entregar el token del comprobante al usuario y conservarlo en ambos bancos.

## Códigos
00 confirmada; 14 cuenta/destino inexistente; 25 estado no encontrado (extensión); 30 formato/operación inválida; 51 saldo/usuario origen no disponible; 57 banco/IP no autorizado; 65 cupo insuficiente; 94 token en conflicto; 96 sin confirmación.
RESP_OK también envuelve rechazos: leer codigo.

## Pruebas
Base temporal y par TCP simulado: formato, alias de cuenta, banco no autorizado, tokens repetidos, contenido distinto, cupo concurrente, UDP, salida confirmada y respuesta perdida.
Falta probar físicamente con NEXUSBANK y los demás grupos; no se acredita compatibilidad externa hasta hacerlo.
