"""Genera el PDF de entrega y diagramas desde los documentos Markdown y SQL.

Solo herramienta de documentación: reportlab, Pillow y pypdf. Las aplicaciones
C# no necesitan Python. Ejecutar desde cualquier directorio.
"""
from pathlib import Path
import hashlib
import re
import textwrap
from xml.sax.saxutils import escape
from PIL import Image, ImageDraw, ImageFont
from reportlab.pdfgen import canvas
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph,
    Spacer, PageBreak, Table, TableStyle, Image as PdfImage, Preformatted, KeepTogether)
from reportlab.platypus.tableofcontents import TableOfContents
from reportlab.lib import colors
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

DOC=Path(__file__).resolve().parent
ROOT=DOC.parent
BLUE='#163A82'
LIGHT='#F4F7FC'
FONT=Path('C:/Windows/Fonts')
pdfmetrics.registerFont(TTFont('Segoe',str(FONT/'segoeui.ttf')))
pdfmetrics.registerFont(TTFont('SegoeBold',str(FONT/'segoeuib.ttf')))
pdfmetrics.registerFontFamily('Segoe',normal='Segoe',bold='SegoeBold',italic='Segoe',boldItalic='SegoeBold')

def font(size,bold=False):
    return ImageFont.truetype(str(FONT/('segoeuib.ttf' if bold else 'segoeui.ttf')),size)

def box(draw,rect,title,lines):
    x,y,x2,y2=rect
    draw.rounded_rectangle(rect,radius=14,fill='white',outline='#A8B6CC',width=2)
    draw.rounded_rectangle((x,y,x2,y+50),radius=12,fill=BLUE)
    draw.rectangle((x,y+30,x2,y+50),fill=BLUE)
    draw.text((x+15,y+10),title,font=font(23,True),fill='white')
    for n,line in enumerate(lines):
        draw.text((x+15,y+65+n*28),line,font=font(19),fill='#182B49')

