# Ficha NETGEAR
| Campo | Valor |
|---|---|
| Nombre | NETGEAR |
| Código | BANC02 provisional, confirmar unicidad |
| TCP | 5000 por defecto, IPv4 Any, JSON UTF-8 por línea |
| UDP | 5005, DISCOVER_BANCO_ATM / HERE_BANCO_ATM |
| IP | La interfaz conectada a la LAN de los otros grupos; pendiente red definitiva |
| Cupo | Q25,000.00 diario por banco |
| Cuenta | NumeroCuenta de 1–20 dígitos o tarjeta activa de 16 dígitos |
| Operaciones | IB_PING, IB_VERIFICAR_CUENTA, IB_TRANSFERIR |
| Extensión | IB_ESTADO_TRANSFERENCIA (acordar soporte) |
| Restricción de acceso | Código e IP autorizados; sin firma/TLS definidos por el MD |
| Responsable/fecha del ensayo | Completar entre grupos |
Wi-Fi y Ethernet pueden tener IP distintas. La red uwu y las IP de BANC01 del MD son referencias del salón, no direcciones de este equipo.
