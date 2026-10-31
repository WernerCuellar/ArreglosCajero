# Correcciones del cajero — NETGEAR 2.1.0

Referencia: cambios.txt entregado el 31/10/2026. Fuente en Codigo/Cajero y Codigo/Aplicacion/ClienteTcp*.cs de la solución del servidor. El paquete CajeroRed_Cajero contiene el portable, no otro fuente diferente.

1. Retiros y depósitos se bloquean en pantalla y en el transporte cuando hay movimiento en curso o pendiente.
2. Cerrar sesión limpia el acceso local inmediatamente; el LOGOUT se notifica sin esperar respuesta.
3. PIN actual/nuevo/confirmación: hasta cuatro dígitos, rechazo de pegados inválidos, cuatro completos, confirmación igual y nuevo distinto del anterior.
4. Saldo muestra saldo, límite, retirado hoy, disponible del límite y mínimo entre saldo/disponible. Los billetes siguen validándose en el servidor.
5. La referencia se escribe atómicamente ANTES del envío y se recupera al autenticar al usuario. Se asocia a dirección/puerto, cajero y usuarioId del servidor. Nunca guarda PIN, token, sesión o tarjeta completa.
6. Tarjeta/PIN/token restringidos a dígitos y longitud; PIN/token enmascarados. Un pegado largo se rechaza completo.
7. Un nuevo acceso limpia el aviso de inactividad.
8. Configuración validada al cargar; JSON roto o puerto inválido permiten corregirla. Los errores de escritura se muestran sin cerrar el cajero; la instalación normal permite respaldo en LocalAppData.
9. Monto y desglose se validan juntos antes de cerrar; un error conserva los valores. Lo mismo para la confirmación de PIN.
10. Guardar comprobante en PDF mediante PDFsharp-WPF 6.2.4 (MIT). Captura solo el resultado confirmado, fecha SQL, referencia, cajero y tarjeta enmascarada; cambio de PIN solo confirma el cambio. Error de guardado no cambia el movimiento.
11. Actividad dentro de formularios cuenta para los 60 segundos. Al expirar se cierran los formularios.
12. Desconexión/deshabilitación cierra formularios e invalida la sesión; una respuesta tardía no vuelve a mostrar información de una sesión terminada.

## Servidor compatible
09_CompatibilidadCajero.sql actualiza solo sp_Operacion. VALIDAR_TOKEN añade usuarioId; ESTADO_SOLICITUD devuelve idOriginal/usuarioId y mantiene la autorización SQL por usuario/cajero; saldo y cambio PIN añaden fecha SQL.
Ejecutar Actualizar-Interbancario.ps1 aplica 08 y 09 sin reinstalar ni reinicializar la base. Conservar también las validaciones del servidor.

## Pendientes y recuperación
Registros en LocalAppData/NETGEAR/Cajero/Pendientes, uno por servidor/puerto/cajero/usuario. El archivo conserva referencia, tipo, monto y fecha técnica local, sin credenciales.
Al volver a ingresar con el usuario correspondiente, usar Consultar operación pendiente. Solo se elimina con una respuesta de estado correlacionada del servidor. Código 25 o 96 no significa rechazo: mantener el bloqueo y revisar con administración.
Un registro dañado o ilegible bloquea dinero y permite consultas; no se descarta silenciosamente.
La dirección de servidor forma parte de la asociación: mantener la misma configuración o un nombre estable al recuperar; si cambió la IP, coordinar la recuperación con administración.
Para reemplazar un PIN/token por pegado, vaciar primero el campo; así no se trunca silenciosamente una entrada demasiado larga.

## CMD y portable
Iniciar-Cajero.cmd sin argumentos respeta la configuración guardada.
Argumentos: código, dirección, puerto opcional. Ejemplo:
Iniciar-Cajero.cmd ATM-01 192.168.0.34 5000
Los argumentos prevalecen sobre el JSON.
Publicar-Cajero.ps1 actualiza el paquete hermano CajeroRed_Cajero, conserva configuración privada y regenera su manifiesto SHA256. El teclado táctil sigue pendiente como deseable.

## Validación
Pruebas/RevisionCajero.cs y --revision-cajero verifican respuesta perdida, bloqueo, recuperación/identidad, entrada numérica, formularios, actividad, deshabilitación, corte, cierre inmediato, respuesta tardía, configuración y cuatro tipos de PDF. Los ensayos son locales con un servidor simulado; la integración SQL/TCP real se verifica aparte en una base temporal.

Resultado final: 28 comprobaciones específicas del cajero, 83 SQL/TCP/UDP y 5 de interfaz del servidor, todas aprobadas. Compilación sin errores ni advertencias. PDF de ejemplo en Pruebas/Evidencias/Comprobantes.
Dependencia PDF fijada: https://www.nuget.org/packages/PDFsharp-WPF/6.2.4 (MIT, Windows/WPF).
