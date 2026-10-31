# Respaldo incluido para las pruebas

Fecha de creación: 2026-10-29 17:56:07, hora de Guatemala (UTC-06:00).
Base: BD_CajeroRed_Fase2.
Origen: SQL Server Express 2022, versión 16.0.1210.3.
Método: BACKUP DATABASE WITH COPY_ONLY, INIT, CHECKSUM sobre un archivo NUEVO de preparación.
Verificación: RESTORE VERIFYONLY WITH CHECKSUM terminó correctamente y SQL informó que el respaldo es válido.
SHA256: 6CB14855DC164361BBB7BBCB234BF71A804899A4B6F37EB3862F8A42CC4621C6

Conserva los usuarios, tarjetas, PIN como hashes, movimientos, inventarios y auditoría existentes al crear esta copia. No se modificaron saldos ni se reinicializó la base para preparar el paquete. La fecha diaria almacenada puede ser anterior a la fecha de su nueva prueba; revisar e inicializar cada cajero una vez por día.

Restaurar en SQL Server 2022 o posterior. Ajustar rutas de archivos e identidad Windows en la PC destino. Este paquete no demuestra todavía que la restauración en la PC de otro integrante se haya realizado: registrar esa prueba en el acta incluida.

Los PIN actuales no se pueden recuperar del hash. Si es necesario, el administrador asigna un PIN temporal desde Usuarios / Restablecer PIN.

Los resultados históricos de Documentacion/07_PruebasYEvidencias conservan sus fechas originales. Este respaldo más reciente no supone haber repetido todas las pruebas.