using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using CajeroRed.Protocolo;

namespace CajeroRed.Infraestructura;

public sealed class BaseDatos(string cadena, bool permisosLimitados = true)
{
    public string Cadena { get; } = new SqlConnectionStringBuilder(cadena) { Pooling = !permisosLimitados }.ConnectionString;
    private async Task<SqlConnection> Abrir()
    {
        var con=new SqlConnection(Cadena);
        try
        {
            await con.OpenAsync();
            if(permisosLimitados)
            {
                await using var contexto=new SqlCommand("EXECUTE AS USER = 'CajeroServidor' WITH NO REVERT;",con);
                await contexto.ExecuteNonQueryAsync();
            }
            return con;
        }
        catch {await con.DisposeAsync();throw;}
    }
    public async Task<DataTable> Consultar(string sql, params (string, object?)[] parametros)
    {
        await using var conexion = await Abrir();
        await using var cmd = new SqlCommand(sql, conexion) { CommandTimeout = 8 };
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var tabla = new DataTable(); tabla.Load(reader); return tabla;
    }
    public async Task Ejecutar(string sql, params (string, object?)[] parametros) => await Consultar(sql, parametros);
    public async Task<Mensaje> Atender(Mensaje m)
    {
        var nombres = new Dictionary<string,string>
        {
            ["REGISTRAR_CAJERO"]="sp_RegistrarCajero", ["HEARTBEAT"]="sp_Heartbeat",
            ["LOGIN"]="sp_ValidarCredenciales", ["VALIDAR_TOKEN"]="sp_ValidarToken",
            ["RETIRO"]="sp_Retiro", ["DEPOSITO"]="sp_Deposito", ["CAMBIO_PIN"]="sp_CambiarPin",
            ["CONSULTA_SALDO"]="sp_ConsultaUsuario", ["ULTIMAS_TRANSACCIONES"]="sp_UltimasTransacciones",
            ["LOGOUT"]="sp_CerrarSesion", ["ESTADO_SOLICITUD"]="sp_EstadoSolicitud"
        };
        if (!nombres.TryGetValue(m.Tipo, out var proc)) return Mensaje.Respuesta(m,"30","Mensaje no reconocido.");
        await using var con = await Abrir();
        await using var cmd = new SqlCommand(proc, con) { CommandType = CommandType.StoredProcedure, CommandTimeout = 8 };
        cmd.Parameters.AddWithValue("@Json", m.Datos.GetRawText());
        cmd.Parameters.AddWithValue("@Cajero", m.Cajero);
        cmd.Parameters.AddWithValue("@Sesion", (object?)m.Sesion ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Solicitud", m.IdSolicitud);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) throw new InvalidOperationException("Sin resultado de SQL.");
        var datos = rd.IsDBNull(2) ? "{}" : rd.GetString(2);
        return Mensaje.Respuesta(m,rd.GetString(0),rd.GetString(1),JsonSerializer.Deserialize<JsonElement>(datos));
    }
    public async Task Auditoria(string origen, string accion, string detalle) => await Ejecutar(
        "INSERT dbo.Bitacora(Origen,Accion,Detalle) VALUES(@o,@a,@d)", ("@o",origen),("@a",accion),("@d",detalle));
}
