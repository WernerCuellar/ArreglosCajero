using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace CajeroRed.Presentacion;

public static class Exportador
{
 // OpenXML real: strings inline, nunca fórmulas ejecutables provenientes de datos.
 public static void Excel(DataTable tabla,string destino)
 {
  using var zip=ZipFile.Open(destino,ZipArchiveMode.Create);
  void Archivo(string nombre,string contenido){using var w=new StreamWriter(zip.CreateEntry(nombre).Open(),new UTF8Encoding(false));w.Write(contenido);}
  Archivo("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
  Archivo("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
  Archivo("xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Reporte\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
  Archivo("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
  using var x=XmlWriter.Create(zip.CreateEntry("xl/worksheets/sheet1.xml").Open(),new XmlWriterSettings(){Encoding=new UTF8Encoding(false)});
  const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
  x.WriteStartElement("worksheet",ns);x.WriteStartElement("sheetData",ns);
  void Fila(IEnumerable<object> valores,bool encabezado=false)
  {
   x.WriteStartElement("row",ns);
   foreach(var v in valores)
   {
    x.WriteStartElement("c",ns);
    if(!encabezado&&v is decimal or int or long or double)
    {x.WriteElementString("v",ns,Convert.ToString(v,CultureInfo.InvariantCulture));}
    else {x.WriteAttributeString("t","inlineStr");x.WriteStartElement("is",ns);x.WriteElementString("t",ns,v is DateTime fecha?fecha.ToString("yyyy-MM-dd HH:mm:ss"):Convert.ToString(v));x.WriteEndElement();}
    x.WriteEndElement();
   }
   x.WriteEndElement();
  }
  Fila(tabla.Columns.Cast<DataColumn>().Select(c=>(object)c.ColumnName),true);
  foreach(DataRow r in tabla.Rows)Fila(r.ItemArray.Select(v=>v??""));
  x.WriteEndElement();x.WriteEndElement();
 }
}
