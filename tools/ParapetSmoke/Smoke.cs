using System;
using System.IO;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    // UI is outside this geometry smoke test; production drawer and settings are linked unchanged.
    internal static class ParapetForm
    {
        internal static ParapetSettings Current;
        internal static void Shutdown() { }
    }
    public class Smoke
    {
        static void Assert(bool ok,string message) { if(!ok) throw new System.Exception(message); }
        static void CheckCellPlacement(Transaction tr,Database db,BlockTableRecord space,DBText label,Extents3d cell)
        {
            foreach(string number in new[]{"1","123456789"})
            {
                var text=new DBText {TextString=number,Height=10,TextStyleId=label.TextStyleId,Position=new Point3d(-500,-500,0)};
                if(!space.IsWriteEnabled) space.UpgradeOpen();
                space.AppendEntity(text); tr.AddNewlyCreatedDBObject(text,true);
                var e=text.GeometricExtents;
                var balloon=new Polyline {Closed=true};
                balloon.AddVertexAt(0,new Point2d(e.MinPoint.X-2,e.MinPoint.Y-2),0,0,0);
                balloon.AddVertexAt(1,new Point2d(e.MaxPoint.X+2,e.MinPoint.Y-2),0,0,0);
                balloon.AddVertexAt(2,new Point2d(e.MaxPoint.X+2,e.MaxPoint.Y+2),0,0,0);
                balloon.AddVertexAt(3,new Point2d(e.MinPoint.X-2,e.MaxPoint.Y+2),0,0,0);
                space.AppendEntity(balloon); tr.AddNewlyCreatedDBObject(balloon,true);
                BaMetrajPozCell.Center(balloon,text,cell);
                foreach(Entity item in new Entity[]{balloon,text})
                {
                    var b=item.GeometricExtents;
                    Assert(Math.Abs(b.MinPoint.X+b.MaxPoint.X-cell.MinPoint.X-cell.MaxPoint.X)<1e-6,"poz balloon/text horizontal center");
                    Assert(Math.Abs(b.MinPoint.Y+b.MaxPoint.Y-cell.MinPoint.Y-cell.MaxPoint.Y)<1e-6,"poz balloon/text vertical center");
                    Assert(b.MinPoint.X>=cell.MinPoint.X && b.MaxPoint.X<=cell.MaxPoint.X && b.MinPoint.Y>=cell.MinPoint.Y && b.MaxPoint.Y<=cell.MaxPoint.Y,"poz must fit first-column cell");
                }
                balloon.Erase(); text.Erase();
            }
        }
        static void CheckLineTable(Transaction tr,Database db,BlockTableRecord space,ObjectId style)
        {
            if(!space.IsWriteEnabled) space.UpgradeOpen();
            var fixture=new List<Entity>();
            foreach(double y in new[]{5000d,5020d,5040d}) fixture.Add(new Line(new Point3d(5000,y,0),new Point3d(5200,y,0)));
            foreach(double x in new[]{5000d,5030d,5200d}) fixture.Add(new Line(new Point3d(x,5000,0),new Point3d(x,5040,0)));
            foreach(var line in fixture) {line.Layer="IC ANTET (BEYKENT)"; space.AppendEntity(line); tr.AddNewlyCreatedDBObject(line,true);}
            var label=new DBText {TextString="2x4ø10/20 L=555 (YATAY)",TextStyleId=style,Height=10,Position=new Point3d(5040,5006,0)};
            space.AppendEntity(label); tr.AddNewlyCreatedDBObject(label,true);
            Assert(BaMetrajPozCell.TryFind(label,BaMetrajPozCell.Collect(tr,space),out Extents3d cell),"temel hatili LINE-based cell recognition");
            Assert(Math.Abs(cell.MinPoint.X-5000)<1e-8 && Math.Abs(cell.MaxPoint.X-5030)<1e-8 && Math.Abs(cell.MinPoint.Y-5000)<1e-8 && Math.Abs(cell.MaxPoint.Y-5020)<1e-8,"temel hatili row bounds");
            CheckCellPlacement(tr,db,space,label,cell);
            label.Erase(); foreach(var line in fixture) line.Erase();
        }
        [CommandMethod("PARAPETSMOKE")]
        public void Run()
        {
            string root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Smoke).Assembly.Location),"../../.."));
            var log=new List<string>();
            try
            {
                foreach(ParapetKonumu k in Enum.GetValues(typeof(ParapetKonumu)))
                foreach(double skirt in new[]{0d,25d,75d})
                foreach(double slab in new[]{15d,20d})
                foreach(double width in new[]{20d,15d})
                foreach(double total in new[]{0d,2000d,3400d})
                {
                    var settings=new ParapetSettings { Konum=k, Sakal=skirt, DosemeKalinligi=slab, ParapetKalinligi=width, ToplamUzunluk=total };
                    if(k==ParapetKonumu.KirisUzeri && skirt>0)
                    {
                        Assert(settings.Validate()!=null,"beam skirt must be rejected");
                        settings.Normalize();
                        Assert(settings.Sakal==0,"beam skirt normalization failed");
                        continue;
                    }
                    Assert(settings.Validate()==null,"valid settings rejected");
                    using(var db=new Database(true,true))
                    {
                        var previous = HostApplicationServices.WorkingDatabase;
                        HostApplicationServices.WorkingDatabase = db;
                        try
                        {
                        using(var tr=db.TransactionManager.StartTransaction())
                        {
                            new ParapetRunner.Drawer(tr,db,Point3d.Origin).Draw(settings);
                            tr.Commit();
                        }
                        using(var tr=db.TransactionManager.StartTransaction())
                        {
                            var bt=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
                            var space=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
                            var cells=BaMetrajPozCell.Collect(tr,space);
                            var quantityLabels=new List<DBText>();
                            int count=0; bool horizontal=false; int dots=0,traces=0,kots=0,barCount=0,verticalLabels=0,extraBars=0;
                            bool edgeHook=false,beamOutside=false;
                            int closedStirrups=0,stirrupHooks=0;
                            int stockLabels=0,tableEntities=0;
                            var verticalDims=new List<RotatedDimension>();
                            DBText title=null,scale=null;
                            foreach(ObjectId id in space)
                            {
                                var e=(Entity)tr.GetObject(id,OpenMode.ForRead);
                                Assert(!(e is Solid),"unexpected solid found");
                                if(e is Hatch hatch) Assert(hatch.Layer=="KOT CIZGISI (BEYKENT)" && hatch.ColorIndex==250,"non-symbol fill found");
                                var bounds=e.GeometricExtents;
                                Assert(!double.IsNaN(bounds.MinPoint.X),"invalid extents");
                                if(e is RotatedDimension dimension && Math.Abs(dimension.Rotation-Math.PI/2)<1e-8) verticalDims.Add(dimension);
                                if(e is DBText t)
                                {
                                    if(t.TextString=="2x"+settings.YatayKesitKotlari().Count+"ø10/15 (YATAY)") horizontal=true;
                                    if(t.TextString.Contains("(YATAY)") && t.TextString.Contains("L="))
                                    {
                                        quantityLabels.Add(t);
                                        var part=settings.YatayParcalar()[stockLabels++];
                                        Assert(t.TextString=="2x"+settings.YatayKesitKotlari().Count+"x"+part.Adet+"ø10 L="+part.Boy+" (YATAY)","stock quantity label mismatch");
                                    }
                                    if(t.TextString.Contains("(DIKEY)"))
                                    {
                                        Assert(!BaMetrajPozCell.TryFind(t,cells,out _),"non-table labels must keep normal poz placement");
                                        string prefix=total==0?"":""+(int)Math.Ceiling(total/15);
                                        Assert(t.TextString==prefix+"ø12/15 L="+settings.DonatiBoyuYuz(verticalLabels==0)+" (DIKEY)","incorrect length/count");
                                        Assert(Math.Abs(t.Rotation-Math.PI/2)<1e-9,"label must be vertical");
                                        verticalLabels++;
                                    }
                                    if(t.Layer=="KOT YAZI (BEYKENT)") { Assert(t.Height==10,"kot text size"); kots++; }
                                    if(t.TextString.Contains("PARAPET DETAYI")) title=t;
                                    if(t.TextString=="ÖLÇEK: 1/25") scale=t;
                                }
                                if(e is Polyline pl && pl.NumberOfVertices==2 && pl.Closed)
                                {
                                    Assert(pl.Closed && pl.ConstantWidth==6,"section dot must use standard polyline");
                                    Assert(Math.Abs(pl.GetBulgeAt(0)-1)<1e-9 && Math.Abs(pl.GetBulgeAt(1)-1)<1e-9,"dot arcs");
                                    if(pl.Layer=="DONATI (BEYKENT)")
                                    {
                                        dots++;
                                        double cx=(pl.GetPoint2dAt(0).X+pl.GetPoint2dAt(1).X)/2;
                                        double cy=pl.GetPoint2dAt(0).Y;
                                        Assert(Math.Abs(cx-8)<1e-8 || Math.Abs(cx-(2*width-8))<1e-8,"section dot must be 1.5cm inside stem");
                                        if(cy>0) Assert(new List<double>{17,42,67,92}.Exists(y=>Math.Abs(y-cy)<1e-8),"section rows differ from foundation standard");
                                    }
                                    else Assert(pl.Layer=="DONATI IZI (BEYKENT)","wrong dot layer");
                                }
                                if(e is Line && e.Layer=="DONATI IZI (BEYKENT)") traces++;
                                if(e.Layer=="IC ANTET (BEYKENT)") tableEntities++;
                                if(e is Polyline hook && hook.NumberOfVertices==6 && hook.Layer=="DONATI IZI (BEYKENT)")
                                {
                                    edgeHook=true; traces+=2;
                                    Assert(Math.Abs(hook.GeometricExtents.MaxPoint.X-2*(width-settings.Paspayi))<1e-8,"slab edge hook cover");
                                    Assert(Math.Abs(hook.GetBulgeAt(1)-0.4142135623730951)<1e-8 && Math.Abs(hook.GetBulgeAt(3)-0.4142135623730951)<1e-8,"slab hook bends");
                                }
                                if(e.Layer=="KIRIS (BEYKENT)")
                                {
                                    Assert(e.GeometricExtents.MaxPoint.X<=2*width+1e-8,"beam outside alignment");
                                    if(Math.Abs(e.GeometricExtents.MaxPoint.X-2*width)<1e-8) beamOutside=true;
                                }
                                if(e is Polyline stirrup && e.Layer=="ETRIYE (BEYKENT)")
                                {
                                    if(stirrup.Closed)
                                    {
                                        closedStirrups++;
                                        Assert(stirrup.NumberOfVertices==8,"stirrup corners missing");
                                        Assert(Math.Abs(bounds.MinPoint.X-2*(width-settings.KirisGenisligi+settings.Paspayi))<1e-8,"stirrup left cover");
                                        Assert(Math.Abs(bounds.MaxPoint.X-2*(width-settings.Paspayi))<1e-8,"stirrup right cover");
                                        Assert(Math.Abs(bounds.MinPoint.Y-2*(-settings.KirisYuksekligi+settings.Paspayi))<1e-8,"stirrup bottom cover");
                                        Assert(Math.Abs(bounds.MaxPoint.Y+2*settings.Paspayi)<1e-8,"stirrup must reach beam top");
                                    }
                                    else { stirrupHooks++; Assert(stirrup.NumberOfVertices==2,"stirrup hook segment"); }
                                }
                                if(e is Polyline bar && (bar.NumberOfVertices==6 || bar.NumberOfVertices==8) && bar.Layer=="DONATI (BEYKENT)")
                                {
                                    barCount++;
                                    if(bar.NumberOfVertices==8)
                                    {
                                        extraBars++;
                                        Assert(bar.GetPoint2dAt(7).Y>bar.GetPoint2dAt(6).Y,"extra hook must turn upward");
                                        bool inner=barCount%2==1;
                                        double capacity=inner || k!=ParapetKonumu.KirisUzeri ? width-2*settings.Paspayi : settings.KirisGenisligi-2*settings.Paspayi;
                                        Assert(Math.Abs(Math.Abs(bar.GetPoint2dAt(6).X-bar.GetPoint2dAt(2).X)-2*capacity)<1e-8,"second bend must be at full width minus twice cover");
                                    }
                                }
                                count++;
                            }
                            Assert(horizontal,"horizontal count mismatch");
                            foreach(var label in quantityLabels)
                            {
                                Assert(BaMetrajPozCell.TryFind(label,cells,out Extents3d cell),"parapet quantity cell must be recognized");
                                Assert(Math.Abs(cell.MaxPoint.X-cell.MinPoint.X-30)<1e-8 && Math.Abs(cell.MaxPoint.Y-cell.MinPoint.Y-20)<1e-8,"first-column cell dimensions");
                                CheckCellPlacement(tr,db,space,label,cell);
                            }
                            if(total>0) CheckLineTable(tr,db,space,quantityLabels[0].TextStyleId);
                            int segments=settings.AltDerinlik>slab+1e-6?3:2;
                            Assert(verticalDims.Count==segments+1,"continuous chain plus one full-height dimension expected");
                            double chainX=2*(width+20), totalX=2*(width+30), chainSpan=0;
                            int totalCount=0;
                            var ranges=new List<(double Low,double High)>();
                            foreach(var dimension in verticalDims)
                            {
                                double low=Math.Min(dimension.XLine1Point.Y,dimension.XLine2Point.Y);
                                double high=Math.Max(dimension.XLine1Point.Y,dimension.XLine2Point.Y);
                                if(Math.Abs(dimension.DimLinePoint.X-totalX)<1e-8)
                                {
                                    totalCount++;
                                    Assert(Math.Abs(low+2*settings.AltDerinlik)<1e-8 && Math.Abs(high-2*settings.ParapetYuksekligi)<1e-8,"overall dimension must include full support depth");
                                }
                                else
                                {
                                    Assert(Math.Abs(dimension.DimLinePoint.X-chainX)<1e-8,"all chain dimensions must align");
                                    chainSpan+=high-low; ranges.Add((low,high));
                                }
                            }
                            ranges.Sort((a,b)=>a.Low.CompareTo(b.Low));
                            for(int i=1;i<ranges.Count;i++) Assert(Math.Abs(ranges[i].Low-ranges[i-1].High)<1e-8,"dimension chain must have no gaps");
                            Assert(totalCount==1 && Math.Abs(chainSpan-2*(settings.ParapetYuksekligi+settings.AltDerinlik))<1e-8,"chain must cover entire section");
                            Assert(dots==2*settings.YatayKesitKotlari().Count,"skirt rows must be included in label count");
                            Assert(stockLabels==settings.YatayParcalar().Count,"stock labels missing");
                            Assert((tableEntities>0)==(total>0),"table must be absent for zero length");
                            Assert(dots>=8 && traces>=2,"missing section/slab reinforcement");
                            Assert(barCount==4 && verticalLabels==2,"section and development bar count");
                            Assert(extraBars==2*((settings.EkGonye(true)>0?1:0)+(settings.EkGonye(false)>0?1:0)),"extra hook count");
                            Assert(edgeHook==(k!=ParapetKonumu.DosemeOrtasi),"edge hook missing");
                            if(k==ParapetKonumu.DosemeOrtasi) Assert(extraBars==0 && settings.EkGonye(true)==0 && settings.EkGonye(false)==0,"middle slab must allow straight lower hooks");
                            Assert(closedStirrups==(k==ParapetKonumu.KirisUzeri?1:0),"closed beam stirrup missing");
                            Assert(stirrupHooks==(k==ParapetKonumu.KirisUzeri?2:0),"stirrup closure hooks missing");
                            if(k==ParapetKonumu.KirisUzeri) Assert(beamOutside && settings.KirisSag==width,"beam outside face mismatch");
                            Assert(kots==3,"standard levels missing");
                            Assert(title!=null && scale!=null,"bottom captions missing");
                            Assert(title.Layer=="KESIT ISMI (BEYKENT)","title layer");
                            Assert(title.GeometricExtents.MaxPoint.Y < -2*settings.AltDerinlik,"title above detail bottom");
                            Assert(scale.GeometricExtents.MaxPoint.Y < title.GeometricExtents.MinPoint.Y,"scale above title");
                            Assert(settings.AltGonye>=14.4-1e-8 && settings.AltGonye<20,"excessive bottom hook");
                            log.Add(k+" sakal="+skirt+" doseme="+slab+" w="+width+" total="+total+" PASS entities="+count);
                        }
                        db.DxfOut(Path.Combine(root,k+"-"+skirt+(slab==15?"":"-d20")+(width==20?"":"-w15")+(total==0?"":"-L"+total)+".dxf"),16,DwgVersion.Current);
                        }
                        finally { HostApplicationServices.WorkingDatabase = previous; }
                    }
                }
                Assert(new ParapetSettings { KirisGenisligi=10,Konum=ParapetKonumu.KirisUzeri }.Validate()!=null,"narrow beam allowed");
                Assert(new ParapetSettings { Paspayi=10 }.Validate()!=null,"cover collision allowed");
                Assert(new ParapetSettings { DikeyAralik=0 }.Validate()!=null,"zero spacing allowed");
                Assert(new ParapetSettings { DosemeKalinligi=double.NaN }.Validate()!=null,"NaN allowed");
                Assert(new ParapetSettings { DosemeKalinligi=20 }.DonatiBoyu==95,"20cm slab should give L95");
                Assert(new ParapetSettings().DonatiBoyu==90,"15cm slab should give L90");
                foreach(double diameter in new[]{8d,10d,12d,16d,20d})
                foreach(double height in new[]{50d,51d,94d,94.5d})
                foreach(double cover in new[]{2.5d,3d})
                foreach(ParapetKonumu position in Enum.GetValues(typeof(ParapetKonumu)))
                foreach(bool inner in new[]{true,false})
                {
                    var fit=new ParapetSettings {DikeyCap=diameter,ParapetYuksekligi=height,Paspayi=cover,ParapetKalinligi=15,Konum=position};
                    double horizontal=fit.AltGonyeYatay(inner), tail=fit.EkGonye(inner);
                    double minimum=12*diameter/10;
                    double body=fit.GovdeBoyu+fit.UstGonye+horizontal;
                    double expected=Math.Ceiling((body+(tail>0?minimum:0))/5-1e-9)*5;
                    if(tail>0) Assert(tail>=minimum-1e-8,"second hook itself must be at least 12 diameters");
                    else Assert(horizontal>=minimum-1e-8,"single hook must be at least 12 diameters");
                    Assert(Math.Abs(fit.DonatiBoyuYuz(inner)/5-Math.Round(fit.DonatiBoyuYuz(inner)/5))<1e-8,"total bar length must be multiple of 5cm");
                    Assert(Math.Abs(fit.DonatiBoyuYuz(inner)-expected)<1e-8,"rounding must use smallest sufficient 5cm multiple");
                }
                var narrow=new ParapetSettings {ParapetKalinligi=15};
                Assert(narrow.AltGonyeYatay(true)==10 && narrow.EkGonye(true)==15 && narrow.DonatiBoyuYuz(true)==95,"narrow section: 10cm horizontal plus 15cm second hook, L95");
                log.Add("240 single/double hook minimum and rounding combinations PASS");
                var skirtRows=new ParapetSettings {ParapetYuksekligi=94,Sakal=30,YatayAralik=20}.YatayKesitKotlari();
                Assert(skirtRows.Count==7,"94/30 example should contain five upper plus two skirt rows");
                Assert(Math.Abs(skirtRows[6]-skirtRows[5]-18.8)<1e-8,"skirt should retain upper 18.8cm step");
                var stock=new ParapetSettings {ToplamUzunluk=3400}.YatayParcalar();
                Assert(stock.Count==2 && stock[0].Adet==2 && stock[0].Boy==1200 && stock[1].Adet==1 && stock[1].Boy==1120,"3400cm stock split");
                var stock2=new ParapetSettings {ToplamUzunluk=2000}.YatayParcalar();
                Assert(stock2.Count==2 && stock2[0].Adet==1 && stock2[1].Boy==860,"2000cm stock split");
                foreach(double length in new[]{1d,1199d,1200d,1201d,2340d,2341d,10000d})
                {
                    int n=0; double cut=0;
                    foreach(var part in new ParapetSettings {ToplamUzunluk=length}.YatayParcalar())
                    { Assert(part.Boy<=1200 && part.Boy>0,"stock bounds"); n+=part.Adet; cut+=part.Boy*part.Adet; }
                    double net=cut-60*(n-1);
                    Assert(net>=length-1e-8 && net<length+5+1e-8,"stock net length must cover run with less than 5cm rounding");
                }
                log.Add("Invalid geometry validation PASS");
                log.Add("ALL PASS");
            }
            catch(System.Exception ex) { log.Add("FAIL "+ex); }
            File.WriteAllLines(Path.Combine(root,"result.txt"),log);
            AcApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n"+string.Join("\n",log));
        }
    }
}
