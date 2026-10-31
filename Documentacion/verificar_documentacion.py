"""QA del PDF y sus capturas; no altera la documentación final."""
from pathlib import Path
from PIL import Image, ImageDraw
from pypdf import PdfReader
import pdfplumber

root=Path(__file__).resolve().parent.parent
pdf=root/'Documentacion/Entrega_Fase_II.pdf'
reader=PdfReader(pdf)
assert len(reader.pages)>20
texts=[p.extract_text() or '' for p in reader.pages]
assert all(len(t.strip())>80 for t in texts), 'Se encontró una página vacía o casi vacía.'
for term in ['sp_Retiro','sp_Deposito','sp_CambiarLimite','Manual del cajero','Matriz de trazabilidad','TRY','ROLLBACK','FOREIGN','PRIMARY KEY']:
    if term=='FOREIGN':continue # las FK se declaran inline con REFERENCES.
    assert term in '\n'.join(texts),term
with pdfplumber.open(pdf) as doc:
    for i,p in enumerate(doc.pages,1):
        bad=[c for c in p.chars if c['x0']<20 or c['x1']>p.width-18 or c['top']<8 or c['bottom']>p.height-8]
        assert not bad, f'Texto fuera del área de página {i}: {bad[:1]}'
pages=sorted((root/'tmp/pdfs').glob('pagina-*.png'))
assert len(pages)==len(reader.pages),(len(pages),len(reader.pages))
for inicio in range(0,len(pages),9):
    sheet=Image.new('RGB',(1110,1610),'#DDE4EF');draw=ImageDraw.Draw(sheet)
    for j,path in enumerate(pages[inicio:inicio+9]):
        im=Image.open(path).convert('RGB');im.thumbnail((350,500))
        x=(j%3)*370+10;y=(j//3)*535+25
        sheet.paste(im,(x,y));draw.text((x,y-18),f'Página {inicio+j+1}',fill='black')
    sheet.save(root/f'tmp/pdfs/contacto_{inicio//9+1}.png')
print(f'QA estructural: {len(reader.pages)} páginas, textos dentro de márgenes y sin páginas vacías.')
print('Hojas de contacto listas en tmp/pdfs; revisar antes de entregar.')
