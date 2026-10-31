using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CajeroRed.Infraestructura;
using CajeroRed.Protocolo;

namespace CajeroRed.Aplicacion;

public sealed class Administracion(BaseDatos bd)
{
    public int? Id {get;private set;}
    public string Usuario {get;private set;}="";
    public string Rol {get;private set;}="";
    public async Task<bool> Login(string usuario,string password)
    {
        var tabla=await bd.Consultar("SELECT Id,Hash,Salt,Iteraciones,Rol FROM dbo.Administrador WHERE Usuario=@u AND Activo=1",("@u",usuario));
        if(tabla.Rows.Count!=1){await bd.Auditoria("SERVIDOR","LOGIN_ADMIN_RECHAZADO","Usuario no válido.");return false;}
        var fila=tabla.Rows[0];var hash=Rfc2898DeriveBytes.Pbkdf2(password,(byte[])fila["Salt"],(int)fila["Iteraciones"],HashAlgorithmName.SHA256,32);
        if(!CryptographicOperations.FixedTimeEquals(hash,(byte[])fila["Hash"]))
        {await bd.Auditoria("SERVIDOR","LOGIN_ADMIN_RECHAZADO","Credenciales incorrectas.");return false;}
        Id=(int)fila["Id"];Usuario=usuario;Rol=(string)fila["Rol"];
        await bd.Auditoria("ADMIN-"+Id,"ACCESO","Ingreso al dashboard.");return true;
    }
    public async Task Accion(string tipo,object datos)
    {
        if(Id==null||Rol!="Administrador")throw new InvalidOperationException("Acción no autorizada.");
        var proc=tipo switch {"INICIALIZAR"=>"sp_InicializarCajero","CARGAR"=>"sp_AgregarEfectivo","CAMBIAR_TARJETA"=>"sp_CambiarTarjeta","CAMBIAR_LIMITE"=>"sp_CambiarLimite",_=>"sp_AdminOperacion"};
        var json=JsonSerializer.Serialize(datos,Mensaje.Json);
        try
        {
            if(proc=="sp_AdminOperacion")await bd.Ejecutar($"EXEC dbo.{proc} @a,@t,@j",("@a",Id),("@t",tipo),("@j",json));
            else await bd.Ejecutar($"EXEC dbo.{proc} @a,@j",("@a",Id),("@j",json));
        }
        catch(Microsoft.Data.SqlClient.SqlException e)
        {await bd.Auditoria("ADMIN-"+Id,tipo+"_RECHAZADA","Operación no aplicada. Código SQL "+e.Number);throw;}
    }
    public Task<DataTable> Consultar(string sql,params (string,object?)[] parametros)
    {
        if(Id==null)throw new InvalidOperationException("Debe iniciar sesión.");
        return bd.Consultar(sql,parametros);
    }
    public Task RegistrarExportacion(string reporte)
    {if(Id==null)throw new InvalidOperationException("Debe iniciar sesión.");return bd.Auditoria("ADMIN-"+Id,"EXPORTACION",reporte);}
}
