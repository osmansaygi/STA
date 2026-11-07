using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        /// <summary>KIRISCIZ: form donatısı ve uç tipi; doluyken KMX/BEA okunmaz.</summary>
        private sealed class KirisManuelKaynak
        {
            public Dictionary<BeamInfo, KirisAcilimMontajBilgi> Montaj = new Dictionary<BeamInfo, KirisAcilimMontajBilgi>();
            public Dictionary<BeamInfo, KirisAcilimUcBilgi> UcTipi = new Dictionary<BeamInfo, KirisAcilimUcBilgi>();
            public Dictionary<BeamInfo, string> Adlar = new Dictionary<BeamInfo, string>();
            /// <summary>Saplama mesnedi için kurulan yan kirişler: görünüşte kesit olarak, açılım olarak çizilmez.</summary>
            public HashSet<BeamInfo> Yardimci = new HashSet<BeamInfo>();
        }

        private KirisManuelKaynak _kirisManuel;

        private const int KirisManuelKat = 1;
        private const int KirisManuelAksY = 101, KirisManuelAksYUst = 102, KirisManuelAksYAlt = 103;
        /// <summary>Döşeme ve saplama kirişinin kiriş ekseninden yanlara uzandığı mesafe (cm).</summary>
        private const double KirisManuelYanCm = 300.0;

        /// <summary>
        /// Formdaki sürekli kirişten tek katlı sentetik ST4 modeli kurar: X aksları mesnet ekseni, tek Y aksı kiriş ekseni,
        /// kolon mesnetleri dikdörtgen kolon, saplama mesnetleri dik yan kiriş, döşeme iki yanda 300 cm şerit.
        /// Kat kotu bina taban kotu olur; kiriş kotu kat kotuna göre.
        /// </summary>
        internal static St4Model KirisManuelModelKur(KirisManuelVeri v)
        {
            var model = new St4Model
            {
                BeamFloorKeyStep = 1000,
                ColumnFloorKeyStep = 1000,
                SlabFloorKeyStep = 1000,
                BuildingBaseKotu = v.KatKotuCm / 100.0
            };
            model.Floors.Add(new FloorInfo(KirisManuelKat, "KIRIS", "", 0.0));
            int n = v.Acikliklar.Count;
            double maxB = v.Acikliklar.Max(a => a.BCm);
            double maxTop = v.Acikliklar.Max(a => a.DkotCm);
            double minBot = v.Acikliklar.Min(a => a.DkotCm - a.HCm);

            double x = 0;
            for (int k = 0; k <= n; k++)
            {
                if (k > 0)
                {
                    x += MesnetYari(v.Mesnetler[k - 1]) + v.Acikliklar[k - 1].LnCm + MesnetYari(v.Mesnetler[k]);
                }
                model.AxisX.Add(new AxisLine(k + 1, AxisKind.X, x, 0.0));
            }
            model.AxisY.Add(new AxisLine(KirisManuelAksY, AxisKind.Y, 0.0, 0.0));
            model.AxisY.Add(new AxisLine(KirisManuelAksYUst, AxisKind.Y, -KirisManuelYanCm, 0.0));
            model.AxisY.Add(new AxisLine(KirisManuelAksYAlt, AxisKind.Y, KirisManuelYanCm, 0.0));

            for (int i = 0; i < n; i++)
            {
                var a = v.Acikliklar[i];
                model.Beams.Add(new BeamInfo
                {
                    BeamId = KirisManuelKat * 1000 + i + 1,
                    FixedAxisId = KirisManuelAksY,
                    StartAxisId = i + 1,
                    EndAxisId = i + 2,
                    WidthCm = a.BCm,
                    HeightCm = a.HCm,
                    Point1KotCm = a.DkotCm,
                    Point2KotCm = a.DkotCm
                });
                if (a.DosemeCm > 0.5)
                {
                    model.Slabs.Add(new SlabInfo { SlabId = KirisManuelKat * 1000 + 2 * i + 1, ThicknessCm = a.DosemeCm, Axis1 = i + 1, Axis2 = i + 2, Axis3 = KirisManuelAksY, Axis4 = KirisManuelAksYUst });
                    model.Slabs.Add(new SlabInfo { SlabId = KirisManuelKat * 1000 + 2 * i + 2, ThicknessCm = a.DosemeCm, Axis1 = i + 1, Axis2 = i + 2, Axis3 = KirisManuelAksY, Axis4 = KirisManuelAksYAlt });
                }
            }

            for (int k = 0; k <= n; k++)
            {
                var m = v.Mesnetler[k];
                if (m.Tip == KirisManuelMesnetTip.Kolon)
                {
                    int sid = KirisManuelKat * 1000 + k + 1;
                    model.Columns.Add(new ColumnAxisInfo { ColumnNo = k + 1, ColumnType = 1, AxisXId = k + 1, AxisYId = KirisManuelAksY, ColumnId = sid });
                    // W kiriş doğrultusunda (mesnet eni), H dik yönde kirişten geniş.
                    model.ColumnDimsBySectionId[sid] = (m.EniCm, Math.Max(40.0, Math.Ceiling((maxB + 20.0) / 5.0) * 5.0));
                    double altM = (minBot - 300.0) / 100.0;
                    double ustM = v.SonKat ? maxTop / 100.0 : (maxTop + 300.0) / 100.0;
                    model.ColumnKotMFromBinaTabaniBySectionId[sid] = (ustM, altM);
                }
                else if (m.Tip == KirisManuelMesnetTip.Kiris)
                {
                    // Saplama: mesnet ekseninde kirişe dik yan kiriş; yüksekliği komşu açıklıkların büyüğü.
                    var komsu = new List<KirisManuelAciklik>();
                    if (k > 0) komsu.Add(v.Acikliklar[k - 1]);
                    if (k < n) komsu.Add(v.Acikliklar[k]);
                    model.Beams.Add(new BeamInfo
                    {
                        BeamId = KirisManuelKat * 1000 + 900 + k + 1,
                        FixedAxisId = k + 1,
                        StartAxisId = KirisManuelAksYAlt,
                        EndAxisId = KirisManuelAksYUst,
                        WidthCm = m.EniCm,
                        HeightCm = komsu.Max(a => a.HCm),
                        Point1KotCm = komsu.Max(a => a.DkotCm),
                        Point2KotCm = komsu.Max(a => a.DkotCm)
                    });
                }
            }
            return model;
        }

        private static double MesnetYari(KirisManuelMesnet m) => m.Tip == KirisManuelMesnetTip.Serbest ? 0.0 : m.EniCm / 2.0;

        /// <summary>KIRISCIZ çizimi: model <see cref="KirisManuelModelKur"/> ile kurulmuş olmalı.</summary>
        internal bool DrawKirisManuel(Database db, Editor ed, Point3d insert, KirisManuelVeri v)
        {
            var kaynak = new KirisManuelKaynak();
            int n = v.Acikliklar.Count;
            foreach (var beam in _model.Beams)
            {
                int no = beam.BeamId % 1000;
                if (no > 900) { kaynak.Yardimci.Add(beam); continue; }
                var a = v.Acikliklar[no - 1];
                KirisManuelVeri.TryDonati(a.Ust, out int ua, out int uc);
                KirisManuelVeri.TryDonati(a.Alt, out int aa, out int ac);
                KirisManuelVeri.TryDonati(a.Govde, out int ga, out int gc);
                KirisManuelVeri.TryDonati(a.SolUst, out int sua, out int suc);
                KirisManuelVeri.TryDonati(a.SagUst, out int gua, out int guc);
                KirisManuelVeri.TryDonati(a.SolAlt, out int saa, out int sac);
                KirisManuelVeri.TryDonati(a.SagAlt, out int gaa, out int gac);
                KirisManuelVeri.TryEtriye(a.Etriye, out int ec, out int es, out int eo);
                kaynak.Montaj[beam] = new KirisAcilimMontajBilgi
                {
                    Adet = ua, Cap = uc, DuzAdet = aa, DuzCap = ac, GovdeAdet = ga, GovdeCap = gc,
                    SolUstAdet = sua, SolUstCap = suc, SagUstAdet = gua, SagUstCap = guc,
                    SolAltAdet = saa, SolAltCap = sac, SagAltAdet = gaa, SagAltCap = gac,
                    EtriyeCap = ec, EtriyeSikCm = es, EtriyeOrtaCm = eo
                };
                short code = 0;
                if (no == 1 && v.Mesnetler[0].Tip == KirisManuelMesnetTip.Serbest) code = 3;
                if (no == n && v.Mesnetler[n].Tip == KirisManuelMesnetTip.Serbest) code = 4;
                kaynak.UcTipi[beam] = new KirisAcilimUcBilgi { Code = code };
                kaynak.Adlar[beam] = a.Ad.Trim();
            }

            var secenekler = new KirisAcilimSecenekleri
            {
                Antetli = false,
                SeciliKatNo = KirisManuelKat,
                AltIkinciGonyeKaldir = v.IkinciGonyeKaldir,
                PaspayiCm = v.PaspayiCm
            };
            _kirisManuel = kaynak;
            _kirisTekEtriye = v.TekEtriye;
            _lbPhiKonumII = Ts500LbPhiKonumII(v.Fck, v.Fyk);
            _lbPhiKonumI = 1.4 * _lbPhiKonumII;
            ed?.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nKIRISCIZ: C{0}, fyk={1} MPa → ℓb Konum II {2:0.##}φ, Konum I {3:0.##}φ.", v.Fck, v.Fyk, _lbPhiKonumII, _lbPhiKonumI));
            try
            {
                return DrawKirisAcilimlariSecenekli(db, ed, insert, null, secenekler);
            }
            finally
            {
                _kirisManuel = null;
                _kirisTekEtriye = false;
                _lbPhiKonumI = KirisAcilimLbPhiKonumI;
                _lbPhiKonumII = KirisAcilimLbPhiKonumII;
            }
        }
    }
}
