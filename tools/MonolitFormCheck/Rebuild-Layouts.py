"""One-time explicit UI migration. Existing controls and event handlers are retained."""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
PROFILES = {
"OtoOlcuForm": '''
var ui = BeginCompact(360);
ui.Section("Hazır şablon").Template(_cmbPreset, _btnPresetAdd, _btnPresetRemove);
ui.Section(null).Field(_lblStyle, _cmbDimStyle);
ui.Section(null).Field(_lblOlcuTipi, _cmbOlcuTipi).When(() => !OtoOlcuSettings.IsAksOlcuPreset);
ui.Section("1. ölçü katmanları").Layers(_lstLayers1, _btnAdd1, _btnDelList1, _btnRemove1).When(() => !OtoOlcuSettings.IsAksOlcuPreset);
ui.Section("2. ölçü katmanları").Layers(_lstLayers2, _btnAdd2, _btnDelList2, _btnRemove2).Choices(_chkIkinciToplam)
    .When(() => !OtoOlcuSettings.IsAksOlcuPreset && _cmbOlcuTipi.SelectedIndex == 1);
ui.Section(null).Control(_lblAksHint).When(() => OtoOlcuSettings.IsAksOlcuPreset);
ui.Footer(_btnRun, _btnClose);
ui.Finish();''',
"PlanOlcuForm": '''
var ui = BeginCompact(380);
ui.Section("Ölçü ayarları").Field("Hazır şablon", _cmbPreset).Field("Ölçü stili", _cmbDimStyle)
    .Field("Bant genişliği · cm", _txtBandWidth).Field("Tek/çift sınırı · cm", _txtTekCiftSinir)
    .Choices(_chkToplamOlcu, _chkCiftOlcu);
ui.Tabs(146,
    ui.Page("Ana katmanlar", p => p.Layers(_lstLayers, _btnAdd, _btnDelList, _btnRemove)),
    ui.Page("Çift ölçü katmanları", p => p.Layers(_lstCiftLayers, _btnCiftAdd, _btnCiftDelList, _btnCiftRemove)));
ui.Footer(_btnRun, _btnClose);
ui.Finish();''',
"DonatiBoyuForm": '''
var ui = BeginCompact(360);
ui.Section("Etiket düzeni").Field("Donatı ölçeği", _cmbOlcek).Field("Çap işareti / boy etiketi", _txtFi, _cmbLFormat)
    .Choices(_chkYuvarla);
ui.Tabs(146,
    ui.Page("Etiket katmanları", p => p.Layers(_lstLayers, btnAdd, btnDel, btnRem)),
    ui.Page("Çizim katmanları", p => p.Layers(_lstDrawingLayers, btnDrawingAdd, btnDrawingDel, btnDrawingRem)));
ui.Section(null).Choices(_chkOnizleme);
ui.Footer(btnRun, btnClose);
ui.Finish();''',
"DonatiMetrajForm": '''
var ui = BeginCompact(350);
ui.Section("Metraj ayarları").Field("Çap işareti", _txtFi).Field("En büyük boy · cm", _txtMax);
ui.Section("Etiket katmanları").Layers(_lstLayers, btnAdd, btnDel, btnRem);
ui.Section(null).Choices(_chkRenkli, _chkOnizleme);
ui.Footer(btnRun, btnClose, btnAntet, btnToplam);
ui.Finish();''',
"KotDegistirForm": '''
var ui = BeginCompact(330);
ui.Section("Kot değişimi").Field("Eski kot · cm", _txtEski).Field("Yeni kot · cm", _txtYeni);
ui.Section("Kot yazısı katmanları").Layers(_lstLayers, btnAdd, btnDel, btnRem);
ui.Footer(btnRun, btnClose);
ui.Finish();''',
"HasirDonatiForm": '''
var ui = BeginCompact(320);
ui.Section(null).Options(_rbTek, _rbCift);
ui.Section("Hasır ölçüleri").Field("Hasır çeşidi", _cmbTip).Field("Boy / en · cm", _numBoy, _numEn)
    .Field("Bindirme · cm", _numBindirme).Field("Paspayı · cm", _numPas);
ui.Footer(btnRun, btnClose);
ui.Finish();''',
"SehpaDonatiForm": '''
var ui = BeginCompact(340);
ui.Section(null).Options(_rbRadye, _rbMantar);
ui.Section("Kesit ve yerleşim").Field(_lblKalinlik, _numKalinlik).Field("Paspayı · cm", _numPas)
    .Field("Çap · mm / üst kot · m", _cmbCap, _numUstKot)
    .Field("En az genişlik · cm", _numGenislik).Field(_lblAlan, _numAlan);
ui.Footer(btnRun, btnClose);
ui.Finish();''',
"RadyeTemelDonatiForm": '''
var ui = BeginCompact(350);
ui.Section("Radye kesiti").Field("Temel yüksekliği · cm", _numYukseklik).Field("Paspayı · cm", _numPaspayi)
    .Field("Yedek aralık · cm", _numAralik);
ui.Section("İlave donatı").Options(_rbAlt, _rbUst).Field(_chkIlaveGonye, _numGonyeMax);
ui.Tip(_numGonyeMax, "Gönye için en büyük boy (cm)");
ui.Footer(_btnRun, _btnClose);
ui.Finish();''',
"DosemeDonatiForm": '''
var ui = BeginCompact(370);
ui.Section("Donatı yönü").Options(_rbSerbest, _rbX, _rbY);
ui.Section(null).Actions(btnAyar);
ui.Tabs(146,
    ui.Page("Konsol", p => p.Layers(_lstSinir, ui.LayerButtons(_lstSinir))),
    ui.Page("Mesnet", p => p.Layers(_lstMesnet, ui.LayerButtons(_lstMesnet))),
    ui.Page("Boşluk", p => p.Layers(_lstBosluk, ui.LayerButtons(_lstBosluk))),
    ui.Page("Kat sınırı", p => p.Layers(_lstKatSinir, ui.LayerButtons(_lstKatSinir))));
ui.Footer(_btnCizgisel, _btnClose);
ui.Finish();''',
"OtoDonatiForm": '''
var ui = BeginCompact(390);
ui.Section(null).Options(_rbRadye, _rbMantar);
ui.Tabs(258,
    ui.Page("Kesit ve donatı", p => p.Field(_lblKalinlik, _txtKalinlik).Field("Paspayı · cm", _txtPaspayi)
        .Field("Üst kot · m", _txtUstKot).Field("Üst · Ø mm / ara cm", _cmbUstCap, _txtUstAralik)
        .Field("Alt · Ø mm / ara cm", _cmbAltCap, _txtAltAralik).Choices(_chkAltCiz, _chkUstCiz, _chkSehpa)),
    ui.Page("Gönye ve analiz", p => p.Field("En büyük gönye · cm", _txtGonyeBoyu).Field("Analiz eşiği · cm", _txtAnalizEsigi)
        .Control(_gonyeSemasi, 120)));
ui.Footer(_btnRun, _btnClose, _btnManuel);
ui.Finish();''',
"MerdivenForm": '''
var ui = BeginCompact(350);
ui.Tabs(250,
    ui.Page("Geometri", p => p.Field("Kat yüksekliği · cm", _numKat).Field("Alt kot · cm", _numAltKot)
        .Field("Döşeme / kiriş · cm", _numDoseme, _numKirisH).Field("Adet / benzer kat", _numAdet, _numBenzerKat)
        .Field(_chkTemel, _numTemelKal)),
    ui.Page("Donatı", p => p.Field("Ana · Ø mm / ara cm", _cmbAna, _numAnaAralik)
        .Field("Tevzi · Ø mm / ara cm", _cmbTevzi, _numTevziAralik).Field("Paspayı · cm", _numPaspayi).Control(hint)));
ui.Footer(btnPlan, btnClose);
ui.Finish();''',
"TemelHatiliForm": '''
var ui = BeginCompact(380);
ui.Tabs(244,
    ui.Page("Kesit", p => p.Field("Temel yüksekliği · cm", _numTemelH).Field("Temel üst kotu · cm", _numKotu)
        .Field("Döşeme kalınlığı · cm", _numDoseme).Field("Hatıl yük. / en · cm", _numHatilH, _numHatilW).Field("Paspayı · cm", _numPas)),
    ui.Page("Donatı", p => p.Field("Düşey · Ø mm / ara cm", _numDikeyCap, _numDikeyAralik)
        .Field("Yatay · Ø mm / ara cm", _numYatayCap, _numYatayAralik)));
ui.Footer(btnManuel, btnClose, btnPlan);
ui.Finish();''',
"TemelCekuruForm": '''
var ui = BeginCompact(420);
ui.Tabs(256,
    ui.Page("Kesit", p => p.Field("Paspayı / üst kot · cm", _pas, _ustKot).Field("Çukur derinliği · cm", _temel)
        .Field("Üst / alt temel · cm", _ust, _alt).Field("Açıklık / en · cm", _gen, _diger).Field("Grobeton açısı · °", _aci)),
    ui.Page("Hatıl ve donatı", p => p.Field("Hatıl yük. / en · cm", _hatil, _hatilGen).Field("Subasman · cm", _doseme)
        .Field("Üst · Ø mm / ara cm", _ustCap, _ustAralik).Field("Alt · Ø mm / ara cm", _altCap, _altAralik)));
ui.Footer(btn, close, plan);
ui.Finish();''',
"TemelDetayForm": '''
var ui = BeginCompact(430);
ui.Tabs(278,
    ui.Page("Kesit", p => p.Field("Paspayı / üst kot · cm", _pas, _ustKot).Choices(_solKademe, _sagKademe)
        .Field("Sol / sağ kademe · cm", _sol, _sag).Field("Sol / sağ üst temel · cm", _ust, _ustSag)
        .Field("Alt temel / açıklık · cm", _alt, _gen).Field("Grobeton açısı · °", _aci).Field("Kademe uzunluğu · cm", _uzunluk)),
    ui.Page("Hatıl ve donatı", p => p.Field("Hatıl yük. / en · cm", _hatil, _hatilGen).Field("Subasman · cm", _doseme)
        .Field("Üst · Ø mm / ara cm", _ustCap, _ustAralik).Field("Alt · Ø mm / ara cm", _altCap, _altAralik)));
ui.Footer(btn, close);
ui.Finish();''',
"KuranglezForm": '''
var ui = BeginCompact(390);
ui.Tabs(276,
    ui.Page("Geometri", p => p.Field("Açıklık boy / en · cm", _boy, _en).Field("Kapama / perde · cm", _kapama, _perde)
        .Field("Yükseklik · cm", _yuk).Field("Temel kalınlığı · cm", _temel).Field("Temel üst kotu · cm", _kot)),
    ui.Page("Donatı ve metraj", p => p.Field("Beton sınıfı", _beton).Field("Düşey · Ø mm / ara cm", _duseyCap, _duseyAr)
        .Field("Yatay · Ø mm / ara cm", _yatayCap, _yatayAr).Field("Paspayı · cm", _pas).Field("Adet", _adet)));
ui.Footer(btn, close);
ui.Finish();''',
"DosemeKonsolAyarForm": '''
var ui = BeginCompact(350);
var bandRows = new Control[DosemeKonsolSettings.BandCount][];
for (int i = 0; i < bandRows.Length; i++) bandRows[i] = new Control[] { _numBoy[i], _cmbCap[i], _numAralik[i] };
ui.Section("Konsol donatı kademeleri").Grid(new[] { "Azami boy · cm", "Ø · mm", "Aralık · cm" }, bandRows);
ui.Footer(btnOk, btnCancel);
ui.Finish();''',
"ParapetForm": '''
var ui = BeginCompact(390);
ui.Section(null).Field("Yerleşim", _konum);
ui.Tabs(272,
    ui.Page("Kesit", p => p.Field("Döşeme kalınlığı · cm", _numbers["DosemeKalinligi"]).Field("Döşeme üst kotu · cm", _numbers["DosemeUstKotu"])
        .Field("Parapet yük. / en · cm", _numbers["ParapetYuksekligi"], _numbers["ParapetKalinligi"])
        .Field("Sakal / paspayı · cm", _numbers["Sakal"], _numbers["Paspayi"])
        .Columns(ui.Column("Kiriş · cm").Field("En / yükseklik", _numbers["KirisGenisligi"], _numbers["KirisYuksekligi"]).When(() => _konum.SelectedIndex == 2))),
    ui.Page("Donatı ve metraj", p => p.Field("Düşey · Ø mm / ara cm", _dikeyCap, _numbers["DikeyAralik"])
        .Field("Yatay · Ø mm / ara cm", _yatayCap, _numbers["YatayAralik"]).Field("Toplam boy · cm", _numbers["ToplamUzunluk"])
        .Hint("Toplam boy 0 ise metraj tablosu çizilmez.")));
ui.Footer(draw, close);
ui.Finish();''',
"RadyeOtomatikForm": '''
var ui = BeginCompact(460);
ui.Section("STA4CAD projesi").Field("Proje dosyası", project).Actions(browse);
ui.Section("Kaynak durumu").Control(details).Choices(reference);
ui.Footer(draw, cancel);
ui.Finish();''',
"KatmanAyarForm": '''
var ui = BeginCompact(940);
var filter = new TextBox();
ui.Section(null).Field("Ad / özellik ara", filter).Control(tabs, 340);
filter.TextChanged += (s, e) =>
{
    foreach (var grid in new[] { _katman, _olcu, _yazi })
    {
        if (!grid.EndEdit()) continue;
        grid.CurrentCell = null;
        foreach (DataGridViewRow row in grid.Rows)
            if (!row.IsNewRow) row.Visible = string.IsNullOrWhiteSpace(filter.Text) || row.Cells.Cast<DataGridViewCell>()
                .Any(c => Convert.ToString(c.Value).IndexOf(filter.Text, StringComparison.CurrentCultureIgnoreCase) >= 0);
    }
};
ui.Section(null).Control(_uyari).Control(_durum);
ui.Footer(yukle, kapat, kaydet, varsayilan);
ui.Finish();''',
"KolonDuseyForm": '''
var ui = BeginCompact(330);
ui.Section("Çizim").Field("Ölçek", _cmbOlcek);
ui.Tip(_chkTum, _chkTum.Text); ui.Tip(_chkAyri, _chkAyri.Text); ui.Tip(_chkBenzer, _chkBenzer.Text);
_chkTum.Text = "Tüm katlar"; _chkAyri.Text = "Kat kat"; _chkBenzer.Text = "Benzer katlar";
ui.Section("Katların çizimi").Options(_chkTum, _chkAyri, _chkBenzer).Choices(_chkSinirla);
ui.Footer(btnCiz, btnIptal);
ui.Finish();''',
"KolonDuseyKatSinirForm": '''
var ui = BeginCompact(330);
ui.Section("Kat sınırı").Field("Son kat", _cmbSon).Hint("Seçilen kat ve altı çizilir; üst katlar alınmaz.");
ui.Footer(btnOk, btnCancel);
ui.Finish();''',
"KapamaPerdeForm": '''
var ui = BeginCompact(300);
_rbHatiliVar.Text = "Hatıllı"; _rbHatiliYok.Text = "Hatılsız";
ui.Section("Temel hatılı").Options(_rbHatiliVar, _rbHatiliYok);
ui.Footer(btnCiz, btnIptal);
ui.Finish();''',
"KirisAcilimForm": '''
var ui = BeginCompact(350);
ui.Section("Çizim düzeni · 1/25").Options(_antetli, plain);
ui.Section("Kat seçimi").Choices(_tum, selected).Field("Çizilecek kat", _kat);
ui.Footer(draw, cancel);
ui.Finish();''',
"SurekliTemelAcilimForm": '''
var ui = BeginCompact(340);
ui.Section("Çizim düzeni · 1/25").Options(_antetli, plain);
ui.Section(null).Hint("Montaj altta; düz donatı üstte çizilir.");
ui.Footer(draw, cancel);
ui.Finish();''',
"TemelIlaveDenemeForm": '''
var ui = BeginCompact(350);
ui.Section("Zarf yerleşimi").Field("En az genişlik · cm", _genislik).Field("Temel paspayı · cm", _paspay).Field("Düşey hiza farkı · cm", _hiza);
ui.Section(null).Control(not);
ui.Footer(ciz, iptal);
ui.Finish();''',
"KolonApplicationForm": '''
var ui = BeginCompact(390);
ui.Section("Pafta düzeni").Choices(merge, info);
ui.Section("Eklenecek açılımlar").Choices(vertical, kapama);
ui.Section(null).Control(description);
ui.Footer(draw, cancel);
ui.Finish();''',
}

