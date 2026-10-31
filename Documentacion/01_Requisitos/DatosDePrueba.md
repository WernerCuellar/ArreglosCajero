# Datos de demostración

Ocho usuarios ficticios, cada uno con Q5000 iniciales. Usuario 1 tiene límite Q500; los demás Q2000. Tarjetas 4000000000000001 a 4000000000000008, PIN inicial 1234. Administrador admin / GrupoCajero#2026. Son credenciales públicas de una entrega académica con datos ficticios.

Ocho cajeros registrados (ATM-01 a ATM-08). Se pueden agregar otros desde el dashboard sin modificar código. La cantidad debe coincidir como mínimo con los integrantes y nunca ser inferior a tres para la demostración.

Inventario sugerido para inicializar con Q10000: 20 de Q200, 20 de Q100, 20 de Q50, 50 de Q20, 50 de Q10, 100 de Q5 y 1000 de Q1. Total: Q10000. Cada cajero se inicializa una vez por día; la inicialización reemplaza el inventario de apertura y registra el efectivo anterior para trazabilidad. Antes del cambio diario, exportar el arqueo del cierre anterior.

El respaldo entregado conserva los datos ficticios preparados y el historial de acceso generado al verificar pantallas. Los resultados monetarios de las pruebas automáticas están en bases temporales que se eliminan; no contaminan esta base.

No confundir saldo inicial de una cuenta con depósito físico: el saldo al dar de alta un usuario no incrementa el inventario de ningún cajero. Los movimientos de efectivo deben pasar por retiro, depósito o carga administrativa.
