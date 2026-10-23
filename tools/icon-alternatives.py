import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

out=Path('docs/icon-alternatives');out.mkdir(exist_ok=True,parents=True)
# Original vector concepts on a common 24-unit grid, not copied library artwork.
items=[
('Oto Ölçü', ['3,7 3,17','21,7 21,17','3,12 21,12','6,9 3,12 6,15','18,9 21,12 18,15']),
('Plan Ölçü',['5,4 20,4 20,18 5,18 5,4','2,4 2,18','5,21 20,21','8,4 8,7','12,4 12,7','16,4 16,7']),
('Merdiven',['3,21 3,17 7,17 7,13 11,13 11,9 15,9 15,5 21,5','4,8 13,2','10,2 13,2 13,5']),
('Döşeme Donatı',['3,4 21,4 21,20 3,20 3,4','7,4 7,20','12,4 12,20','17,4 17,20','3,9 21,9','3,15 21,15']),
('Oto Donatı',['3,17 3,7 21,7 21,17 3,17','6,10 18,10','6,14 18,14','18,2 18,5','16,3.5 20,3.5']),
('Sehpa Donatısı',['2,19 6,19 8,6 16,6 18,19 22,19','8,10 16,10']),
('Hasır Donatı',['3,3 21,3 21,21 3,21 3,3','7,3 7,21','11,3 11,21','15,3 15,21','19,3 19,21','3,7 21,7','3,11 21,11','3,15 21,15','3,19 21,19']),
('Radye İlave Donatı',['2,19 22,19','3,15 21,15','5,5 5,12 19,12 19,5','9,8 9,15','15,8 15,15']),
('Temel Hatılı',['2,15 8,15 8,4 16,4 16,15 22,15 22,20 2,20 2,15','5,18 19,18']),
('Parapet Detayı',['3,21 3,4 12,4 12,14 21,14 21,20','7,18 7,8 9,8','12,17 21,17']),
('Temel Çukuru',['2,5 5,5 5,20 19,20 19,5 22,5','8,5 8,16 16,16 16,5','10,11 14,11','12,9 12,13']),
('Temel Detayı',['8,3 16,3 16,14 21,14 21,21 3,21 3,14 8,14 8,3','6,18 18,18','10,6 10,15','14,6 14,15']),
('Kuranglez',['2,3 6,3 6,19 20,19 20,9 23,9','10,4 10,14 17,14','7,7 10,7','7,11 10,11']),
('Perde Boşluk',['2,3 22,3 22,21 2,21 2,3','8,8 16,8 16,17 8,17 8,8','5,3 5,21','19,3 19,21','8,5 16,5']),
('Donatı Boyu',['2,8 5,8 5,5 19,5 19,8 22,8','3,16 21,16','3,13 3,19','21,13 21,19','6,14 3,16 6,18','18,14 21,16 18,18']),
('Donatı Metraj',['3,2 21,2 21,22 3,22 3,2','3,8 21,8','10,8 10,22','4,13 20,13','4,18 20,18']),
('Donatı Adet',['4,4 4,20','9,4 9,20','14,4 14,20','19,4 19,20','2,17 21,7']),
('Donatı Oku',['3,6 18,6','3,12 18,12','3,18 18,18','20,3 14,9','14,5 14,9 18,9','20,12 17,15']),
('Kot Değiştir',['3,11 21,11','5,5 19,5 12,11 5,5','12,14 12,22','9,18 12,22 15,18']),
('Katman Yükle',['2,7 12,2 22,7 12,12 2,7','2,12 12,17 22,12','2,17 12,22 22,17','19,1 19,5','17,3 21,3'])]
data=[{'label':n,'lines':[[[float(v) for v in p.split(',')] for p in l.split()] for l in ls]} for n,ls in items]
(out/'concepts.json').write_text(json.dumps(data,ensure_ascii=False),encoding='utf-8')
S=2; W=1260; H=1090
im=Image.new('RGB',(W*S,H*S),'#202832');d=ImageDraw.Draw(im)
def font(n,b=False):return ImageFont.truetype('C:/Windows/Fonts/'+('arialbd.ttf' if b else 'arial.ttf'),n*S)
def text(x,y,t,n=16,c='#e6edf3',b=False):d.text((x*S,y*S),t,font=font(n,b),fill=c)
text(35,24,'YARDIMCI / SİMGE ALTERNATİFLERİ',25,b=True)
text(35,64,'Aynı komutlar, üç görsel yaklaşım · 32 px önizleme · Henüz uygulanmadı',16,'#b6c2cc')
titles=['A  /  Sade çizgisel','B  /  İki tonlu','C  /  Teknik + vurgu']
subs=['En sakin ve sade görünüm','Küçük boyutta daha belirgin','Önerim: işi anlatan teknik şekiller']
for col in range(3):
 x=35+col*410
 text(x,111,titles[col],21,b=True);text(x,145,subs[col],14,'#b6c2cc')
 d.line((x*S,178*S,(x+380)*S,178*S),fill='#4a5663',width=S)
 for i,item in enumerate(data):
  y=192+i*42
  if col==1:d.rounded_rectangle((x*S,y*S,(x+34)*S,(y+34)*S),radius=5*S,fill='#31444a')
  lines=item['lines']
  # Simplified silhouettes in A; complete technical detail in C.
  if col==0 and len(lines)>4:lines=lines[:4]
  for j,line in enumerate(lines):
   color='#dde6ed' if col==0 else ('#75d9bd' if col==1 else ('#75d9bd' if j==len(lines)-1 else '#e6edf3'))
   pts=[((x+2+a*1.25)*S,(y+2+b*1.25)*S) for a,b in line]
   d.line(pts,fill=color,width=(2 if col==0 else 3)*S,joint='curve')
  text(x+46,y+7,item['label'],16)
