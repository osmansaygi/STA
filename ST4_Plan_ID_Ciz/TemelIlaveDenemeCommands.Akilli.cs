using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace ST4PlanIdCiz
{
    /// <summary>
    /// Akıllı zarf: ilave aralığı yalnız ana aralık s ve 1,5s (ana /20 → ilave /20, /30); grupta tek çap/aralık ve ortak boy
    /// (AkilliSadelestir). Birkaç aday kademe kümesi çizilmeden (kuru) zarflanır; 12 m kesim planlı sipariş
    /// ağırlığı en az olan seçilir; mevcut zarf kıyas için yazılır.
    /// Her zarf çiziminde metraj ve 12 m kesim planı komut satırına yazılır.
    /// Not: yalnız 12 m'yi tam bölen boylar (200/240/300/400/600) SAS_TML'de net ağırlığı %22 artırdı; fire
    /// boy kısıtıyla değil karma kesim planıyla (ör. 450+450+300, 700+500) düşürülür.
    /// </summary>
    public sealed partial class TemelIlaveDenemeCommands
    {
        /// <summary>Form "Akıllı zarf" düğmesiyle kapanınca true, "Çiz" ile false (TemelIlaveDenemeForm.TryShow yazar).</summary>
        internal static bool AkilliZarf;

        /// <summary>Test: "set:12/40,16/40,..." seçimi atlayıp bu kademe kümesini kullanır.</summary>
        internal static string AkilliDeneme;

        private const double StokBoyCm = 1200.0;

        /// <summary>Kıyas için: grup sadeleştirmesi uygulanmaz.</summary>
        private static bool _sadeKapali;

        /// <summary>Kuru çalışma: KutuYaz çizmez, yalnız metraja ekler.</summary>
        private static bool _kuruCalisma;

        /// <summary>Yüz başına ısı işaretlerini (çizmeden) üretir; aday kademe denemesi için.</summary>
        private delegate List<Isaret> IsaretUretici(List<DonatiAdim> adimlar);

        /// <summary>Yüz başına ısı eşiklerini çizmeden işaretlere çevirir (geri alınan işlemde; çizim yok).</summary>
        private static IsaretUretici Uretici(
            Database db, IList<PlanIdDrawingManager.TemelPlanCopyLayout> yuzKopya, Face[] faces, IList<int[]> ag, List<GprRadyeDugumAs> nodes)
        {
            return adimlar =>
            {
                var liste = new List<Isaret>();
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    for (int i = 0; i < faces.Length && i < yuzKopya.Count; i++)
                    {
                        if (yuzKopya[i].TemelGeom == null) continue;
                        CizBantlar(tr, btr, yuzKopya[i], ag, nodes, faces[i], adimlar, null, liste, false);
                    }
                    tr.Abort();
                }
                return liste;
            };
        }

        /// <summary>
        /// Komutun kademesi. Akıllı zarf kapalıysa mevcut merdiven. Açıksa adaylar kuru zarflanır ve seçilen yazılır.
        /// </summary>
        private static List<DonatiAdim> KademeSec(
            Database db, string st4Path, GprRadyeGenelDonati genel, Editor ed, string komut, IsaretUretici uret)
        {
            List<DonatiAdim> varsayilan = Merdiven(st4Path, genel);
            if (!AkilliZarf) return varsayilan;
            if (AkilliDeneme != null && AkilliDeneme.StartsWith("set:", StringComparison.Ordinal))
                return Merdiven(st4Path, genel, KumeOku(AkilliDeneme.Substring(4)));

            var adaylar = new List<KeyValuePair<string, List<DonatiAdim>>>
            {
                new KeyValuePair<string, List<DonatiAdim>>("mevcut", varsayilan)
            };
            foreach (KeyValuePair<string, List<int[]>> k in HizaliKumeler(ProjeCaplari(st4Path, genel), AnaAralik(genel)))
                adaylar.Add(new KeyValuePair<string, List<DonatiAdim>>(k.Key, Merdiven(st4Path, genel, k.Value)));

            var ozetler = new List<MetrajOzet>();
            foreach (KeyValuePair<string, List<DonatiAdim>> a in adaylar)
            {
                // "mevcut" bugünkü zarfın kendisi (sadeleştirmesiz) kıyas satırıdır.
                _sadeKapali = ReferenceEquals(a.Value, varsayilan);
                MetrajOzet o;
                try { o = KuruMetraj(db, uret(a.Value)); }
                finally { _sadeKapali = false; }
                o.Ad = a.Key;
                o.Kademe = string.Join(" ", a.Value.Select(x => x.Yazi));
                ozetler.Add(o);
            }
            // Mevcut kademe ve sadeleştirmesiz zarf yalnız kıyas içindir. Akıllı adaylardan sipariş ağırlığı en az olan;
            // eşitlikte daha az çap/aralık çeşidi.
            int sec = -1;
            for (int i = 1; i < ozetler.Count; i++)
            {
                MetrajOzet o = ozetler[i];
                if (o.Kutu == 0) continue;
                if (sec < 0) { sec = i; continue; }
                MetrajOzet s = ozetler[sec];
                if (o.Siparis < s.Siparis - 0.5 || (Math.Abs(o.Siparis - s.Siparis) <= 0.5 && o.TipCesit < s.TipCesit)) sec = i;
            }
            if (sec < 0) sec = 0;
            if (ed != null)
            {
                ed.WriteMessage("\n{0}: akıllı zarf adayları (net kg / sipariş kg / çubuk / kutu):", komut);
                for (int i = 0; i < ozetler.Count; i++)
                {
                    MetrajOzet o = ozetler[i];
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture, "\n  {0} {1}: {2:0} / {3:0} / {4} / {5}  [{6}]",
                        i == sec ? "*" : " ", o.Ad, o.Net, o.Siparis, o.Cubuk, o.Kutu, o.Kademe));
                }
            }
            return adaylar[sec].Value;
        }

        /// <summary>
        /// Ana aralığa (s) hizalı aday kümeler: aralık yalnız s ve 1,5s (kullanıcı kuralı 12.11.2026; /40, /10 yok).
        /// "tum": projedeki her çap; "iki-cap": yalnız en küçük ve en büyük çap; "tek-aralik": yalnız s.
        /// </summary>
        private static List<KeyValuePair<string, List<int[]>>> HizaliKumeler(SortedSet<int> caps, int s)
        {
            var sonuc = new List<KeyValuePair<string, List<int[]>>>();
            if (caps == null || caps.Count == 0) return sonuc;
            var c = new SortedSet<int>(caps);
            if (!c.Contains(12)) c.Add(12);
            int min = c.Min, max = c.Max;
            int bucuk = s * 3 % 2 == 0 ? s * 3 / 2 : 0;
            var tum = new List<int[]>();
            foreach (int cap in c)
            {
                if (bucuk > 0) tum.Add(new[] { cap, bucuk });
                tum.Add(new[] { cap, s });
            }
            sonuc.Add(new KeyValuePair<string, List<int[]>>("tum", tum));
            if (c.Count >= 3)
                sonuc.Add(new KeyValuePair<string, List<int[]>>("iki-cap", tum.Where(p => p[0] == min || p[0] == max).ToList()));
            if (bucuk > 0)
                sonuc.Add(new KeyValuePair<string, List<int[]>>("tek-aralik", tum.Where(p => p[1] == s).ToList()));
            return sonuc;
        }

        // ---------------- Grup sadeleştirme ----------------

        /// <summary>
        /// Akıllı zarf: bir zarf grubunda demircinin karşılaşacağı geçişler azaltılır.
        /// 1) Grubun tüm kutuları en güçlü kutunun çap/aralığını alır (tek çap/aralık; donatı alanı hiçbir yerde düşmez).
        /// 2) Aralık yönünde bitişik kutular (zincir) ortak boy kenarına (en dış üst/alt) uzatılır; temel paspayına
        ///    sığmayan kutu kendi boyunda kalır.
        /// 3) Aynı yazı ve aynı boy kenarlı bitişik dikdörtgenler tek kutuda birleşir.
        /// 0) Önce ayrık ısı ayrı zarfa bölünür (AyrikIsiBol); boy ve zincir genişliği en az 240 cm (gönyeli kırpık kutu hariç).
        /// </summary>
        private static void AkilliSadelestir(List<KutuKayit> kutular, List<Isaret> grup)
        {
            if (!AkilliZarf || kutular == null || kutular.Count == 0) return;
            List<KeyValuePair<KutuKayit, KutuKayit>> bolunen = Deneme("bolmeyok")
                ? new List<KeyValuePair<KutuKayit, KutuKayit>>() : AyrikIsiBol(kutular, grup);
            foreach (var yon in kutular.GroupBy(k => k.Yatay).ToList())
            {
                List<KutuKayit> liste = yon.ToList();
                IkiCesit(liste, Deneme("tip1") ? 1 : 2);
                if (Deneme("boyyok")) continue;

                var dik = liste.Where(k => k.Halkalar == null).ToList();
                foreach (List<KutuKayit> zincir in Zincirler(dik))
                {
                    double b0 = zincir.Min(k => k.B0), b1 = zincir.Max(k => k.B1);
                    if (Deneme("firesizyok") || !FiresizBoyaOtur(zincir, b0, b1))
                    {
                        if (zincir.Count < 2) continue;
                        foreach (KutuKayit k in zincir)
                        {
                            double e0 = k.B0, e1 = k.B1;
                            k.B0 = b0;
                            k.B1 = b1;
                            if (!SinirKapsar(k.X0, k.Y0, k.X1, k.Y1)) { k.B0 = e0; k.B1 = e1; }
                        }
                    }
                    if (!Deneme("genislikyok")) EnAzGenislik(zincir, kutular);
                    if (zincir.Count >= 2) Birlestir(zincir, kutular);
                }
            }
            // Ayrılan leke kutusu, standart boy/genişlik sonrası ana kutuya biniyorsa ayırmak anlamsız: geri birleşir.
            foreach (KeyValuePair<KutuKayit, KutuKayit> p in bolunen)
            {
                KutuKayit ana = p.Key, leke = p.Value;
                if (!kutular.Contains(ana) || !kutular.Contains(leke)) continue;
                if (Math.Min(ana.X1, leke.X1) - Math.Max(ana.X0, leke.X0) <= 0.5 ||
                    Math.Min(ana.Y1, leke.Y1) - Math.Max(ana.Y0, leke.Y0) <= 0.5) continue;
                double e0 = ana.B0, e1 = ana.B1;
                ana.B0 = Math.Min(ana.B0, leke.B0);
                ana.B1 = Math.Max(ana.B1, leke.B1);
                if (!SinirKapsar(ana.X0, ana.Y0, ana.X1, ana.Y1)) { ana.B0 = e0; ana.B1 = e1; continue; }
                if (YaziAlani(leke.Yazi) > YaziAlani(ana.Yazi)) ana.Yazi = leke.Yazi;
                kutular.Remove(leke);
            }
        }

        /// <summary>Akıllı zarfta en kısa donatı boyu ve en dar zarf genişliği (cm). Temel kenarında kırpılan (gönyeli) kutular hariç.</summary>
        private const double AkilliEnAzCm = 240.0;

        /// <summary>Çubuk yönünde bu kadar boşlukla ayrılan ısı kümeleri ayrı zarf olur (cm).</summary>
        private const double AyrikIsiBoslukCm = 60.0;

        /// <summary>
        /// Ayrık ısı: kutunun içindeki ısı, çubuk (boy) yönünde ≥ 60 cm boşlukla birden çok kümeye ayrılıyorsa
        /// kutu yalnız en büyük kümeyi örter; diğer kümeler için kendi zarfı açılır (çap/aralığı o kümeye değen
        /// en güçlü işaretten). Böylece ana ısıdan kopuk küçük bir leke tüm zinciri uzatmaz.
        /// Dönen çiftler (ana, leke): sonradan çakışırlarsa geri birleştirilir.
        /// </summary>
        private static List<KeyValuePair<KutuKayit, KutuKayit>> AyrikIsiBol(List<KutuKayit> kutular, List<Isaret> grup)
        {
            var sonuc = new List<KeyValuePair<KutuKayit, KutuKayit>>();
            if (grup == null || grup.Count == 0) return sonuc;
            var gf = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory();
            foreach (KutuKayit k in kutular.Where(x => x.Halkalar == null).ToList())
            {
                NetTopologySuite.Geometries.Polygon kutu = KutuPoligon(gf, k.X0, k.Y0, k.X1, k.Y1);
                var parcalar = new List<KeyValuePair<NetTopologySuite.Geometries.Geometry, Isaret>>();
                foreach (Isaret i in grup)
                {
                    if (i.Sekil == null || i.Sekil.IsEmpty || i.Yatay != k.Yatay) continue;
                    try
                    {
                        if (!i.Sekil.EnvelopeInternal.Intersects(kutu.EnvelopeInternal)) continue;
                        NetTopologySuite.Geometries.Geometry g = i.Sekil.Intersection(kutu);
                        for (int n = 0; n < g.NumGeometries; n++)
                        {
                            var p = g.GetGeometryN(n);
                            if (p is NetTopologySuite.Geometries.Polygon && p.Area >= 50.0)
                                parcalar.Add(new KeyValuePair<NetTopologySuite.Geometries.Geometry, Isaret>(p, i));
                        }
                    }
                    catch (System.Exception)
                    {
                    }
                }
                if (parcalar.Count < 2) continue;
                // Boy (B) ekseninde aralık kümeleri.
                Func<NetTopologySuite.Geometries.Envelope, double> e0 = e => k.Yatay ? e.MinX : e.MinY;
                Func<NetTopologySuite.Geometries.Envelope, double> e1 = e => k.Yatay ? e.MaxX : e.MaxY;
                var sirali = parcalar.OrderBy(p => e0(p.Key.EnvelopeInternal)).ToList();
                var kumeler = new List<List<KeyValuePair<NetTopologySuite.Geometries.Geometry, Isaret>>>();
                double son = double.MinValue;
                foreach (var p in sirali)
                {
                    var e = p.Key.EnvelopeInternal;
                    if (kumeler.Count == 0 || e0(e) - son >= AyrikIsiBoslukCm)
                        kumeler.Add(new List<KeyValuePair<NetTopologySuite.Geometries.Geometry, Isaret>>());
                    kumeler[kumeler.Count - 1].Add(p);
                    son = Math.Max(son, e1(e));
                }
                if (kumeler.Count < 2) continue;
                var anaKume = kumeler.OrderByDescending(c => c.Sum(p => p.Key.Area)).First();
                foreach (var c in kumeler)
                {
                    var env = new NetTopologySuite.Geometries.Envelope();
                    foreach (var p in c) env.ExpandToInclude(p.Key.EnvelopeInternal);
                    if (ReferenceEquals(c, anaKume))
                    {
                        k.B0 = Math.Max(k.B0, e0(env));
                        k.B1 = Math.Min(k.B1, e1(env));
                        continue;
                    }
                    string yazi = c.Select(p => p.Value.Yazi).OrderByDescending(YaziAlani).FirstOrDefault() ?? k.Yazi;
                    var leke = new KutuKayit
                    {
                        Yatay = k.Yatay, Yazi = yazi,
                        X0 = k.X0, X1 = k.X1, Y0 = k.Y0, Y1 = k.Y1,
                        IstenenB0 = e0(env), IstenenB1 = e1(env)
                    };
                    leke.B0 = e0(env);
                    leke.B1 = e1(env);
                    // Lekenin aralık yönü kendi ısısına iner (en az genişlik sonra uygulanır).
                    leke.A0 = Math.Max(k.A0, k.Yatay ? env.MinY : env.MinX);
                    leke.A1 = Math.Min(k.A1, k.Yatay ? env.MaxY : env.MaxX);
                    kutular.Add(leke);
                    sonuc.Add(new KeyValuePair<KutuKayit, KutuKayit>(k, leke));
                }
            }
            return sonuc;
        }

        /// <summary>
        /// Zincirin aralık yönündeki toplam genişliği 240 cm'den azsa dış kutular iki yana eşit genişler
        /// (temel paspayına ya da aynı yöndeki başka kutuya dayanan yan karşıya aktarılır). Gönyeli (kırpık) kutu yok sayılır.
        /// </summary>
        private static void EnAzGenislik(List<KutuKayit> zincir, List<KutuKayit> tumu)
        {
            double a0 = zincir.Min(k => k.A0), a1 = zincir.Max(k => k.A1);
            double eksik = AkilliEnAzCm - (a1 - a0);
            if (eksik <= 0.5) return;
            KutuKayit alt = zincir.OrderBy(k => k.A0).First(), ust = zincir.OrderByDescending(k => k.A1).First();
            bool Serbest(KutuKayit k)
            {
                if (!SinirKapsar(k.X0, k.Y0, k.X1, k.Y1)) return false;
                foreach (KutuKayit d in tumu)
                {
                    if (ReferenceEquals(d, k) || d.Yatay != k.Yatay || zincir.Contains(d)) continue;
                    if (Math.Min(d.X1, k.X1) - Math.Max(d.X0, k.X0) > 0.5 && Math.Min(d.Y1, k.Y1) - Math.Max(d.Y0, k.Y0) > 0.5)
                        return false;
                }
                return true;
            }
            foreach (double pay0 in new[] { eksik * 0.5, 0.0, eksik })
            {
                double ea0 = alt.A0, ea1 = ust.A1;
                alt.A0 = ea0 - pay0;
                ust.A1 = ea1 + (eksik - pay0);
                if (Serbest(alt) && Serbest(ust)) return;
                alt.A0 = ea0;
                ust.A1 = ea1;
            }
        }

        /// <summary>12 m firesiz boylar: 1200 bölenleri (200, 240, 300, 400, 600) ve bir bölenle 1200 tamamlayanlar (800+400, 900+300, 960+240, 1000+200).</summary>
        private static readonly double[] FiresizBoyCm = { 200, 240, 300, 400, 600, 800, 900, 960, 1000, 1200 };

        /// <summary>
        /// Zincirin ortak boyu [b0, b1]'i örten en kısa standart boy (200…1200; 12 m kesim planında 450+450+300, 700+500 gibi eşlenir)
        /// seçilir (kullanıcı tercihi 12.11.2026; test "firesiz": önce 1200 bölenleri). Ortada, alt uca ya da üst uca dayalı yerleşim
        /// denenir; zincirin her kutusu temel paspayına sığmalıdır. Hiçbiri sığmazsa false (kutular dokunulmadan kalır).
        /// </summary>
        private static bool FiresizBoyaOtur(List<KutuKayit> zincir, double b0, double b1)
        {
            double boy = b1 - b0;
            foreach (double[] liste in Deneme("firesiz") ? new[] { FiresizBoyCm, TemelIlaveKutuBol.StandartBoyCm } : new[] { TemelIlaveKutuBol.StandartBoyCm })
                foreach (double f in liste)
                {
                    if (f < boy - 0.5 || f < AkilliEnAzCm - 0.5) continue;
                    foreach (double p0 in new[] { (b0 + b1 - f) * 0.5, b0, b1 - f })
                    {
                        bool sigar = true;
                        var eski = zincir.Select(k => new[] { k.B0, k.B1 }).ToList();
                        foreach (KutuKayit k in zincir)
                        {
                            k.B0 = p0;
                            k.B1 = p0 + f;
                            if (!SinirKapsar(k.X0, k.Y0, k.X1, k.Y1)) { sigar = false; break; }
                        }
                        if (sigar) return true;
                        for (int i = 0; i < zincir.Count; i++) { zincir[i].B0 = eski[i][0]; zincir[i].B1 = eski[i][1]; }
                    }
                }
            return false;
        }

        private static bool Deneme(string anahtar) =>
            AkilliDeneme != null && AkilliDeneme.Split(';').Contains(anahtar);

        /// <summary>
        /// Grubu en çok <paramref name="cesit"/> çap/aralığa indirir; hiçbir kutunun donatı alanı düşmez.
        /// İki çeşitte eşik t seçilir: alanı t'ye kadar olan kutular t'yi, üstündekiler en güçlüyü alır;
        /// t, toplam (kutu alanı × As) en az olacak biçimde seçilir (ağırlık As ile orantılı).
        /// </summary>
        private static void IkiCesit(List<KutuKayit> liste, int cesit)
        {
            var yazilar = liste.Select(k => k.Yazi).Where(y => YaziAlani(y) > 0).Distinct()
                .OrderBy(YaziAlani).ToList();
            if (yazilar.Count <= cesit) return;
            string enGuclu = yazilar[yazilar.Count - 1];
            string esik = null;
            if (cesit >= 2)
            {
                double enAz = double.MaxValue;
                for (int i = 0; i < yazilar.Count - 1; i++)
                {
                    double tA = YaziAlani(yazilar[i]), sA = YaziAlani(enGuclu), maliyet = 0;
                    foreach (KutuKayit k in liste)
                    {
                        double alan = (k.X1 - k.X0) * (k.Y1 - k.Y0);
                        maliyet += alan * (YaziAlani(k.Yazi) <= tA + 1e-9 ? tA : sA);
                    }
                    if (maliyet < enAz) { enAz = maliyet; esik = yazilar[i]; }
                }
            }
            double esikA = esik == null ? -1 : YaziAlani(esik);
            foreach (KutuKayit k in liste)
            {
                if (YaziAlani(k.Yazi) <= 0) continue;
                k.Yazi = esik != null && YaziAlani(k.Yazi) <= esikA + 1e-9 ? esik : enGuclu;
            }
        }

        /// <summary>Aralık yönünde bitişik (≤ 15 cm boşluk, boyda ≥ 10 cm ortak) dikdörtgen kümeleri.</summary>
        private static List<List<KutuKayit>> Zincirler(List<KutuKayit> dik)
        {
            bool Bitisik(KutuKayit p, KutuKayit q)
            {
                if (Math.Min(p.B1, q.B1) - Math.Max(p.B0, q.B0) < 10.0) return false;
                double ara = Math.Max(p.A0, q.A0) - Math.Min(p.A1, q.A1);
                return ara <= NetKomsuCm;
            }
            var sonuc = new List<List<KutuKayit>>();
            var kalan = new HashSet<KutuKayit>(dik);
            while (kalan.Count > 0)
            {
                KutuKayit ilk = kalan.First();
                kalan.Remove(ilk);
                var zincir = new List<KutuKayit> { ilk };
                for (int i = 0; i < zincir.Count; i++)
                    foreach (KutuKayit d in kalan.ToList())
                        if (Bitisik(zincir[i], d)) { kalan.Remove(d); zincir.Add(d); }
                sonuc.Add(zincir);
            }
            return sonuc;
        }

        /// <summary>Aynı yazı ve boy kenarlı, aralık yönünde ardışık kutuları tek kutu yapar (fazlası listeden çıkar).</summary>
        private static void Birlestir(List<KutuKayit> zincir, List<KutuKayit> tumu)
        {
            foreach (var g in zincir.GroupBy(k => k.Yazi + "|" + Math.Round(k.B0, 1) + "|" + Math.Round(k.B1, 1)))
            {
                var sirali = g.OrderBy(k => k.A0).ToList();
                KutuKayit bas = sirali[0];
                for (int i = 1; i < sirali.Count; i++)
                {
                    KutuKayit k = sirali[i];
                    if (k.A0 - bas.A1 > NetKomsuCm) { bas = k; continue; }
                    double a1 = Math.Max(bas.A1, k.A1);
                    double e1 = bas.A1;
                    bas.A1 = a1;
                    if (!SinirKapsar(bas.X0, bas.Y0, bas.X1, bas.Y1)) { bas.A1 = e1; bas = k; continue; }
                    bas.Kesik1 = k.Kesik1;
                    tumu.Remove(k);
                }
            }
        }

        /// <summary>"ø16/20" → cm²/m; okunamazsa 0. "yetmez" ekli yazı aynı alanın üstünde sayılır.</summary>
        private static double YaziAlani(string yazi)
        {
            if (string.IsNullOrEmpty(yazi)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(yazi, @"[øØ]\s*(\d+)\s*/\s*(\d+)");
            if (!m.Success) return 0;
            double a = GprRadyeAyir.AlanCm2Pm(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
            if (yazi.IndexOf("yetmez", StringComparison.OrdinalIgnoreCase) < 0) return a;
            // "ø20/10 yetmez - ø20/4 yeter": gereken alan ikinci yazıdan (daha sıkı aralık daha güçlü sayılır).
            var y = m.NextMatch();
            if (y.Success)
            {
                double b = GprRadyeAyir.AlanCm2Pm(int.Parse(y.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(y.Groups[2].Value, CultureInfo.InvariantCulture));
                if (b > a) return b;
            }
            return a + 0.001;
        }

        private static List<int[]> KumeOku(string metin)
        {
            var ham = new List<int[]>();
            foreach (string p in metin.Split(','))
            {
                string[] c = p.Split('/');
                ham.Add(new[] { int.Parse(c[0], CultureInfo.InvariantCulture), int.Parse(c[1], CultureInfo.InvariantCulture) });
            }
            return ham;
        }

        /// <summary>Radye ana donatı aralıklarının en sık olanı (yoksa 20).</summary>
        private static int AnaAralik(GprRadyeGenelDonati genel)
        {
            if (genel == null) return 20;
            int[] a = { genel.AralikXAltCm, genel.AralikYAltCm, genel.AralikXUstCm, genel.AralikYUstCm };
            var gecerli = a.Where(v => v >= 10 && v <= 40).ToList();
            if (gecerli.Count == 0) return 20;
            return gecerli.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
        }

        /// <summary>İşaretleri çizmeden zarflar, metraj özetini döner (yalnız KUTU katmanları hazırlanabilir).</summary>
        private static MetrajOzet KuruMetraj(Database db, List<Isaret> isaretler)
        {
            MetrajBaslat();
            _kuruCalisma = true;
            try
            {
                if (isaretler != null && isaretler.Count > 0)
                    CizYonKutulariIc(db, isaretler, 0.0);
                return MetrajHesapla(_metraj);
            }
            finally
            {
                _kuruCalisma = false;
                _metraj = null;
            }
        }

        // ---------------- Metraj ve kesim planı ----------------

        private sealed class MetrajKutu
        {
            public int CapMm;
            public double AralikCm;
            public double BoyCm;
            public double BantCm;
        }

        private sealed class MetrajOzet
        {
            public string Ad, Kademe;
            public int Kutu, Cubuk, BoyCesit, TipCesit;
            public double Net, Siparis;
            public List<string> CapSatirlari = new List<string>();
        }

        private static List<MetrajKutu> _metraj;

        private static void MetrajBaslat() => _metraj = new List<MetrajKutu>();

        /// <summary>KutuYaz'ın çizdiği (ya da kuru çalışmada çizeceği) her kutu.</summary>
        private static void MetrajEkle(KutuKayit k)
        {
            if (_metraj == null || k == null || string.IsNullOrEmpty(k.Yazi)) return;
            var m = System.Text.RegularExpressions.Regex.Match(k.Yazi, @"[øØ]\s*(\d+)\s*/\s*(\d+)");
            if (!m.Success) return;
            _metraj.Add(new MetrajKutu
            {
                CapMm = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                AralikCm = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                BoyCm = k.B1 - k.B0,
                BantCm = k.A1 - k.A0
            });
        }

        /// <summary>RADYEILAVECIZ ile aynı adet: bant / aralık tabanı + 1.</summary>
        private static int MetrajAdet(double bantCm, double aralikCm)
        {
            double s = aralikCm > 0.5 ? aralikCm : 15.0;
            if (bantCm < 0.5) return 1;
            return Math.Max(1, (int)Math.Floor(bantCm / s + 1e-9) + 1);
        }

        private static double BirimKg(int capMm) => capMm * capMm * 0.00617;

        private static MetrajOzet MetrajHesapla(List<MetrajKutu> liste)
        {
            var o = new MetrajOzet();
            if (liste == null) return o;
            var ci = CultureInfo.InvariantCulture;
            var boylar = new HashSet<int>();
            var tipler = new HashSet<string>();
            o.Kutu = liste.Count;
            foreach (var capGrup in liste.GroupBy(k => k.CapMm).OrderBy(g => g.Key))
            {
                var parcalar = new List<int>();
                foreach (MetrajKutu k in capGrup)
                {
                    int boy = (int)Math.Ceiling(k.BoyCm - 0.5);
                    boylar.Add(boy);
                    tipler.Add(capGrup.Key + "/" + k.AralikCm.ToString("0", ci));
                    int adet = MetrajAdet(k.BantCm, k.AralikCm);
                    for (int i = 0; i < adet; i++) parcalar.Add(boy);
                }
                List<List<int>> stoklar = KesimPlani(parcalar, (int)StokBoyCm);
                double w = BirimKg(capGrup.Key);
                double n = parcalar.Sum() / 100.0 * w;
                double s = stoklar.Count * StokBoyCm / 100.0 * w;
                o.Cubuk += parcalar.Count;
                o.Net += n;
                o.Siparis += s;
                o.CapSatirlari.Add(string.Format(ci, "ø{0}: {1} çubuk, net {2:0} kg, {3} adet 12 m, sipariş {4:0} kg, fire %{5:0.0}; kesim {6}",
                    capGrup.Key, parcalar.Count, n, stoklar.Count, s, s > 0 ? (s - n) / s * 100.0 : 0, KalipOzeti(stoklar)));
            }
            o.BoyCesit = boylar.Count;
            o.TipCesit = tipler.Count;
            return o;
        }

        /// <summary>Komut sonunda: kutu, çubuk, net kg, 12 m stok, sipariş kg, fire ve sık kesim kalıpları.</summary>
        private static void MetrajRapor(Editor ed, string komut)
        {
            List<MetrajKutu> liste = _metraj;
            _metraj = null;
            if (ed == null || liste == null || liste.Count == 0) return;
            MetrajOzet o = MetrajHesapla(liste);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\n{0} zarf metrajı{1}: {2} kutu, {3} çubuk, {4} boy, {5} çap/aralık; net {6:0} kg, 12 m kesim planıyla sipariş {7:0} kg, fire %{8:0.0}",
                komut, AkilliZarf ? " (akıllı)" : "", o.Kutu, o.Cubuk, o.BoyCesit, o.TipCesit,
                o.Net, o.Siparis, o.Siparis > 0 ? (o.Siparis - o.Net) / o.Siparis * 100.0 : 0));
            foreach (string s in o.CapSatirlari) ed.WriteMessage("\n    " + s);
        }

        /// <summary>Sık kesim kalıpları: "51×[450+450+300] 39×[700+500]".</summary>
        private static string KalipOzeti(List<List<int>> stoklar)
        {
            return string.Join(" ", stoklar
                .Select(p => "[" + string.Join("+", p.OrderByDescending(v => v)) + "]")
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .Select(g => g.Count() + "×" + g.Key));
        }

        /// <summary>
        /// 1B kesim planı: büyükten küçüğe, en az boşluk bırakan açık stoğa yerleşim (best-fit decreasing).
        /// Stoktan uzun parça tam stoklar + artık parça olarak sayılır (ek boyu).
        /// </summary>
        internal static List<List<int>> KesimPlani(List<int> parcalar, int stok)
        {
            var tamStok = new List<List<int>>();
            var acik = new List<List<int>>();
            var kalan = new List<int>();
            foreach (int p in parcalar.OrderByDescending(v => v))
            {
                int parca = p;
                while (parca >= stok)
                {
                    tamStok.Add(new List<int> { stok });
                    parca -= stok;
                }
                if (parca <= 0) continue;
                int en = -1;
                for (int i = 0; i < kalan.Count; i++)
                    if (kalan[i] >= parca && (en < 0 || kalan[i] < kalan[en])) en = i;
                if (en < 0)
                {
                    acik.Add(new List<int> { parca });
                    kalan.Add(stok - parca);
                    continue;
                }
                acik[en].Add(parca);
                kalan[en] -= parca;
            }
            tamStok.AddRange(acik);
            return tamStok;
        }
    }
}
