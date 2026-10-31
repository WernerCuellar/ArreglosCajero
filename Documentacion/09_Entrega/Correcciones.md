# Correcciones posteriores a la entrega inicial

## Límite de entrada del número de tarjeta

Los formularios Crear usuario y Cambiar tarjeta ahora permiten únicamente dígitos ASCII y un máximo de 16 caracteres desde la entrada. El campo bloquea letras, espacios y el dígito 17. Rechaza pegados demasiado largos completos, sin truncar silenciosamente el número. Permite reemplazar una selección si el resultado no supera los 16 dígitos.

Se mantiene la validación autoritativa de exactamente 16 dígitos en SQL Server al guardar. No se cambian tarjetas existentes, saldos, PIN ni historial.

Implementación: Codigo/PresentacionCompartida/Ui.cs, EntradaTarjeta y Formulario. Verificación reproducible: Pruebas --entrada-tarjeta; evidencia en Pruebas/Evidencias/entrada_tarjeta.txt. Cinco comprobaciones de escritura, pegado y reemplazo aprobadas.
