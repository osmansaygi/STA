using System;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Colors;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace ST4Yardimci
{
    internal static class ParapetRunner
    {
        public static void RunManual()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { ParapetForm.Shutdown(); return; }
            try
            {
                var s = ParapetForm.Current ?? ParapetSettings.Load();
                string error = s.Validate();
                if (error != null) { doc.Editor.WriteMessage("\nPARAPET: " + error); return; }
                var pick = doc.Editor.GetPoint("\nParapet sol yüzü / döşeme üstü kesişimi: ");
                if (pick.Status != PromptStatus.OK) return;
                Point3d origin = pick.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    new Drawer(tr, doc.Database, origin).Draw(s);
                    tr.Commit();
                }
                doc.Editor.Regen();
                doc.Editor.WriteMessage("\nPARAPET: 1/25 detay çizildi.");
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\nPARAPET hata: " + ex.Message); }
            finally { ParapetForm.Current = null; ParapetForm.Shutdown(); }
        }

        /// <summary>Temel hatılı kesit/açılım standardı: gerçek cm × 2. Yalnız kot simgesi dolguludur.</summary>
        internal sealed class Drawer
        {
            readonly Transaction _tr;
            readonly Database _db;
            readonly BlockTableRecord _space;
            readonly Point3d _origin;
            readonly ObjectId _style, _dim;
            const string Rebar = "DONATI (BEYKENT)", RebarText = "DONATI YAZISI (BEYKENT)";
            const string Outline = "PARAPET (BEYKENT)", Slab = "DOSEME (BEYKENT)", Beam = "KIRIS (BEYKENT)";
            const string Marks = "DONATI IZI (BEYKENT)", Measure = "OLCU (BEYKENT)";
            const string Kot = "KOT CIZGISI (BEYKENT)", KotText = "KOT YAZI (BEYKENT)";
            const string Title = "KESIT ISMI (BEYKENT)", Leader = "ETIKET CIZGISI (BEYKENT)";
            const string Stirrup = "ETRIYE (BEYKENT)";
            const string Table = "IC ANTET (BEYKENT)", TableTitle = "ANTET BASLIK 1 (BEYKENT)";

            public Drawer(Transaction tr, Database db, Point3d origin)
            {
                _tr = tr; _db = db; _origin = origin;
                _space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (string layer in new[] { Rebar, RebarText, Outline, Slab, Beam, Marks, Measure, Kot, KotText,
                    "KESIT SINIRI (BEYKENT)", "YAZI (BEYKENT)", Title, Leader, Stirrup, Table, TableTitle })
                    BeykentKatmanYukle.Ensure(tr, db, layer);
                _style = BaDimensionStyles.EnsureYazi(tr, db);
                _dim = BaDimensionStyles.Ensure(tr, db, "PLAN_OLCU_DONATI 25");
            }

            Point3d P(double x, double y) => new Point3d(_origin.X + 2*x, _origin.Y + 2*y, _origin.Z);
            void Add(Entity e, string layer)
            {
                e.Layer = layer;
                e.ColorIndex = 256;
                e.LineWeight = LineWeight.ByLayer;
                _space.AppendEntity(e); _tr.AddNewlyCreatedDBObject(e, true);
            }
            void Line(string l, double x, double y, double xx, double yy) => Add(new Line(P(x,y),P(xx,yy)),l);
            void Path(string l, params double[] coords)
            {
                var p = new Polyline { Elevation = _origin.Z };
                for (int i=0;i<coords.Length;i+=2) { var q=P(coords[i],coords[i+1]); p.AddVertexAt(i/2,new Point2d(q.X,q.Y),0,0,0); }
                Add(p,l);
            }
            DBText Text(string l, string text, double x, double y, double h=5,
                double rotation=0, TextHorizontalMode horizontal=TextHorizontalMode.TextLeft,
                TextVerticalMode vertical=TextVerticalMode.TextBase)
            {
                var t=new DBText { Position=P(x,y), TextString=text, Height=2*h, TextStyleId=_style,
                    Rotation=rotation, HorizontalMode=horizontal, VerticalMode=vertical };
                if(horizontal!=TextHorizontalMode.TextLeft || vertical!=TextVerticalMode.TextBase) t.AlignmentPoint=P(x,y);
                Add(t,l);
                t.AdjustAlignment(_db);
                return t;
            }
            void Snap(DBText t, double? centerX=null, double? centerY=null, double? right=null,
                double? bottom=null, double? top=null)
            {
                var e=t.GeometricExtents;
                double dx=centerX.HasValue ? P(centerX.Value,0).X-(e.MinPoint.X+e.MaxPoint.X)/2
                    : right.HasValue ? P(right.Value,0).X-e.MaxPoint.X : 0;
                double dy=centerY.HasValue ? P(0,centerY.Value).Y-(e.MinPoint.Y+e.MaxPoint.Y)/2
                    : bottom.HasValue ? P(0,bottom.Value).Y-e.MinPoint.Y
                    : top.HasValue ? P(0,top.Value).Y-e.MaxPoint.Y : 0;
                t.TransformBy(Matrix3d.Displacement(new Vector3d(dx,dy,0)));
            }
            void Dim(double x,double y,double xx,double yy,double dx,double dy, bool vertical)
            {
                var dim = new RotatedDimension(vertical ? Math.PI/2 : 0, P(x,y),P(xx,yy),P(dx,dy),"",_dim);
                BaDimensionStyles.Apply(_tr,_db,dim,_dim,"PLAN_OLCU_DONATI 25");
                Add(dim,Measure);
            }
            void Level(double x,double y,double kot)
            {
                // Temel hatılı klasik kotu: ölçüler çizim birimi olarak 12/12/20/26.
                Line(Kot,x-10,y,x,y);
                var triangle=new Polyline { Elevation=_origin.Z, Closed=true };
                var a=P(x,y); var b=P(x-6,y+6); var c=P(x,y+6);
                triangle.AddVertexAt(0,new Point2d(a.X,a.Y),0,0,0);
                triangle.AddVertexAt(1,new Point2d(b.X,b.Y),0,0,0);
                triangle.AddVertexAt(2,new Point2d(c.X,c.Y),0,0,0);
                Add(triangle,Kot);
                var fill=new Hatch();
                Add(fill,Kot);
                fill.ColorIndex=250;
                fill.SetHatchPattern(HatchPatternType.PreDefined,"SOLID");
                fill.Associative=false;
                fill.AppendLoop(HatchLoopTypes.Outermost,new ObjectIdCollection { triangle.ObjectId });
                fill.EvaluateHatch(true);
                Line(Kot,x-6,y+6,x+6,y+6); Line(Kot,x,y,x+6,y+6);
                Line(Kot,x+6,y+6,x,y+6); Line(Kot,x+6,y+6,x+19,y+6);
                double m=Math.Round(kot/100,2,MidpointRounding.AwayFromZero);
                string v=Math.Abs(m)<0.005 ? "±0.00" : m.ToString("+0.00;-0.00",CultureInfo.InvariantCulture);
                var t=Text(KotText,v,x+6,y+6);
                Snap(t,bottom:y+6+2.85);
            }
            void Cut(double x,double y,double d) => Line("KESIT SINIRI (BEYKENT)",x,y+5,x,y-d-5);
            static string N(double n) => n.ToString("0.#",CultureInfo.InvariantCulture);

            public void Draw(ParapetSettings s)
            {
                double w=s.ParapetKalinligi,h=s.ParapetYuksekligi,d=s.DosemeKalinligi;
                double skirt=s.Sakal>0 ? -d-s.Sakal : 0;
                bool beam=s.Konum==ParapetKonumu.KirisUzeri, edge=s.Konum!=ParapetKonumu.DosemeOrtasi;
                double bl=beam?s.KirisSol:0, br=w;
                double left=-80;
                double right=edge?w:Math.Max(w+100,br+60);
                if (beam) left=Math.Min(left,bl-60);

                // Birleşim içinde fazladan beton sınırı çizilmez.
                Line(Slab,left,0,0,0);
                if (!edge) Line(Slab,beam?br:w,0,right,0);
                Line(Outline,0,0,0,h); Line(Outline,0,h,w,h);
                double outerBottom = edge && !beam ? -d-s.Sakal : 0;
                Line(Outline,w,h,w,outerBottom);
                if (s.Sakal>0)
                {
                    double visibleStart=beam?-s.KirisYuksekligi:-d;
                    if (skirt<visibleStart)
                    {
                        Line(Outline,0,visibleStart,0,skirt); Line(Outline,0,skirt,w,skirt);
                        if (beam || !edge) Line(Outline,w,visibleStart,w,skirt);
                    }
                    else if(edge) Line(Outline,0,skirt,w,skirt);
                }
                if (beam)
                {
                    Line(Slab,left,-d,bl,-d);
                    Path(Beam,bl,-d,bl,-s.KirisYuksekligi);
                    Path(Beam,br,0,br,-s.KirisYuksekligi);
                    if (skirt < -s.KirisYuksekligi)
                    { Line(Beam,bl,-s.KirisYuksekligi,0,-s.KirisYuksekligi); Line(Beam,w,-s.KirisYuksekligi,br,-s.KirisYuksekligi); }
                    else Line(Beam,bl,-s.KirisYuksekligi,br,-s.KirisYuksekligi);
                    Dim(bl,-s.KirisYuksekligi,br,-s.KirisYuksekligi,0,-s.KirisYuksekligi-15,false);
                }
                else
                {
                    Line(Slab,left,-d,s.Sakal>0?0:w,-d);
                    if (!edge) Line(Slab,w,-d,right,-d);
                }
                Cut(left,0,d); if(!edge) Cut(right,0,d);
                double dimX=w+20, totalDimX=dimX+10;
                double kotX=Math.Max(right+40,totalDimX+20);
                Level(kotX,0,s.DosemeUstKotu); Level(kotX,h,s.DosemeUstKotu+h);
                Level(kotX,-s.AltDerinlik,s.DosemeUstKotu-s.AltDerinlik);
                Dim(0,h,w,h,0,h+12,false);
                // Tek hat üzerinde boşluksuz düşey zincir; dış hatta tüm kesit yüksekliği.
                Dim(w,0,w,h,dimX,0,true);
                Dim(w,-d,w,0,dimX,0,true);
                if(s.AltDerinlik>d+1e-6) Dim(w,-s.AltDerinlik,w,-d,dimX,0,true);
                Dim(w,-s.AltDerinlik,w,h,totalDimX,0,true);

                // Temel hatılı: iki yüzün alt ve üst gönyeleri birbirine bakar.
                double p=s.Paspayi;
                double top=h-p, bottom=-s.AltDerinlik+p, hook=s.UstGonye, leg=s.AltGonye;
                Bar(p,bottom,top,hook,s.AltGonyeYatay(true),1,s.EkGonye(true));
                Bar(w-p,bottom,top,hook,s.AltGonyeYatay(false),-1,s.EkGonye(false));
                // r=1.5 cm: kesit merkezleri düşey gövdenin iç tarafında, gönyenin altında.
                double xCL=p+1.5, xCR=w-p-1.5;
                var rows=s.YatayKesitKotlari();
                double yHigh=rows[0], yLow=rows[0];
                int count=rows.Count;
                foreach(double y in rows)
                {
                    yLow=Math.Min(yLow,y);
                    Dot(xCL,y,Rebar); Dot(xCR,y,Rebar);
                }
                Path(Leader,xCL,yHigh,xCL-10,yHigh,xCL-10,yLow,xCL,yLow);
                var horizontal=Text(RebarText,"2x"+count+"ø"+N(s.YatayCap)+"/"+N(s.YatayAralik)+" (YATAY)",
                    xCL-12,(yLow+yHigh)/2,5,Math.PI/2,TextHorizontalMode.TextCenter,TextVerticalMode.TextVerticalMid);
                Snap(horizontal,right:xCL-12,centerY:(yLow+yHigh)/2);

                // Temel içi izler: iki yatay ByLayer çizgi, 20 cm adımlı ø1.5/width6 kesitler.
                SlabTraces(left,right,-d+p,-p,xCL,xCR,edge ? w-p : (double?)null);
                if(beam) BeamStirrup(bl+p,br-p,-s.KirisYuksekligi+p,-p);
                // Aynı kotta, aralarında en yakın noktalar arasında 5 cm bırakılmış iki açılım.
                double span=Math.Max(hook,leg), ax=kotX+48;
                double ax2=ax+2*span+5;
                Development(s,ax,top,bottom,hook,s.AltGonyeYatay(true),1,s.EkGonye(true));
                Development(s,ax2,top,bottom,hook,s.AltGonyeYatay(false),-1,s.EkGonye(false));
                if(s.ToplamUzunluk>0) QuantityTable(s,count,ax2+27,h);
                double center=(left+ax2+12)/2;
                double titleTop=-s.AltDerinlik-30;
                var title=Text(Title,"("+N(w)+"/"+N(h)+") PARAPET DETAYI",center,titleTop,10,
                    0,TextHorizontalMode.TextCenter,TextVerticalMode.TextTop);
                Snap(title,centerX:center,top:titleTop);
                var scale=Text(Title,"ÖLÇEK: 1/25",center,titleTop-18,5,
                    0,TextHorizontalMode.TextCenter,TextVerticalMode.TextTop);
                Snap(scale,centerX:center,top:titleTop-18);
            }

            void Dot(double x,double y,string layer)
            {
                var poly=new Polyline { Elevation=_origin.Z, Closed=true };
                var a=P(x-0.75,y); var b=P(x+0.75,y);
                poly.AddVertexAt(0,new Point2d(a.X,a.Y),1,0,0);
                poly.AddVertexAt(1,new Point2d(b.X,b.Y),1,0,0);
                poly.ConstantWidth=6;
                Add(poly,layer);
            }

            void QuantityTable(ParapetSettings s,int rows,double x,double top)
            {
                var pieces=s.YatayParcalar();
                double width=30, divider=x+15;
                for(int i=0;i<pieces.Count;i++)
                {
                    var part=pieces[i];
                    string label="2x"+rows+"x"+part.Adet+"ø"+N(s.YatayCap)+" L="+N(part.Boy)+" (YATAY)";
                    var text=Text(RebarText,label,divider+5,top-(i+0.5)*10,5,0,
                        TextHorizontalMode.TextLeft,TextVerticalMode.TextVerticalMid);
                    Snap(text,centerY:top-(i+0.5)*10);
                    width=Math.Max(width,(text.GeometricExtents.MaxPoint.X-P(x,0).X)/2+5);
                }
                double bottom=top-10*pieces.Count;
                Path(Table,x,bottom,x+width,bottom,x+width,top,x,top,x,bottom);
                Line(Table,divider,bottom,divider,top);
                for(int i=1;i<pieces.Count;i++) Line(Table,x,top-i*10,x+width,top-i*10);
                var title=Text(TableTitle,"YATAY DONATI (L="+N(s.ToplamUzunluk)+" cm)",x+width/2,top+2.5,6,0,
                    TextHorizontalMode.TextCenter,TextVerticalMode.TextBottom);
                Snap(title,centerX:x+width/2,bottom:top+2.5);
            }

            void BeamStirrup(double left,double right,double bottom,double top)
            {
                // Kirişin tüm yüksekliğini saran etriye; üst kenarı döşeme içinde.
                double r=Math.Min(1.5,Math.Min((right-left)/4,(top-bottom)/4));
                const double b=0.4142135623730951;
                double[,] v={{left+r,bottom,0},{right-r,bottom,b},{right,bottom+r,0},{right,top-r,b},
                    {right-r,top,0},{left+r,top,b},{left,top-r,0},{left,bottom+r,b}};
                var stirrup=new Polyline { Elevation=_origin.Z, Closed=true };
                for(int i=0;i<8;i++) { var q=P(v[i,0],v[i,1]); stirrup.AddVertexAt(i,new Point2d(q.X,q.Y),v[i,2],0,0); }
                Add(stirrup,Stirrup);
                double hook=Math.Min(5,(right-left)/3);
                Path(Stirrup,left,top-r,left+hook,top-r-hook);
                Path(Stirrup,left+r,top,left+r+hook,top-hook);
                foreach(double x in new[]{left+1.5,right-1.5})
                foreach(double y in new[]{bottom+1.5,top-1.5}) Dot(x,y,Marks);
            }

            void SlabTraces(double left,double right,double low,double high,double xCL,double xCR,double? hookX=null)
            {
                if(right-left<1 || high-low<1) return;
                if(hookX.HasValue)
                {
                    double x=hookX.Value, r=Math.Min(1.5,(high-low)/3);
                    var bar=new Polyline { Elevation=_origin.Z };
                    double[,] vertices={{left,low,0},{x-r,low,0.4142135623730951},
                        {x,low+r,0},{x,high-r,0.4142135623730951},{x-r,high,0},{left,high,0}};
                    for(int i=0;i<6;i++) { var q=P(vertices[i,0],vertices[i,1]); bar.AddVertexAt(i,new Point2d(q.X,q.Y),vertices[i,2],0,0); }
                    Add(bar,Marks);
                }
                foreach(double y in new[]{low,high})
                {
                    if(!hookX.HasValue) Line(Marks,left,y,right,y);
                    double dotY=y==low ? y+1.5 : y-1.5;
                    for(double x=xCL;x>=left-0.025;x-=20) if(x<=right+0.025) Dot(x,dotY,Marks);
                    for(double x=xCR;x<=right+0.025;x+=20) if(x>=left-0.025) Dot(x,dotY,Marks);
                }
            }

            void Bar(double x,double bottom,double top,double hook,double leg,int direction,double tail=0)
            {
                double r=Math.Min(1.5,Math.Abs(hook)/3);
                var poly=new Polyline { Elevation=_origin.Z };
                double bulge=direction*0.4142135623730951;
                double[,] v={{x+direction*hook,top,0},{x+direction*r,top,bulge},{x,top-r,0},
                    {x,bottom+r,bulge},{x+direction*r,bottom,0},{x+direction*leg,bottom,0}};
                if(tail>0)
                {
                    v[5,0]=x+direction*(leg-r);
                    v[5,2]=bulge;
                }
                for(int i=0;i<6;i++) { var q=P(v[i,0],v[i,1]); poly.AddVertexAt(i,new Point2d(q.X,q.Y),v[i,2],0,0); }
                if(tail>0)
                {
                    var bend=P(x+direction*leg,bottom+r);
                    var tip=P(x+direction*leg,bottom+tail);
                    poly.AddVertexAt(6,new Point2d(bend.X,bend.Y),0,0,0);
                    poly.AddVertexAt(7,new Point2d(tip.X,tip.Y),0,0,0);
                }
                Add(poly,Rebar);
            }

            void Development(ParapetSettings s,double x,double top,double bottom,double hook,double leg,int direction,double tail=0)
            {
                Bar(x,bottom,top,hook,leg,direction,tail);
                var upper=Text(RebarText,N(hook),x+direction*hook/2,top+2,5,0,
                    TextHorizontalMode.TextCenter,TextVerticalMode.TextBottom);
                Snap(upper,centerX:x+direction*hook/2,bottom:top+2);
                var lower=Text(RebarText,N(leg),x+direction*leg/2,bottom-2,5,0,
                    TextHorizontalMode.TextCenter,TextVerticalMode.TextTop);
                Snap(lower,centerX:x+direction*leg/2,top:bottom-2);
                if(tail>0)
                {
                    var extra=Text(RebarText,N(tail),x+direction*leg+3,bottom+tail/2,5,Math.PI/2,
                        TextHorizontalMode.TextCenter,TextVerticalMode.TextVerticalMid);
                    Snap(extra,centerX:x+direction*leg+3,centerY:bottom+tail/2);
                }
                string label=(s.DikeyAdet>0?s.DikeyAdet.ToString(CultureInfo.InvariantCulture):"")+"ø"+N(s.DikeyCap)+"/"+N(s.DikeyAralik)+" L="+N(s.DonatiBoyuYuz(direction>0))+" (DIKEY)";
                var t=Text(RebarText,label,x-2,(top+bottom)/2,5,Math.PI/2,
                    TextHorizontalMode.TextCenter,TextVerticalMode.TextVerticalMid);
                Snap(t,right:x-2,centerY:(top+bottom)/2);
            }
        }
    }
}
