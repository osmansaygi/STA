from pathlib import Path
p=Path('ST4_Yardimci/TemelCekuruRunner.cs');s=p.read_text(encoding='utf-8-sig')
a=s.index('                var walls = new List<Polyline>();',s.index('private void PlaceSectionEtiket'))
b=s.index('                string ustTok =',a);s=s[:a]+s[b:]
a=s.index('                var left = new List<Polyline>();',s.index('private void PlaceSectionEtiket'))
b=s.index('                DBText icL =',a);s=s[:a]+s[b:]
s=s.replace('                double pitCx = (MapPt(new Point3d(XInnerL, Y2, 0)).X + MapPt(new Point3d(XInnerR, Y2, 0)).X) * 0.5;\n','')
s=s.replace('                Database db = ms.Database;\n                DBText ustTx', '                DBText ustTx')
s=s.replace('                Polyline inL = left.Count >= 2 ? left[left.Count - 1] : null;\n                Polyline inR = right.Count >= 2 ? right[right.Count - 1] : null;\n','')
p.write_text(s,encoding='utf-8-sig')
p=Path('tools/CukurSmoke/Smoke.cs');s=p.read_text();s=s.replace('TemelCekuruSettings.HatilYuksekligiCm = 0;', 'TemelCekuruSettings.PaspayiCm = 5;\n            TemelCekuruSettings.TemelUstKotuCm = 0;\n            TemelCekuruSettings.HatilYuksekligiCm = 0;');p.write_text(s)
