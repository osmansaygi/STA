from pathlib import Path

def replace_method(s, signature, body):
    start=s.index(signature); brace=s.index('{',start); depth=1; end=brace+1
    while depth:
        if s[end]=='{': depth+=1
        elif s[end]=='}': depth-=1
        end+=1
    return s[:start]+signature+'\n        {\n'+body+'\n        }'+s[end:]

p=Path('ST4_Plan_ID_Ciz/KolonDuseyForm.cs'); s=p.read_text(encoding='utf-8-sig')
start=s.index('        static string IniPath()'); end=s.index('        public static void EnsureLoaded()',start); s=s[:start]+s[end:]
s=replace_method(s,'public static void EnsureLoaded()', '''            if (_loaded) return;
            _loaded = true;
            var saved = PlanFormSettings.Load("kolondusey");
            Olcek25 = saved.Bool("Olcek25", Olcek25);
            KatModu = saved.Int("KatModu", KatModu, 0, 2);
            KatSinirla = saved.Bool("KatSinirla", KatSinirla);
            SonKatAdi = saved.Text("SonKatAdi", SonKatAdi);''')
s=replace_method(s,'public static void Save()', '''            EnsureLoaded();
            PlanFormSettings.Load("kolondusey").Set("Olcek25", Olcek25).Set("KatModu", KatModu)
                .Set("KatSinirla", KatSinirla).Set("SonKatAdi", SonKatAdi).Save();''')
s=s.replace('                KolonDuseySettings.SonKatAdi = f.SelectedSonKatAdi;\n                KolonDuseySettings.Save();\n','')
s=s.replace('            _items = items ?? new List<(int fi, string name, double elev)>();','            KolonDuseySettings.EnsureLoaded();\n            _items = items ?? new List<(int fi, string name, double elev)>();')
s=s.replace('            btnOk.Click += (s, e) =>\n            {\n                DialogResult', '            btnOk.Click += (s, e) =>\n            {\n                if (_cmbSon.SelectedIndex < 0) return;\n                KolonDuseySettings.SonKatAdi = SelectedSonKatAdi;\n                KolonDuseySettings.Save();\n                DialogResult')
p.write_text(s,encoding='utf-8')

p=Path('ST4_Plan_ID_Ciz/KapamaPerdeForm.cs'); s=p.read_text(encoding='utf-8-sig')
start=s.index('        static string IniPath()'); end=s.index('        public static void EnsureLoaded()',start); s=s[:start]+s[end:]
s=replace_method(s,'public static void EnsureLoaded()', '''            if (_loaded) return;
            _loaded = true;
            TemelHatili = PlanFormSettings.Load("kapamaperde").Bool("TemelHatili", TemelHatili);''')
s=replace_method(s,'public static void Save()', '''            EnsureLoaded();
            PlanFormSettings.Load("kapamaperde").Set("TemelHatili", TemelHatili).Save();''')
p.write_text(s,encoding='utf-8')