def arrow(draw,points,label=None,color='#4878B8'):
    draw.line(points,fill=color,width=4)
    import math
    x,y=points[-1];px,py=points[-2];a=math.atan2(y-py,x-px)
    draw.polygon([(x,y),(x-15*math.cos(a-.5),y-15*math.sin(a-.5)),(x-15*math.cos(a+.5),y-15*math.sin(a+.5))],fill=color)
    if label:
        tx,ty=points[len(points)//2];draw.text((tx+5,ty-27),label,font=font(18),fill=color)

def diagramas():
    carpeta=DOC/'02_Arquitectura';carpeta.mkdir(exist_ok=True)
    im=Image.new('RGB',(1700,760),LIGHT);d=ImageDraw.Draw(im)
    d.text((45,25),'RED DE CAJEROS - ARQUITECTURA',font=font(34,True),fill=BLUE)
    for i in range(3):
        y=120+i*180;box(d,(50,y,400,y+140),f'ATM-0{i+1} / C# WPF',['Configuración IP / puerto','Sin conexión a SQL Server'])
        arrow(d,[(400,y+75),(590,y+75),(590,315),(680,315)])
    box(d,(680,215,1110,420),'SERVIDOR C#',['TcpListener / Tasks','Reglas y autorización','Dashboard administrativo','Único acceso a la base'])
    arrow(d,[(1110,315),(1260,315)])
    d.text((1140,265),'SQL',font=font(23,True),fill=BLUE)
    box(d,(1260,215,1650,420),'SQL SERVER',['Datos centralizados','Transacciones y bloqueos','Historial y auditoría','Usuario con permisos limitados'])
    box(d,(680,535,1110,680),'BANCO EXTERNO',['TCP JSON 5000 / UDP 5005','SQL propio / protocolo IB'])
    arrow(d,[(890,420),(890,535)],'Protocolo IB',color='#A65E00')
    d.text((470,125),'TCP persistente / JSON UTF-8',font=font(24,True),fill=BLUE)
    d.text((470,162),'Respuestas y comandos remotos',font=font(20),fill='#53657F')
    im.save(carpeta/'Arquitectura.png')

    carpeta=DOC/'03_BaseDeDatos';carpeta.mkdir(exist_ok=True)
    im=Image.new('RGB',(1800,1320),LIGHT);d=ImageDraw.Draw(im)
    d.text((40,25),'MODELO RELACIONAL - OPERACIÓN Y EFECTIVO',font=font(32,True),fill=BLUE)
    nodes={
      'Usuario':(50,110,['PK Id','Nombre / saldo / límite','Estado / intentos']),
      'Tarjeta':(490,110,['PK Id / UNIQUE Numero','FK UsuarioId','PIN hash + salt / activa']),
      'Transaccion':(930,110,['PK Id','FK UsuarioId / TarjetaId','FK Cajero / solicitud','Tipo / monto / resultado']),
      'TransaccionDetalle':(1370,110,['PK/FK TransaccionId','PK/FK Denominacion','Cantidad']),
      'Sesion':(50,490,['PK Id','FK UsuarioId / TarjetaId','FK Cajero','Inicio / actividad / fin']),
      'Token':(490,490,['PK Id','FK UsuarioId / TarjetaId','FK Cajero','Hash / expiración / usado']),
      'Cajero':(930,490,['PK Codigo','Nombre / ubicación / IP','Estado / heartbeat','Fecha de inicialización']),
      'InventarioCajero':(1370,490,['PK/FK Cajero','PK/FK Denominacion','Cantidad']),
      'CargaEfectivo':(930,870,['PK Id / FK Cajero','FK AdministradorId','Tipo / total / anterior']),
      'CargaEfectivoDetalle':(1370,870,['PK/FK CargaId','PK/FK Denominacion','Cantidad']),
      'Denominacion':(490,870,['PK Valor','200,100,50,20,10,5,1']),
      'SolicitudOperacion':(50,870,['PK/FK Cajero + Solicitud','FK UsuarioId','Huella / código / datos'])}
    for name,(x,y,lines) in nodes.items():box(d,(x,y,x+380,y+235),name,lines)
    arrow(d,[(430,200),(490,200)])
    arrow(d,[(870,200),(930,200)])
    arrow(d,[(1310,200),(1370,200)])
    arrow(d,[(1120,490),(1120,345)])
    arrow(d,[(1310,580),(1370,580)])
    arrow(d,[(1120,725),(1120,870)])
    arrow(d,[(1310,970),(1370,970)])
    arrow(d,[(240,345),(240,490)])
    arrow(d,[(680,345),(680,490)])
    arrow(d,[(870,970),(1340,970),(1340,760),(1560,760),(1560,725)])
    d.text((50,1180),'PK: llave primaria. FK: llave foránea. Flechas: relaciones principales de padre a detalle.',font=font(22),fill=BLUE)
    d.text((50,1220),'Las FK listadas en cada tabla y el script 01 completan las relaciones no dibujadas para mantener legibilidad.',font=font(20),fill='#53657F')
    im.save(carpeta/'ModeloRelacional.png')
    im=Image.new('RGB',(1800,860),LIGHT);d=ImageDraw.Draw(im)
    d.text((40,25),'MODELO RELACIONAL - ADMINISTRACIÓN Y TRAZABILIDAD',font=font(32,True),fill=BLUE)
    nodes=[('Administrador',50,115,['PK Id / UNIQUE Usuario','Hash PBKDF2 / salt / rol']),('HistorialLimite',490,115,['PK Id / FK UsuarioId','FK AdministradorId','Anterior / nuevo / fecha']),('CambioPin',930,115,['PK Id / FK UsuarioId','FK Cajero (nullable)','Origen / resultado / fecha']),('Alerta',1370,115,['PK Id / FK Cajero','FK AdministradorId','Tipo / atendida / fecha']),('Usuario',490,490,['PK Id','Entidad operativa central']),('Bitacora',50,490,['PK Id','Origen / acción / detalle','Fecha SQL']),('Parametro',930,490,['PK Clave','Valor configurable']),('Cajero',1370,490,['PK Codigo','Entidad de la red'])]
    for name,x,y,lines in nodes:box(d,(x,y,x+380,y+230),name,lines)
    arrow(d,[(430,210),(490,210)])
    arrow(d,[(680,490),(680,345)])
    arrow(d,[(870,580),(900,580),(900,250),(930,250)])
    arrow(d,[(1560,490),(1560,345)])
    d.text((50,775),'Administrador también autoriza CargaEfectivo. Las bajas son lógicas; no se destruye el historial.',font=font(22),fill=BLUE)
    im.save(carpeta/'ModeloAuditoria.png')

styles=getSampleStyleSheet()
styles.add(ParagraphStyle(name='BodyGT',fontName='Segoe',fontSize=9.2,leading=14,spaceAfter=8,textColor=colors.HexColor('#182B49')))
styles.add(ParagraphStyle(name='H1GT',fontName='SegoeBold',fontSize=20,leading=26,spaceAfter=16,textColor=colors.HexColor(BLUE)))
styles.add(ParagraphStyle(name='H2GT',fontName='SegoeBold',fontSize=13,leading=19,spaceBefore=12,spaceAfter=8,textColor=colors.HexColor(BLUE)))
styles.add(ParagraphStyle(name='CellGT',fontName='Segoe',fontSize=7.2,leading=10,spaceAfter=0))
styles.add(ParagraphStyle(name='CodeGT',fontName='Courier',fontSize=6.4,leading=8,spaceAfter=8))

class Documento(BaseDocTemplate):
    def afterFlowable(self,f):
        if isinstance(f,Paragraph) and f.style.name=='H1GT':
            text=f.getPlainText();key=hashlib.sha256(text.encode()).hexdigest()[:20]
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(text,key,0,False)
            self.notify('TOCEntry',(0,text,self.page,key))

def cabecera(c,doc):
    c.saveState();c.setStrokeColor(colors.HexColor('#D4DDEA'));c.line(40,805,555,805)
    c.setFont('Segoe',8);c.setFillColor(colors.HexColor(BLUE));c.drawString(40,814,'NETGEAR | FASE II - CLIENTE / SERVIDOR')
    c.setFillColor(colors.HexColor('#53657F'));c.drawString(40,26,'Documentación técnica y manuales | 31 de octubre de 2026')
    c.drawRightString(555,26,str(doc.page));c.restoreState()

def inline(s):
    s=escape(s.replace('\u2011','-').replace('\u2013','-').replace('\u2014','-'))
    s=re.sub(r'`([^`]+)`',r'<font name="Courier">\1</font>',s)
    s=re.sub(r'\*\*([^*]+)\*\*',r'<b>\1</b>',s)
    return s

def image(path,width=510):
    with Image.open(path) as im:w,h=im.size
    return PdfImage(str(path),width=width,height=width*h/w)

def tabla(lines):
    rows=[]
    for line in lines:
        cells=[c.strip() for c in line.strip().strip('|').split('|')]
        if all(re.match(r'^:?-+:?$',c) for c in cells):continue
        rows.append([Paragraph(inline(c),styles['CellGT']) for c in cells])
    n=max(map(len,rows)); widths=[515/n]*n
    if n==2:widths=[150,365]
    if n==4:widths=[42,130,210,133]
    t=Table(rows,colWidths=widths,repeatRows=1,hAlign='LEFT')
    t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),colors.HexColor('#E4EDFC')),('VALIGN',(0,0),(-1,-1),'TOP'),('GRID',(0,0),(-1,-1),.4,colors.HexColor('#D4DDEA')),('LEFTPADDING',(0,0),(-1,-1),7),('RIGHTPADDING',(0,0),(-1,-1),7),('TOPPADDING',(0,0),(-1,-1),6),('BOTTOMPADDING',(0,0),(-1,-1),6),('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white,colors.HexColor(LIGHT)])]))
    return t

def markdown(path):
    output=[];lines=path.read_text(encoding='utf-8-sig').splitlines();i=0
    while i<len(lines):
        s=lines[i].strip()
        if not s:i+=1;continue
        if s.startswith('```'):
            language=s[3:].strip();code=[];i+=1
            while i<len(lines) and not lines[i].strip().startswith('```'):code.append(lines[i]);i+=1
            if language!='mermaid':
                wrapped='\n'.join(part for line in code for part in (textwrap.wrap(line,110,replace_whitespace=False,drop_whitespace=False) or ['']))
                output.append(Preformatted(escape(wrapped),styles['CodeGT']))
        elif s.startswith('|'):
            block=[]
            while i<len(lines) and lines[i].strip().startswith('|'):block.append(lines[i]);i+=1
            output.append(tabla(block));output.append(Spacer(1,10));continue
        elif s.startswith('# '):output.append(Paragraph(inline(s[2:]),styles['H1GT']))
        elif s.startswith('## '):output.append(Paragraph(inline(s[3:]),styles['H2GT']))
        else:
            paragraph=[s];i+=1
            while i<len(lines) and lines[i].strip() and not lines[i].lstrip().startswith(('#','|','```')):
                paragraph.append(lines[i].strip());i+=1
            # Mantener listas como párrafos individuales facilita lectura sin una
            # falsa lista anidada en el documento consolidado.
            output.append(Paragraph(inline(' '.join(paragraph)),styles['BodyGT']));continue
        i+=1
    return output

def generar():
    diagramas();story=[]
    story += [Spacer(1,70),Paragraph('SISTEMA BANCARIO',styles['H1GT']),Paragraph('Red de cajeros automáticos<br/>Fase II - Cliente / Servidor',ParagraphStyle('Portada',fontName='SegoeBold',fontSize=29,leading=38,textColor=colors.HexColor(BLUE))),Spacer(1,25),Paragraph('C# / WPF / SQL Server / TCP - JSON',styles['H2GT']),Paragraph('Documento técnico, manuales, protocolo, trazabilidad y scripts SQL completos.',styles['BodyGT']),Spacer(1,25),Paragraph('Visual Studio 2022 · Entrega preparada el 31 de octubre de 2026',styles['BodyGT']),Paragraph('Integrantes, carnés y enlace al video: completar por el grupo antes de presentar.',styles['BodyGT']),Spacer(1,30),Paragraph('Estado de validación',styles['H2GT']),Paragraph('Implementación y pruebas locales documentadas. La demostración con tres equipos físicos, el firewall de la red real y el video requieren la participación del grupo. La integración NETGEAR implementa el MD recibido; falta el ensayo físico con otro grupo.',styles['BodyGT']),PageBreak()]
    story.append(Paragraph('Contenido',styles['H1GT']));toc=TableOfContents();toc.levelStyles=[ParagraphStyle('TocGT',fontName='Segoe',fontSize=9,leading=14,spaceBefore=5,leftIndent=0)];story.append(toc);story.append(PageBreak())
    files=[DOC/'09_Entrega/NETGEAR_2.2.0.md',DOC/'09_Entrega/RevisionServidor_2026-10-31.md',DOC/'09_Entrega/CorreccionesCajero_2026-10-31.md',DOC/'00_Indice/Indice.md',DOC/'01_Requisitos/Alcance.md',DOC/'01_Requisitos/DatosDePrueba.md',DOC/'01_Requisitos/MatrizDeTrazabilidad.md',DOC/'02_Arquitectura/Arquitectura.md',DOC/'03_BaseDeDatos/Modelo.md',DOC/'04_Protocolo/ProtocoloInterno.md',DOC/'05_InstalacionYRed/ManualDeInstalacion.md',DOC/'06_Manuales/ManualDelServidor.md',DOC/'06_Manuales/ManualDelCajero.md',DOC/'07_PruebasYEvidencias/Resultados.md',DOC/'07_PruebasYEvidencias/EscenariosFisicos.md',DOC/'08_IntegracionEntreGrupos/LEEME_Integracion.md',DOC/'08_IntegracionEntreGrupos/FichaDeConexion.md',DOC/'08_IntegracionEntreGrupos/ProtocoloExterno.md',DOC/'08_IntegracionEntreGrupos/PruebasDeConexion.md',DOC/'09_Entrega/GuiaDeContinuacion.md']
    for f in files:
        story.extend(markdown(f))
        if f.name=='Arquitectura.md':story.append(image(DOC/'02_Arquitectura/Arquitectura.png'))
        if f.name=='Modelo.md':
            story += [PageBreak(),Paragraph('Diagrama relacional - operación',styles['H1GT']),image(DOC/'03_BaseDeDatos/ModeloRelacional.png'),PageBreak(),Paragraph('Diagrama relacional - administración',styles['H1GT']),image(DOC/'03_BaseDeDatos/ModeloAuditoria.png')]
        if f.name in ['ManualDelServidor.md','ManualDelCajero.md']:
            names=['servidor_resumen.png','servidor_cajeros.png','servidor_usuarios.png'] if f.name=='ManualDelServidor.md' else ['cajero_acceso.png','cajero_token.png','cajero_menu.png']
            for n in names:
                p=DOC/'06_Manuales/Capturas'/n
                if p.exists():story += [PageBreak(),Paragraph('Captura: '+n.replace('_',' ').replace('.png',''),styles['H1GT']),image(p),Spacer(1,12),Paragraph('Captura histórica del paquete original en esta PC; no muestra todas las ampliaciones NETGEAR ni sustituye la prueba física.',styles['BodyGT'])]
        story.append(PageBreak())
    story.append(Paragraph('Anexo - scripts SQL completos',styles['H1GT']));story.append(Paragraph('Aplicar en orden mediante Instalar-BaseDeDatos.ps1. Los scripts no eliminan bases existentes; la instalación se detiene si el esquema ya está creado. Los procedimientos pueden actualizarse por separado después de revisar su versión.',styles['BodyGT']))
    for f in sorted((ROOT/'BaseDeDatos/Scripts').glob('*.sql')):
        story.append(Paragraph(f.name,styles['H2GT']))
        for line in f.read_text(encoding='utf-8-sig').splitlines():
            line=line.replace('\u2011','-').replace('\u2013','-').replace('\u2014','-')
            wrapped='\n'.join(textwrap.wrap(line,110,replace_whitespace=False,drop_whitespace=False) or [' '])
            story.append(Preformatted(escape(wrapped),styles['CodeGT']))
        story.append(PageBreak())
    out=DOC/'Entrega_Fase_II.pdf'
    doc=Documento(str(out),pagesize=A4,leftMargin=40,rightMargin=40,topMargin=55,bottomMargin=45,title='Sistema bancario - Fase II',author='Equipo del proyecto - completar integrantes')
    doc.addPageTemplates(PageTemplate(id='Normal',frames=[Frame(40,45,515,742,leftPadding=0,rightPadding=0,topPadding=0,bottomPadding=0)],onPage=cabecera))
    doc.multiBuild(story)
    from pypdf import PdfReader
    print(f'{out}: {len(PdfReader(out).pages)} páginas')

if __name__=='__main__':generar()
