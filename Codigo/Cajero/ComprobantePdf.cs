using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CajeroRed.Cajero;

public static class ComprobantePdf
{
 public static void Guardar(string destino,IReadOnlyList<string> lineas)
 {
  string temporal=destino+"."+Guid.NewGuid().ToString("N")+".tmp";
  try
  {
   using var documento=new PdfDocument();
   documento.Info.Title="NETGEAR - Comprobante";
   var fuente=new XFont("Segoe UI",11,XFontStyleEx.Regular);
   var titulo=new XFont("Segoe UI",20,XFontStyleEx.Bold);
   XGraphics? g=null;double y=0,ancho=0,alto=0;
   void Pagina()
   {
    g?.Dispose();var pagina=documento.AddPage();pagina.Size=PdfSharp.PageSize.A4;
    ancho=pagina.Width.Point-88;alto=pagina.Height.Point;g=XGraphics.FromPdfPage(pagina);y=44;
    g.DrawString("NETGEAR",titulo,XBrushes.DarkBlue,new XRect(44,y,ancho,30),XStringFormats.TopLeft);y+=42;
   }
   void Linea(string texto)
   {
    if(y>alto-60)Pagina();
    g!.DrawString(texto,fuente,XBrushes.Black,new XRect(44,y,ancho,18),XStringFormats.TopLeft);y+=18;
   }
   Pagina();
   foreach(string texto in lineas)
   {
    foreach(string parrafo in texto.Replace("\r","").Split('\n'))
    {
     string linea="";
     foreach(char c in parrafo)
     {
      if(g!.MeasureString(linea+c,fuente).Width>ancho){Linea(linea);linea="";}
      linea+=c;
     }
     Linea(linea);
    }
   }
   g!.Dispose();documento.Save(temporal);File.Move(temporal,destino,true);
  }
  finally{if(File.Exists(temporal))File.Delete(temporal);}
 }
}