def closing_brace(source, start):
    # Braces inside comments and literals are not C# blocks.
    token = re.compile(r'//[^\n]*|/\*.*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|[{}]', re.S)
    depth = 0
    for match in token.finditer(source, start):
        value = match.group()
        if value == '{': depth += 1
        elif value == '}':
            depth -= 1
            if depth == 0: return match.start()
    raise ValueError('Unbalanced constructor')

for name, body in PROFILES.items():
    files = list((ROOT/'ST4_Yardimci').glob(name+'.cs')) + list((ROOT/'ST4_Plan_ID_Ciz').glob(name+'.cs'))
    if name == 'KolonDuseyKatSinirForm': files = [ROOT/'ST4_Plan_ID_Ciz/KolonDuseyForm.cs']
    path = files[0]
    source = path.read_text(encoding='utf-8-sig')
    pattern = re.compile(r'^        (?:(?:private|internal|public) )?'+re.escape(name)+r'\([^\n]*\)\s*\{', re.M)
    constructor = pattern.search(source)
    if not constructor: raise ValueError('Constructor not found: '+name)
    end = closing_brace(source, source.index('{', constructor.start()))
    if 'BeginCompact(' in source[constructor.end():end]: raise ValueError('Already migrated: '+name)
    insertion = '\n            // Kompakt, içerik odaklı form düzeni.\n' + '\n'.join('            '+line for line in body.strip().splitlines())+'\n        '
    source = source[:end] + insertion + source[end:]
    if name in ('OtoOlcuForm', 'KolonDuseyForm'):
        source = re.sub(r'^\s*AddMonolitHeading\([^\n]*\);\n', '\n', source, flags=re.M)
    path.write_text(source, encoding='utf-8', newline='')
print('Explicit compact layouts installed:', len(PROFILES))
