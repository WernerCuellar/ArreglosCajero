# Manual del cajero

## Conectar

Ejecutar Cajero.exe. Sin servidor accesible muestra Fuera de servicio y reintenta cada cinco segundos. Abrir Configuración de conexión para indicar IP/nombre, puerto y código único. Probar conexión verifica que el puerto TCP sea accesible; el registro debe aceptarse además en el servidor. Un cajero sin inicializar o deshabilitado no permite acceder.

Al usar argumentos de arranque, estos prevalecen sobre el archivo de configuración. Para que una modificación de la interfaz persista entre arranques, ejecutar el EXE sin argumentos o actualizar también los argumentos del acceso directo.

## Ingresar

Tarjeta de dieciséis dígitos y PIN de cuatro, enmascarado. Continuar genera el token y abre Validar token. Obtener los seis dígitos de la ventana Token del usuario en el servidor e ingresarlos antes de que venza. Tres errores consecutivos de PIN/token bloquean la tarjeta en todos los cajeros. Solo administración puede desbloquearla. Si el PIN es temporal, primero debe cambiarse.

Capturas: `Capturas/cajero_acceso.png`, `Capturas/cajero_token.png`, `Capturas/cajero_menu.png`.

## Retirar

Ingresar monto en quetzales enteros, por ejemplo 239. Indicar cantidad de billetes de Q200, Q100, Q50, Q20, Q10, Q5 y Q1. La suma debe coincidir con el monto. Confirmar operación. El servidor valida saldo, límite del usuario en todos los cajeros y existencias de este cajero. Si no puede entregar el desglose, propone una alternativa cuando exista. Se puede aceptar o cancelar; aceptar inicia una validación nueva con otro ID.

Un retiro aprobado muestra comprobante con referencia, transacción, monto, saldo, cajero, tarjeta enmascarada y fecha oficial. Continuar vuelve al menú.

## Depositar

Ingresar monto y desglose. Confirmar. Aumenta el saldo del usuario y el inventario del cajero de forma conjunta. Si supera Q30000 o el máximo menor configurado, se rechaza sin cambiar datos. El depósito se registra antes de responder.

## Otras operaciones

Consultar saldo muestra saldo actual, límite diario, retirado hoy, disponible del límite y máximo según saldo y límite. La cantidad efectivamente retirable también depende del saldo y de los billetes del cajero. Últimas cinco transacciones muestra movimientos aprobados de todos los cajeros, con fecha/hora, tipo y monto. Cambiar PIN requiere actual, nuevo de cuatro dígitos y confirmación; debe ser distinto al anterior. El cambio se aplica a toda la red.

Cerrar sesión termina el acceso. Sesenta segundos sin clics o teclado cierran automáticamente la sesión local; SQL rechaza sesiones que superen dos minutos sin operación. Los heartbeats no mantienen abierta la sesión del usuario.

## Corte durante una operación

Si pasan diez segundos sin respuesta o se pierde la conexión, no asumir que el movimiento fue rechazado. La referencia puede existir como operación confirmada en SQL. Volver a ingresar con el mismo usuario y pulsar Consultar operación pendiente. Si aparece un resultado confirmado, la interfaz permite volver a operar. Si no hay resultado, pedir revisión al administrador antes de repetir. La app conserva la referencia en LocalAppData y la recupera al ingresar con el usuario correcto; si la IP cambió, revisar con administración. No borre un pendiente por un timeout.

El cliente no continúa operaciones fuera de línea y nunca modifica saldo localmente. No guarda una copia de cuentas ni una cadena de conexión SQL.

## Actualización NETGEAR 2.1.0
El CMD acepta código, servidor y puerto opcional; sin argumentos usa la configuración guardada.
Los formularios validan antes de cerrar y conservan datos al corregir un error. PIN/token solo admiten dígitos; para reemplazarlos por pegado, vaciar primero el campo.
El cierre local es inmediato aunque el servidor no responda. Los formularios cuentan actividad y se cierran al vencer la sesión, perder la conexión o deshabilitar el cajero.
El comprobante confirmado permite Guardar comprobante en PDF. No incluye PIN/token y usa la fecha del servidor. Un fallo de guardado no revierte la operación.
Los retiros y depósitos quedan bloqueados hasta resolver un pendiente con su usuario; consultas y cierre siguen disponibles.
Ver Documentacion/09_Entrega/CorreccionesCajero_2026-10-31.md para detalle de los doce puntos y compatibilidad del servidor.
