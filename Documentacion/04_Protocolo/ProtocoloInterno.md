# Protocolo interno v1

Transporte TCP persistente. Un objeto JSON por línea, UTF-8 sin BOM y terminador LF. Puerto por defecto 5000. Solicitud máxima 32768 bytes; respuesta máxima 65536 bytes. El receptor conserva fragmentos de mensajes entre lecturas y admite varias líneas en un mismo paquete TCP. Tamaños excesivos o JSON inválido cierran la conexión. Cada socket tiene un solo escritor serializado para evitar mezclar respuestas y comandos.

## Sobre común

```json
{"tipo":"RETIRO","idSolicitud":"8e1b52a91e2a4f9d8b1c76929f206abc","cajero":"ATM-01","sesion":"UUID-de-sesion","datos":{"monto":239,"desglose":{"200":1,"20":1,"10":1,"5":1,"1":4}}}
```

El grupo define los campos específicos dentro de `datos`. `idSolicitud` es texto único de hasta 64 caracteres y correlaciona la respuesta. `cajero` es el código dado de alta, hasta 24 caracteres. `sesion` se recibe al validar el token. Los campos de respuesta no se usan como autoridad en una solicitud.

```json
{"tipo":"RESPUESTA","idSolicitud":"8e1b52a91e2a4f9d8b1c76929f206abc","cajero":"ATM-01","codigo":"00","mensaje":"Operación aprobada","datos":{"idTransaccion":1,"saldo":4761.00,"disponibleHoy":261.00,"fecha":"2026-10-28T10:42:15","monto":239,"tipo":"RETIRO","cajero":"ATM-01"}}
```

El cliente espera como máximo 10 segundos. Si pierde una respuesta financiera, muestra la incertidumbre y conserva la referencia en memoria; no reenvía automáticamente el cargo. Para obtener su resultado envía ESTADO_SOLICITUD con la misma identidad de usuario. Un resultado no encontrado requiere revisión administrativa; no significa automáticamente que se pueda repetir.

## Mensajes

| Tipo | Dirección | datos y efecto |
|---|---|---|
| REGISTRAR_CAJERO | Cliente a servidor | equipo, version. Primer mensaje en 15 s. IP tomada del socket. Rechaza desconocidos, inactivos y duplicados. Devuelve habilitado/inicializado. |
| HEARTBEAT | Cliente a servidor | datos vacío. Cada 10 s; devuelve estado operativo. Tras 30 s sin señal, el monitor cierra la conexión y registra desconexión. |
| LOGIN | Cliente a servidor | tarjeta, pin (strings). Genera token en servidor; el cajero recibe requiereToken, nunca el código. |
| VALIDAR_TOKEN | Cliente a servidor | tarjeta, token (string de seis dígitos). Devuelve sesion, nombre, debeCambiarPin. |
| RETIRO | Cliente a servidor | monto entero positivo, desglose objeto denominación/cantidad. Validación autoritativa SQL. |
| DEPOSITO | Cliente a servidor | monto y desglose; rechaza superar capacidad del cajero. |
| CAMBIO_PIN | Cliente a servidor | pinActual, pinNuevo. PIN nuevo de cuatro dígitos distinto al anterior. |
| CONSULTA_SALDO | Cliente a servidor | vacío. Devuelve saldo, limiteDiario, retiradoHoy, disponibleHoy. |
| ULTIMAS_TRANSACCIONES | Cliente a servidor | vacío. datos es una lista de máximo cinco movimientos aprobados de cualquier cajero. |
| LOGOUT | Cliente a servidor | vacío. Cierra la sesión asociada al socket/cajero. |
| ESTADO_SOLICITUD | Cliente a servidor | idOriginal. Resultado confirmado de un movimiento del mismo usuario y cajero. |
| RESPUESTA | Servidor a cliente | codigo, mensaje, datos; conserva idSolicitud. |
| COMANDO | Servidor a cliente | comando y texto. FUERA_DE_SERVICIO, HABILITAR, MENSAJE o ACTUALIZAR_INVENTARIO. |

Un cajero no puede cambiar su identidad en un socket registrado. El cierre de sesión local ocurre a 60 s de inactividad; SQL rechaza sesiones con más de 120 s. Heartbeats no renuevan la sesión de usuario.

## Códigos

| Código | Significado |
|---|---|
| 00 | Operación aprobada |
| 25 | No existe resultado confirmado para la referencia consultada |
| 30 | Mensaje, monto o desglose inválido |
| 51 | Saldo insuficiente |
| 55 | PIN o credenciales incorrectas |
| 56 | Token inválido, vencido, usado o de otro cajero |
| 57 | Sesión vencida o inválida |
| 58 | Debe cambiar el PIN temporal |
| 61 | Límite diario global excedido |
| 75 | Tarjeta bloqueada o inactiva |
| 91 | Cajero fuera de servicio, inactivo o sin inicializar |
| 92 | Billetes insuficientes para el desglose; datos.alternativa si existe |
| 93 | Se supera la capacidad del cajero |
| 94 | Cajero duplicado o ID reutilizado con otro cuerpo |
| 96 | No se pudo confirmar; consultar antes de repetir |

En un 92 no se aplica retiro. Si el usuario acepta la alternativa, el cliente envía una solicitud NUEVA con otro identificador; SQL vuelve a validar existencias, saldo y límite. El algoritmo prueba primero denominaciones mayores y retrocede cuando el inventario acotado impide una combinación greedy; así encuentra alternativas que un algoritmo voraz simple perdería.

## Idempotencia

La clave es (cajero,idSolicitud), con usuario, tipo, huella del JSON y resultado persistido. El cliente mantiene serialización estable al repetir la misma solicitud. Reordenar propiedades del cuerpo cambia la huella: para consultar una operación conocida es preferible ESTADO_SOLICITUD. Nunca reutilizar el ID para aceptar una alternativa con otro desglose.

El estado de una solicitud puede revisarse también en Transacciones/Auditoría del dashboard por su referencia. Si la aplicación cliente se cerró y perdió su referencia en memoria, usar el historial y la auditoría antes de volver a operar.

## Listener compartido NETGEAR
Antes de REGISTRAR_CAJERO, los tipos IB_* se encaminan a BancoInterbancario y reciben una respuesta de una sola conexión con idMensaje. Los ATM conservan mensajes e idSolicitud originales.
Descubrimiento UDP 5005 y contrato IB: Documentacion/08_IntegracionEntreGrupos/ProtocoloExterno.md.
