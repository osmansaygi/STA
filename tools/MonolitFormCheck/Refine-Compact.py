from pathlib import Path
root = Path(__file__).resolve().parents[2]
edits = {
'DonatiBoyuForm': [('BeginCompact(360)','BeginCompact(340)'), ('ui.Tabs(146,','ui.Tabs(132,'), ('.Choices(_chkYuvarla);','.Choices(_chkYuvarla, _chkOnizleme);'), ('            ui.Section(null).Choices(_chkOnizleme);','')],
'DonatiMetrajForm': [('BeginCompact(350)','BeginCompact(330)'), ('.Field("Çap işareti", _txtFi).Field("En büyük boy · cm", _txtMax)','.Field("Ø / en büyük boy · cm", _txtFi, _txtMax)'), ('            ui.Section(null).Choices(_chkRenkli, _chkOnizleme);','            ui.Tip(_chkOnizleme, _chkOnizleme.Text); _chkOnizleme.Text = "Özet yaz";\n            ui.Section(null).Choices(_chkRenkli, _chkOnizleme);')],
'KotDegistirForm': [('BeginCompact(330)','BeginCompact(320)'), ('ui.Section("Kot değişimi").Field("Eski kot · cm", _txtEski).Field("Yeni kot · cm", _txtYeni);','ui.Section(null).Grid(new[] { "Eski kot · cm", "Yeni kot · cm" }, new Control[] { _txtEski, _txtYeni });')],
'HasirDonatiForm': [('BeginCompact(320)','BeginCompact(300)')],
'SehpaDonatiForm': [('BeginCompact(340)','BeginCompact(310)'), ('ui.Section("Kesit ve yerleşim")','ui.Section(null)')],
'OtoDonatiForm': [('BeginCompact(390)','BeginCompact(360)'), ('ui.Tabs(258,','ui.Tabs(224,')],
'MerdivenForm': [('BeginCompact(350)','BeginCompact(330)'), ('ui.Tabs(250,','ui.Tabs(208,')],
'TemelHatiliForm': [('BeginCompact(380)','BeginCompact(350)'), ('ui.Tabs(244,','ui.Tabs(202,')],
'TemelCekuruForm': [('BeginCompact(420)','BeginCompact(400)'), ('ui.Tabs(256,','ui.Tabs(212,')],
'TemelDetayForm': [('BeginCompact(430)','BeginCompact(420)'), ('ui.Tabs(278,','ui.Tabs(260,')],
'KuranglezForm': [('BeginCompact(390)','BeginCompact(360)'), ('ui.Tabs(276,','ui.Tabs(212,')],
'ParapetForm': [('BeginCompact(390)','BeginCompact(360)'), ('ui.Tabs(272,','ui.Tabs(220,')],
'PlanOlcuForm': [('BeginCompact(380)','BeginCompact(360)'), ('ui.Tabs(146,','ui.Tabs(132,')],
'KatmanAyarForm': [('.Control(tabs, 340)','.Control(tabs, 300)')],
'KolonDuseyForm': [('BeginCompact(330)','BeginCompact(300)'), ('ui.Section("Kat sınırı").Field','ui.Section(null).Field'), ('Seçilen kat ve altı çizilir; üst katlar alınmaz.','Seçilen kat ve altı çizilir.')],
'KapamaPerdeForm': [('BeginCompact(300)','BeginCompact(270)'), ('ui.Section("Temel hatılı").Options','ui.Section(null).Options')],
}
for name, changes in edits.items():
    path = next(p for module in ('ST4_Yardimci','ST4_Plan_ID_Ciz') for p in (root/module).glob(name+'.cs'))
    text = path.read_text(encoding='utf-8-sig')
    for before, after in changes:
        if name == 'DonatiMetrajForm' and 'ui.Tip(_chkOnizleme' in text and 'ui.Tip(_chkOnizleme' in after: continue
        text = text.replace(before, after)
    path.write_text(text, encoding='utf-8')
print('Measured compact profiles refined.')