text(35,1050,'Özgün taslaklar. A: çizgisel kütüphane yaklaşımı · B: güçlü siluet · C: teknik geometri + seçici vurgu',14,'#b6c2cc')
im.resize((W,H),Image.Resampling.LANCZOS).save(out/'alternatifler.png')

canvas='''import { useHostTheme, useState } from "cursor/canvas";
const items = DATA;
export default function IconAlternatives(){
 const t=useHostTheme();const [size,setSize]=useState(32);
 const titles=["A · Sade çizgisel","B · İki tonlu","C · Teknik + vurgu"];
 const notes=["Sade, sakin; ince detaylar azaltıldı.","Dolu zemin ve güçlü çizgi, küçük boyutta belirgin.","Önerim: komutun yaptığı işi anlatan özgün teknik şekiller."];
 return <main style={{padding:24,color:t.text.primary,background:t.bg.editor,fontFamily:"inherit"}}>
 <h1 style={{fontSize:24}}>Yardımcı simge alternatifleri</h1>
 <p style={{color:t.text.secondary}}>20 komut için özgün taslak karşılaştırması. Mevcut simgeler değiştirilmedi.</p>
 <div style={{display:"flex",gap:8,alignItems:"center",marginBottom:24}}>Simge boyutu: {[24,32,40].map(n=><button key={n} onClick={()=>setSize(n)} style={{padding:"6px 12px",color:size===n?t.text.onAccent:t.text.primary,background:size===n?t.accent.control:t.fill.secondary,border:0,borderRadius:4}}>{n} px</button>)}</div>
 <div style={{display:"grid",gridTemplateColumns:"repeat(3,minmax(240px,1fr))",gap:24,overflowX:"auto"}}>{titles.map((title,c)=><section key={title}><h2 style={{fontSize:18}}>{title}</h2><p style={{color:t.text.secondary,fontSize:12,minHeight:36}}>{notes[c]}</p><div style={{borderTop:`1px solid ${t.stroke.secondary}`,paddingTop:12}}>{items.map(item=><div key={item.label} style={{display:"flex",alignItems:"center",gap:12,minHeight:size+12}}><svg width={size} height={size} viewBox="0 0 24 24" style={{flexShrink:0}} aria-label={item.label}>{c===1&&<rect x="0" y="0" width="24" height="24" rx="4" fill={t.fill.secondary}/>}{(c===0&&item.lines.length>4?item.lines.slice(0,4):item.lines).map((line,j)=><polyline key={j} points={line.map(p=>p.join(",")).join(" ")} fill="none" stroke={c===1||(c===2&&j===item.lines.length-1)?t.accent.primary:t.text.primary} strokeWidth={c===0?1.6:2} strokeLinecap="round" strokeLinejoin="round"/>)}</svg><span style={{fontSize:13}}>{item.label}</span></div>)}</div></section>)}</div>
 <p style={{fontSize:12,color:t.text.secondary,marginTop:24}}>Kaynak yaklaşım: <a href="https://lucide.dev/" style={{color:t.text.link}}>Lucide</a> / <a href="https://github.com/tabler/tabler-icons" style={{color:t.text.link}}>Tabler Icons</a>. Buradaki şekiller özgün teknik taslaklardır; kütüphaneden alınmış hazır simgeler değildir.</p>
 </main>;
}
'''.replace('DATA',json.dumps(data,ensure_ascii=False))
(out/'yardimci-simge-alternatifleri.canvas.tsx').write_text(canvas,encoding='utf-8')
print(out/'alternatifler.png')
