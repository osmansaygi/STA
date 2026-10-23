# Execute section selection/grouping/rebar geometry from production, without an AutoCAD host.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/PlanIdDrawingManager.KirisAcilim.Kesit.cs'))
function Extract([string]$signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing production member: $signature" }
    $open = $source.IndexOf('{', $start)
    $depth = 1; $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$members = @(
    (Extract 'private static List<(double X0, double Y0, double X1, double Y1, double Lane)> KirisKesitYanOlculeri('),
    (Extract 'private void KirisAcilimKesitBaslat('),
    (Extract 'private void KirisAcilimKesitTakimBaslat('),
    (Extract 'private static double KirisKesitSonrakiX('),
    (Extract 'private static string KirisKesitEtriyeCarpan('),
    (Extract 'private static List<(double Lo, double Hi)> KirisKesitEtriyeKutulari('),
    (Extract 'private void KirisKesitMontajHazirla('),
    ([regex]::Match($source, 'private static double KirisGovdeAralik\([^\r\n]+\r?\n[^\r\n]+').Value),
    (Extract 'private static (int Adet, int Cap) KirisGovdeTahvil('),
    (Extract 'private static (double AsAna, double AsGerek, double SAlan, double SMax, double NetMin, double EksenMin) SurekliDagitmaKurali('),
    ([regex]::Match($source, 'private const double SurekliDagitmaMaxAralikCm[^\r\n]+').Value),
    ([regex]::Match($source, 'private const double KirisGovdeMinAralikCm[^\r\n]+').Value),
    (Extract 'private static double KirisKesitKartYuksekligi('),
    (Extract 'private static double KirisKesitYerlesimAlti('),
    (Extract 'private sealed class KirisKesitDonati'),
    (Extract 'private sealed class KirisKesitSira'),
    (Extract 'private sealed class KirisKesitCubuk'),
    (Extract 'private static List<double> KirisKesitSiraXleri('),
    (Extract 'private static List<KirisKesitCubuk> KirisKesitCubukYerlesimi('),
    (Extract 'private sealed class KirisKesitBilgi'),
    (Extract 'private static bool KirisKesitYolKes('),
    (Extract 'private static (double Ust, double Alt) KirisKesitDosemeKotlari('),
    (Extract 'private static List<Point2d> KirisKesitCubukMerkezleri('),
    (Extract 'private static string KirisKesitHarf('),
    (Extract 'private static string KirisKesitImza('),
    (Extract 'private static double KirisKesitIstasyonBindirmedenKac('),
    (Extract 'private static double KirisKesitIstasyonSec('),
    (Extract 'private static bool KirisKesitAraliktaMi('),
    ([regex]::Match($source, 'private static double KirisKesitIstasyon\(double[^\r\n]+').Value)
) -join "`n"
$sectionSource = $source
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Plan_ID_Ciz/PlanIdDrawingManager.KolonDusey.cs'))
$members += "`n" + (Extract 'private static double EtriyeAcilimKancaCm(')
$members += "`n" + (Extract 'private static double EtriyeAcilimKesimBoyCm(')
$source = $sectionSource
$code = @'
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
public struct Point2d { public double X,Y; public Point2d(double x,double y){X=x;Y=y;} }
public class Geometry { public bool IsEmpty => false; public string AsText()=>""; }
public class GeometryFactory { public Geometry CreatePolygon()=>new Geometry(); }
namespace NetTopologySuite {
 public class NtsGeometryServices {
  public static readonly NtsGeometryServices Instance=new NtsGeometryServices();
  public GeometryFactory CreateGeometryFactory()=>new GeometryFactory();
 }
}
public class FloorInfo { public int FloorNo; }
public class SlabInfo { public int SlabId; }
public static class NtsOverlaySafe { public static Geometry PrepareForOverlay(Geometry g)=>g; }
public class BeamInfo { public double WidthCm,HeightCm; }
public struct KirisAcilimMontajBilgi { public int EtriyeCap,Cap,Adet,DuzCap,DuzAdet,GovdeAdet,GovdeCap,SolUstAdet,SolUstCap,SagUstAdet,SagUstCap,SolAltAdet,SolAltCap,SagAltAdet,SagAltCap; }
public class BeamSectionTests {
 readonly Dictionary<BeamInfo,string> _kirisKesitAdetNotlari=new Dictionary<BeamInfo,string>();
 const double KirisAcilimMontajPaspayiCm=5.5, KirisAcilimDuzPaspayiCm=5.5;
 static void KirisAcilimTani(string s){}
 class Model { public List<SlabInfo> Slabs=new List<SlabInfo>(); }
 Model _model=new Model();
 GeometryFactory _ntsDrawFactory=null;
 readonly List<KirisKesitBilgi> _kirisKesitler=new List<KirisKesitBilgi>();
 readonly List<KirisKesitDonati> _kirisKesitDonatilari=new List<KirisKesitDonati>();
 readonly List<(SlabInfo Slab,Geometry Poly)> _kirisKesitDosemeler=new List<(SlabInfo,Geometry)>();
 int GetSlabFloorNo(int id)=>id/1000;
 Geometry SlabFootprintPoly(SlabInfo slab)=>_ntsDrawFactory.CreatePolygon();
'@ + $members + @'
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
 static void Near(double a,double b,string name){Check(Math.Abs(a-b)<1e-7,name);}
 public static void Run(){
  var dims=KirisKesitYanOlculeri(25,85,(0,-12),(-70,-85));
  Check(dims.Count==5,"Both slab sides have full-height detail chains plus right total");
  var total=dims.Last(); Near(total.X0,40,"Bottom slab total starts at slab tip"); Near(total.X1,25,"Top total starts at beam face");
  Near(total.Lane,60,"Right total is 20 final cm beyond detail");
  Near(dims.Where(d=>d.Lane==-25).Sum(d=>d.Y1-d.Y0),85,"Left detail covers entire beam height");
  Near(dims.Where(d=>d.Lane==50).Sum(d=>d.Y1-d.Y0),85,"Right detail covers entire beam height");
  dims=KirisKesitYanOlculeri(40,60,null,null);Check(dims.Count==1,"No slab: total only");Near(dims[0].Lane,50,"No right slab: total 20 final cm from face");
  dims=KirisKesitYanOlculeri(40,60,null,(-20,-35));Check(dims.Count==4,"Midheight slab has above/thickness/below detail and total");
  Near(dims.Last().X0,40,"Midheight slab bottom anchor on beam");Near(dims.Last().X1,40,"Midheight slab top anchor on beam");
  dims=KirisKesitYanOlculeri(40,60,null,(0,-20));Near(dims.Last().X1,55,"Top slab total anchor on slab tip");Near(dims.Last().X0,40,"Top slab bottom anchor on beam");
  Near(EtriyeAcilimKesimBoyCm(32,52,Math.Max(10,EtriyeAcilimKancaCm(8))),188,"40x60 phi8: dimension label and opening must include two 10 cm hooks");
  Near(EtriyeAcilimKesimBoyCm(32,52,Math.Max(10,EtriyeAcilimKancaCm(10))),188,"40x60 phi10: no length jump at minimum hook boundary");
  Near(EtriyeAcilimKesimBoyCm(32,52,Math.Max(10,EtriyeAcilimKancaCm(12))),192,"40x60 phi12: keep hooks larger than 10 cm");
  var fresh=new BeamSectionTests();
  Check(KirisKesitEtriyeCarpan(1)=="" && KirisKesitEtriyeCarpan(2)=="2x" && KirisKesitEtriyeCarpan(3)=="3x","Multiplier prefix preserves station count and piece count separately");
  foreach(bool top in new[]{true,false}){
   var main=new KirisKesitSira{Tur=top?"mon":"duz",Cap=20,Adet=3};
   var extra=new KirisKesitSira{Tur=top?"ilaU":"ilaA",Cap=20,Adet=6,Derinlik=top?8:52};
   var placed=KirisKesitCubukYerlesimi(40,60,new List<KirisKesitSira>{main,extra});
   Check(placed.Count==9 && placed.Count(c=>c.Sira==0 && ReferenceEquals(c.Donati,extra))==2,"Fill two fitting extras between three main bars; overflow only remaining extras");
   Check(placed.Any(c=>ReferenceEquals(c.Donati,main) && Math.Abs(c.Merkez.X-20)<1e-7),"Preserve middle main bar position");
   var one=new KirisKesitSira{Tur=extra.Tur,Cap=14,Adet=1,Derinlik=extra.Derinlik};
   main.Cap=12;
   var small=KirisKesitCubukYerlesimi(40,60,new List<KirisKesitSira>{main,one});
   Check(small.Count==4 && small.All(c=>c.Sira==0),"A fitting single extra must share the first plane");
  }
  var mon=new KirisKesitSira{Tur="mon",Cap=20,Adet=5,Derinlik=5.5};
  var ila=new KirisKesitSira{Tur="ilaU",Cap=20,Adet=3,Derinlik=8};
  var packed=KirisKesitCubukYerlesimi(40,60,new List<KirisKesitSira>{mon,ila});
  Check(packed.Count==8,"Preserve combined main/extra counts");
  Check(packed.Count(c=>c.Sira==0)==5 && packed.Count(c=>c.Sira==1)==3,"Keep five main bars in the first plane and three extras in the second plane");
  Near(packed.Where(c=>c.Sira==1).Min(c=>c.Merkez.X),5,"Second row begins at left stirrup edge");
  Near(packed.Where(c=>c.Sira==1).Max(c=>c.Merkez.X),35,"Second row fills right edge before middle");
  for(double width=25;width<=160;width+=5)
  foreach(int dia in new[]{12,20,32})
  foreach(string type in new[]{"mon","duz"}){
   int count=2*KirisKesitEtriyeKutulari(width).Count+12;
   var bar=new KirisKesitSira{Tur=type,Cap=dia,Adet=count};
   var p=KirisKesitCubukYerlesimi(width,100,new List<KirisKesitSira>{bar});
   Check(p.Count==count,"Packing never changes source quantity");
   foreach(var row in p.GroupBy(c=>c.Sira)){
    var ordered=row.OrderBy(c=>c.Merkez.X).ToList();
    for(int i=1;i<ordered.Count;i++)Check(ordered[i].Merkez.X-ordered[i-1].Merkez.X-dia/10.0>=4-1e-7,"Actual horizontal clear distance >=4 cm");
   }
   var rows=p.GroupBy(c=>c.Sira).OrderBy(g=>g.Key).ToList();
   for(int i=1;i<rows.Count;i++)Near(Math.Abs(rows[i].First().Merkez.Y-rows[i-1].First().Merkez.Y)-dia/10.0,3,"Vertical clear distance exactly 3 cm");
   foreach(var box in KirisKesitEtriyeKutulari(width)){
    Check(p.Any(c=>c.Sira==0 && Math.Abs(c.Merkez.X-box.Lo-1)<1e-7),"First row left stirrup corner supported");
    Check(p.Any(c=>c.Sira==0 && Math.Abs(c.Merkez.X-box.Hi+1)<1e-7),"First row right stirrup corner supported");
   }
  }

  var seven=new KirisKesitSira{Tur="mon",Cap=12,Adet=7};
  var p7=KirisKesitCubukYerlesimi(40,60,new List<KirisKesitSira>{seven});
  Check(p7.Count==7 && p7.Count(c=>c.Sira==0)==5 && p7.Count(c=>c.Sira==1)==2,"Seven bars: demote one more for symmetric second row");
  Near(p7.Where(c=>c.Sira==1).Min(c=>c.Merkez.X),5,"Symmetric second row left corner");
  Near(p7.Where(c=>c.Sira==1).Max(c=>c.Merkez.X),35,"Symmetric second row right corner");
  Near(KirisKesitIstasyon(30,330,true),180,"Cantilever cut at half net span");
  double rawCut=KirisKesitIstasyon(0,300);
  double shifted=KirisKesitIstasyonBindirmedenKac(0,300,rawCut,x=>x>190&&x<210?2:1);
  Check(shifted<rawCut-10 && shifted>0.15*300-1,"Section shifts left 10-15 cm out of montage lap");
  Near(KirisKesitIstasyonBindirmedenKac(0,300,rawCut,x=>1),rawCut,"No shift without overlapping montage");
  var none=new List<(double Lo,double Hi)>();
  Near(KirisKesitIstasyonSec(0,300,rawCut,none,none),rawCut,"No ilave/lap keeps Ln 2/3 cut");
  var ilz=new List<(double Lo,double Hi)>{(-30,80),(180,330)};
  double c1=KirisKesitIstasyonSec(0,300,rawCut,ilz,none);
  Check(!KirisKesitAraliktaMi(ilz,c1) && c1<=175+1e-6 && c1>=85-1e-6,"Cut leaves ilave zone with 5 cm clearance");
  Near(c1,175,"Cut nearest to Ln 2/3 outside ilave");
  var lap=new List<(double Lo,double Hi)>{(120,170)};
  double c2=KirisKesitIstasyonSec(0,300,rawCut,ilz,lap);
  Check(!KirisKesitAraliktaMi(ilz,c2) && !KirisKesitAraliktaMi(lap,c2),"Cut avoids both ilave and lap");
  Near(c2,175,"Free gap right of lap kept");
  var lap2=new List<(double Lo,double Hi)>{(80,180)};
  double c3=KirisKesitIstasyonSec(0,300,rawCut,ilz,lap2);
  Check(!KirisKesitAraliktaMi(ilz,c3),"When forced, a lap-only position beats an ilave position");
  var full=new List<(double Lo,double Hi)>{(-10,310)};
  double c4=KirisKesitIstasyonSec(0,300,rawCut,full,new List<(double Lo,double Hi)>{(150,250)});
  Check(!(c4>150 && c4<250),"Fully covered by ilave: least overlapping position (no lap)");
  Check(c4>=30-1e-6 && c4<=270+1e-6,"Search stays inside 10% end margins");
  var kol=new List<(double Lo,double Hi)>{(170,230)};
  double c5=KirisKesitIstasyonSec(0,300,rawCut,none,none,kol);
  Check(!KirisKesitAraliktaMi(kol,c5) && (c5<=165+1e-6 || c5>=235-1e-6),"Cut leaves column-on-beam zone with 5 cm clearance");
  double c6=KirisKesitIstasyonSec(0,300,rawCut,new List<(double Lo,double Hi)>{(30,170)},none,new List<(double Lo,double Hi)>{(170,270)});
  Check(!KirisKesitAraliktaMi(new List<(double Lo,double Hi)>{(170,270)},c6),"Column-on-beam outranks ilave when forced");
  Check(KirisKesitEtriyeKutulari(43).Count==1 && KirisKesitEtriyeKutulari(43.01).Count==2,"35 cm leg threshold is strict");
  var wide=KirisKesitEtriyeKutulari(80);
  Check(wide.Count==2,"80 cm beam needs two overlapping equal stirrups");
  Near(wide[0].Hi-wide[0].Lo,48,"80 cm stirrup piece width");
  Near(wide[1].Hi-wide[1].Lo,48,"Both stirrups must be equal");
  var beam=new BeamInfo{WidthCm=150};
  var bars=new Dictionary<BeamInfo,KirisAcilimMontajBilgi>{{beam,new KirisAcilimMontajBilgi{EtriyeCap=8,Cap=14,Adet=4,DuzCap=20,DuzAdet=5}}};
  fresh.KirisKesitMontajHazirla(bars);
  Check(bars[beam].Adet==6 && bars[beam].DuzAdet==6 && fresh._kirisKesitAdetNotlari[beam].Contains("4→6"),"Raise insufficient counts before drawing, retain note");
  fresh.KirisKesitMontajHazirla(bars);
  Check(bars[beam].Adet==6 && bars[beam].DuzAdet==6,"Adjustment must not compound counts");
  var caps=new List<int>{12,14,16,20,25};
  var g1=KirisGovdeTahvil(22,14,70,11,caps);
  Check(KirisGovdeAralik(70,g1.Adet,11)>=12-1e-6 || g1.Cap==25,"Web spacing reaches 12 cm or the largest project bar");
  Check(g1.Adet*g1.Cap*g1.Cap>=22*14*14 && g1.Adet%2==0,"Web area kept, symmetric on both faces");
  Check(g1.Cap>14,"Web bar switched to a larger project diameter");
  var g0=KirisGovdeTahvil(4,12,70,11,caps);
  Check(g0.Adet==4 && g0.Cap==12,"Web with >=12 cm spacing unchanged");
  var gMax=KirisGovdeTahvil(40,20,70,11,new List<int>{12,20});
  Check(gMax.Adet==40 && gMax.Cap==20,"No larger project bar: unchanged");
  var gAgain=KirisGovdeTahvil(g1.Adet,g1.Cap,70,11,caps);
  Check(gAgain==g1,"Web conversion idempotent");
  var gb=new BeamInfo{WidthCm=40,HeightCm=70};
  var gbars=new Dictionary<BeamInfo,KirisAcilimMontajBilgi>{{gb,new KirisAcilimMontajBilgi{EtriyeCap=10,Cap=12,Adet=5,DuzCap=12,DuzAdet=4,GovdeAdet=22,GovdeCap=14,SolUstAdet=2,SolUstCap=20}}};
  fresh.KirisKesitMontajHazirla(gbars);
  Check(gbars[gb].GovdeCap==20 && gbars[gb].GovdeAdet==12,"22ø14 h=70 → 12ø20 (project bars 12/14/20)");
  var dk=SurekliDagitmaKurali(12,15,8,22);
  Check(Math.Abs(dk.AsAna-7.54)<0.01 && Math.Abs(dk.AsGerek-1.508)<0.001,"ø12/15 main: 7.54 cm²/m, distribution need 1.51 cm²/m");
  Check(Math.Abs(dk.SAlan-33.33)<0.05 && Math.Abs(dk.SMax-30)<1e-9,"ø8 area spacing 33.3 → governed by 30 cm");
  Check(Math.Abs(dk.NetMin-2.9333)<0.001 && Math.Abs(dk.EksenMin-3.7333)<0.001,"Net clear spacing max(Ø,25mm,4Dmax/3), axis = net+Ø");
  var dk2=SurekliDagitmaKurali(20,10,8,22);
  Check(Math.Abs(dk2.SMax-8.0)<0.01,"Heavy main ø20/10 (31.4 cm²/m) → area governs: ø8 every 8 cm");
  for(double width=20;width<=400;width+=0.5){
   var boxes=KirisKesitEtriyeKutulari(width);
   var legs=boxes.SelectMany(v=>new[]{v.Lo,v.Hi}).OrderBy(v=>v).ToArray();
   for(int i=1;i<legs.Length;i++)Check(legs[i]-legs[i-1]<=35+1e-7,"No horizontal gap between legs exceeds 35 cm");
   foreach(var box in boxes)Near(box.Hi-box.Lo,boxes[0].Hi-boxes[0].Lo,"All pieces have identical width");
   foreach(string type in new[]{"mon","duz"}){
    var centers=KirisKesitCubukMerkezleri(width,60,2*boxes.Count+3,type,0);
    Check(centers.Count==2*boxes.Count+3,"Retain existing count above minimum");
    foreach(var box in boxes){
     Check(centers.Any(p=>Math.Abs(p.X-(box.Lo+1))<1e-7),"Every left inner radius has a bar");
     Check(centers.Any(p=>Math.Abs(p.X-(box.Hi-1))<1e-7),"Every right inner radius has a bar");
    }
   }
  }
  fresh._model.Slabs.Add(new SlabInfo{SlabId=1001});
  fresh._model.Slabs.Add(new SlabInfo{SlabId=2001});
  fresh.KirisAcilimKesitBaslat(new FloorInfo{FloorNo=1});
  Check(fresh._ntsDrawFactory!=null && fresh._kirisKesitDosemeler.Count==1,"Fresh beam-section command must initialize geometry factory before reading slab footprints");
  fresh._kirisKesitler.Add(new KirisKesitBilgi{Ad="Previous beam team"});
  fresh._kirisKesitDonatilari.Add(new KirisKesitDonati{Yol=new List<Point2d>(),Adet=3,Cap=16,Tur="mon"});
  fresh.KirisAcilimKesitTakimBaslat();
  Check(fresh._kirisKesitler.Count==0 && fresh._kirisKesitDonatilari.Count==0,"New beam team must not inherit previous team section groups or bar paths");
  Check(fresh._kirisKesitDosemeler.Count==1,"Team boundary preserves same-floor slab cache");
  double sectionX=100;
  foreach(double width in new[]{25.0,40.0,80.0,30.0,60.0,100.0,35.0}){
   double next=KirisKesitSonrakiX(sectionX,width);
   Near(2*((next-19)-(sectionX+width+38)),50,"Final gap between section envelopes is approximately 50 cm, including after third section");
   sectionX=next;
  }
  for(int count=1;count<=20;count++){
   double bottom=KirisKesitYerlesimAlti(-25,80,10,count);
   double actualBottom=-25-80-72-count*8-(80-8)-30;
   Check(actualBottom>bottom,"Pre-draw reservation contains stirrup opening in a single row");
  }
  var factory=fresh._ntsDrawFactory;
  fresh.KirisAcilimKesitBaslat(new FloorInfo{FloorNo=2});
  Check(ReferenceEquals(factory,fresh._ntsDrawFactory) && fresh._kirisKesitDosemeler.Count==1,"Repeated floor startup must reuse factory and clear slab cache");
  fresh._model.Slabs=null;
  fresh.KirisAcilimKesitBaslat(new FloorInfo{FloorNo=1});
  Check(fresh._kirisKesitDosemeler.Count==0,"No-slab model must leave an empty section slab cache");
  Near(KirisKesitIstasyon(30,330),230,"Cut must be at 2/3 of net face-to-face length");
  Near(KirisKesitIstasyon(-100,200),100,"Cut uses net start, not global origin");
  var slab=KirisKesitDosemeKotlari(-300,0,14,-300);
  Near(slab.Ust,0,"Slab datum relative to beam top");Near(slab.Alt,-14,"Actual 14 cm slab thickness");
  slab=KirisKesitDosemeKotlari(-300,-10,12,-305);
  Near(slab.Ust,-5,"Slab offset and beam drop must both affect section");Near(slab.Alt,-17,"Offset slab bottom");
  var path=new List<Point2d>{new Point2d(0,-30),new Point2d(0,-5.5),new Point2d(100,-5.5),new Point2d(200,-15.5)};
  Check(KirisKesitYolKes(path,150,out double y),"Sloped continuous bar must be cut");
  Near(y,-10.5,"Actual sloped bar depth at section");
  Check(!KirisKesitYolKes(path,220,out y),"An extra bar ending before section must not appear");
  Check(!KirisKesitYolKes(new List<Point2d>{new Point2d(10,0),new Point2d(10,30)},10,out y),"Vertical end hook must not count as a longitudinal bar");
  var topBars=KirisKesitCubukMerkezleri(30,60,3,"mon",8);
  Check(topBars.Count==3,"Top count");
  Near(topBars[0].X,5.0,"Left center cover");Near(topBars[2].X,25.0,"Right center cover");Near(topBars[1].X,15,"Middle bar");
  foreach(var p in topBars)Near(p.Y,-5.0,"Top center cover");
  foreach(var p in KirisKesitCubukMerkezleri(30,80,4,"duz",0))Near(p.Y,-75.0,"Bottom center cover");
  var side=KirisKesitCubukMerkezleri(30,80,4,"gov",0);
  Check(side.Count==4,"Web count");
  Near(side[0].X,5.0,"Web left center");Near(side[1].X,25.0,"Web right center");
  Check(side[0].Y>side[2].Y && side.All(p=>p.Y < -5.0 && p.Y > -75.0),"Web rows between top and bottom centers");
  foreach(var p in KirisKesitCubukMerkezleri(30,60,2,"ilaU",8))Near(p.Y,-7.5,"Extra row follows 0.5 cm cover adjustment");
  Check(KirisKesitCubukMerkezleri(10,60,4,"mon",0).Count==0,"Degenerate width");
  var a=new KirisKesitBilgi{Ad="K01",AdetNotu="",B=30,H=60,Kot=-300,Etriye=8,Sol=(0,-14),Sag=(0,-14),X=0,Y=0};
  a.Siralar.Add(new KirisKesitSira{Adet=3,Cap=16,Tur="mon",Derinlik=5.5});
  a.Siralar.Add(new KirisKesitSira{Adet=3,Cap=14,Tur="duz",Derinlik=54.5});
  var b=new KirisKesitBilgi{Ad="K99",B=30,H=60,Kot=-300,Etriye=8,Sol=(0,-14),Sag=(0,-14),X=100,Y=100};
  b.Siralar.Add(new KirisKesitSira{Adet=3,Cap=14,Tur="duz",Derinlik=54.5});
  b.Siralar.Add(new KirisKesitSira{Adet=1,Cap=16,Tur="mon",Derinlik=5.5});
  b.Siralar.Add(new KirisKesitSira{Adet=2,Cap=16,Tur="mon",Derinlik=5.5});
  string key=KirisKesitImza(a);
  Check(key==KirisKesitImza(b),"Similar sections merge across names/positions/row order and equivalent bar totals");
  b.Sag=null;Check(key!=KirisKesitImza(b),"Edge beam must not merge with two-sided slab");b.Sag=(0,-12);
  Check(key!=KirisKesitImza(b),"Different slab thickness");b.Sag=(0,-14);b.Etriye=10;
  Check(key!=KirisKesitImza(b),"Different stirrup diameter");b.Etriye=8;b.H=80;
  Check(key!=KirisKesitImza(b),"Different height");b.H=60;b.Kot=-320;
  Check(key!=KirisKesitImza(b),"Different elevation");b.Kot=-300;b.Siralar[0].Cap=12;
  Check(key!=KirisKesitImza(b),"Different longitudinal reinforcement");
  Check(KirisKesitHarf(0)=="A" && KirisKesitHarf(25)=="Z" && KirisKesitHarf(26)=="AA" && KirisKesitHarf(51)=="AZ","Unique section symbols beyond 26 groups");
 }
}
'@
# Windows PowerShell Add-Type eski C# derleyicisi ValueTuple/=> desteklemez; dotnet ile derle.
$work = Join-Path $env:TEMP ("Test-KirisKesit-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
    Set-Content -Path (Join-Path $work 'Program.cs') -Value ($code + "`nclass Program { static void Main(){ BeamSectionTests.Run(); System.Console.WriteLine(""PASS""); } }") -Encoding UTF8
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
'@ | Set-Content -Path (Join-Path $work 'Test-KirisKesit.csproj') -Encoding UTF8
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    & $dotnet run --project (Join-Path $work 'Test-KirisKesit.csproj') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Test-KirisKesit dotnet run exit $LASTEXITCODE" }
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
Write-Output 'PASS: fresh startup, independent beam-team groups and placement, Ln 2/3 cuts, bar intersections, 5 cm centers, slab/geometry/rebar/elevation grouping and section letters.'
