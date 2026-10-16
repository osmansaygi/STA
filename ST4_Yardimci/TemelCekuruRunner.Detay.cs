using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace ST4Yardimci
{
    internal static partial class TemelCekuruRunner
    {
        private sealed partial class Fit
        {
            private double DetailDy1L, DetailDy1R;
            private int DetailIcR, DetailDisR;
            private bool DetailLeft, DetailRight;

            private bool DetailEnabled(bool right) => right ? DetailRight : DetailLeft;

            private void ConfigureDetail(double leftHeight, double rightHeight)
            {
                DetayBaslik = true;
                KarsiUstAdet = DetailLengthAdet(false);
                KarsiAltAdet = DetailLengthAdet(true);
                GeometryChanged = true;
                SplitHeight = true;
                DetailLeft = TemelDetaySettings.SolKademe;
                DetailRight = TemelDetaySettings.SagKademe;
                StemSolCm = DetailLeft ? leftHeight : 0;
                StemSagCm = DetailRight ? rightHeight : 0;
                double floor = Y3 + Dy3;
                DetailDy1L = floor + StemSolCm * DuPerCm - Y1;
                DetailDy1R = floor + StemSagCm * DuPerCm - Y1;
                Dy2L = (DetailLeft ? floor + (StemSolCm - TemelDetaySettings.UstTemelYuksekligiCm) * DuPerCm : Y4 + Dy4) - Y2;
                Dy2R = (DetailRight ? floor + (StemSagCm - TemelDetaySettings.SagUstTemelYuksekligiCm) * DuPerCm : Y4 + Dy4) - Y2;
                Dy3L = Dy3R = Dy3; // Ortak alt temel ve taban; sığ taraf yukarı kaldırılmaz.
                double angle = TemelCekuruSettings.GrobetonAcisi * Math.PI / 180.0;
                DRunL = Math.Max(0, (Y2 + Dy2L - Y4 - Dy4) / Math.Tan(angle)) - TemplateRunDu;
                DRunR = Math.Max(0, (Y2 + Dy2R - Y4 - Dy4) / Math.Tan(angle)) - TemplateRunDu;
            }

            private double DyDetail(double y, bool right)
            {
                double top = right ? DetailDy1R : DetailDy1L;
                double step = right ? Dy2R : Dy2L;
                double hatil = DetailEnabled(right) ? TemelCekuruSettings.HatilYuksekligiCm * DuPerCm : 0;
                double hatilShift = Y1 + top + hatil - Y0;
                if (y >= Y0 - 0.8) return hatilShift;
                if (Math.Abs(y - YLip) <= 3) return hatilShift;
                if (Math.Abs(y - Y1) <= 12) return top;
                if (Math.Abs(y - Y2) <= 12) return step;
                if (Math.Abs(y - Y3) <= 12) return Dy3;
                if (Math.Abs(y - Y4) <= 12) return Dy4;
                if (y > Y1) return Lerp(y, Y1, Y0, top, hatilShift);
                if (y > Y2) return Lerp(y, Y2, Y1, step, top);
                if (y > Y3) return Lerp(y, Y3, Y2, Dy3, step);
                if (y > Y4) return Lerp(y, Y4, Y3, Dy4, Dy3);
                return Dy4;
            }

            private double DyDetailSide(double y, bool right)
            {
                double top = right ? DetailDy1R : DetailDy1L;
                return y >= 1330 ? top : y <= 1160 ? Dy4 : Lerp(y, 1160, 1330, Dy4, top);
            }

            private int StraightForX(double x)
            {
                if (!DetayBaslik) return StraightCm;
                double height = x > XKotCenter ? StemSagCm : StemSolCm;
                return Math.Max(2, (int)Math.Round(height + TemelCekuruSettings.AltTemelYuksekligiCm
                    - 2 * TemelCekuruSettings.PaspayiCm, MidpointRounding.AwayFromZero));
            }

            private void SetRightDetailAdet()
            {
                DetailIcR = IcAdet;
                DetailDisR = DisAdet;
                if (!DetayBaslik) return;
                double ust = Math.Max(1, TemelCekuruSettings.UstAralikCm) * DuPerCm;
                double alt = Math.Max(1, TemelCekuruSettings.AltAralikCm) * DuPerCm;
                DetailIcR = DetailRight ? Math.Max(0, BarCount(NamedDot(9), NamedDot(11), ust)
                    + BarCount(NamedDot(11), NamedDot(13), ust) - 4) : 0;
                DetailDisR = DetailRight ? Math.Max(0, BarCount(NamedDot(12), NamedDot(14), alt)
                    + BarCount(NamedDot(14), NamedDot(16), alt) - 3) : 0;
                if (!DetailLeft) IcAdet = DisAdet = 0;
            }

            private int SideAdet(bool outside, bool right)
            {
                if (DetayBaslik && right) return outside ? DetailDisR : DetailIcR;
                return outside ? DisAdet : IcAdet;
            }

            private bool SingleDetail => DetayBaslik && DetailLeft != DetailRight;

            private static int DetailLengthAdet(bool bottom)
            {
                double length = Math.Max(0, TemelDetaySettings.KademeUzunluguCm - 2 * TemelCekuruSettings.PaspayiCm);
                double spacing = Math.Max(1, bottom ? TemelCekuruSettings.AltAralikCm : TemelCekuruSettings.UstAralikCm);
                return (int)Math.Ceiling(length / spacing - 1e-6) + 1;
            }

            private double DetailCutX()
            {
                double reach = Math.Max(80, ElliCapCm(true) + 20) * DuPerCm;
                return DetailLeft
                    ? Math.Min(XInnerL + reach, XOutR + DW + DSide + DRunR - 10 * DuPerCm)
                    : Math.Max(XInnerR + DW - reach, XOutL - DSide - DRunL + 10 * DuPerCm);
            }

            private bool KeepDetailX(double x)
            {
                return DetailLeft ? x <= DetailCutX() + NamedDotTolerance : x >= DetailCutX() - NamedDotTolerance;
            }
            private bool OmitDetailEntity(Entity ent)
            {
                double x;
                if (SingleDetail)
                {
                    if (ent is Polyline horizontal && IsRebarGeom(horizontal) && horizontal.NumberOfVertices >= 4
                        && !IsSideBar(CentroidX(horizontal)) && !BarIsWall(horizontal)) return true;
                    if (ent is DBText label && !IsSideBar(label.Position.X))
                    {
                        string text = label.TextString ?? "";
                        if (label.Position.Y < 1120 || text.StartsWith("14\u00f8", StringComparison.Ordinal)
                            || text.StartsWith("16\u00f8", StringComparison.Ordinal)) return true;
                    }
                }
                if (ent is Polyline pl && IsRebarGeom(pl) && pl.NumberOfVertices >= 4)
                {
                    x = CentroidX(pl);
                    return !DetailEnabled(x > XKotCenter) && (IsSideBar(x) || BarIsWall(pl));
                }
                if (ent is DBText tx)
                {
                    x = tx.Position.X;
                    if (DetailEnabled(x > XKotCenter)) return false;
                    string text = tx.TextString ?? "";
                    if (text.IndexOf("DİKEY", StringComparison.Ordinal) >= 0 || text.StartsWith("3\u00f8", StringComparison.Ordinal)) return true;
                    return IsSideBar(x) && int.TryParse(text.Trim(), out _);
                }
                return false;
            }

            private void DrawDetailKots(Transaction tr, BlockTableRecord ms)
            {
                EnsureKotLayer(tr, ms.Database, LayerKotCizgi);
                EnsureKotLayer(tr, ms.Database, LayerKotYazi);
                var placed = new List<Point3d>();
                void AddKot(double level, double x, double y, bool right)
                {
                    Point3d point = new Point3d(x, y, 0);
                    if (placed.Exists(p => p.DistanceTo(point) <= NamedDotTolerance)) return;
                    placed.Add(point);
                    DrawKotSymbol(tr, ms.Database, ms, level, x, y, textToRight: right);
                }
                double floor = Y3 + Dy3;
                double floorCm = TemelCekuruSettings.TemelUstKotuCm - Math.Max(StemSolCm, StemSagCm);
                for (int side = 0; side < 2; side++)
                {
                    bool right = side == 1;
                    if (!DetailEnabled(right)) continue;
                    double top = Y1 + (right ? DetailDy1R : DetailDy1L);
                    double step = Y2 + (right ? Dy2R : Dy2L);
                    double x = right ? XOutR + DW + DSide + DRunR + KotRightGapDu : XOutL - DSide - DRunL - KotRightGapDu;
                    double topCm = floorCm + (right ? StemSagCm : StemSolCm);
                    double thick = right ? TemelDetaySettings.SagUstTemelYuksekligiCm : TemelDetaySettings.UstTemelYuksekligiCm;
                    AddKot(topCm / 100, x, top, right);
                    AddKot((topCm - thick) / 100, x, step, right);
                    if (TemelCekuruSettings.HatilYuksekligiCm >= 0.5)
                        AddKot((topCm + TemelCekuruSettings.HatilYuksekligiCm) / 100,
                            x, top + TemelCekuruSettings.HatilYuksekligiCm * DuPerCm, right);
                }
                bool baseRight = DetailRight;
                double baseX = baseRight ? XOutR + DW + DSide + DRunR + KotRightGapDu
                    : XOutL - DSide - DRunL - KotRightGapDu;
                AddKot(floorCm / 100, baseX, floor, baseRight);
                AddKot((floorCm - TemelCekuruSettings.AltTemelYuksekligiCm) / 100,
                    baseX, Y4 + Dy4, baseRight);
            }
            private double DetailMeasureLane()
            {
                return 10 * DuPerCm;
            }

            private void DrawDetailDimensions(Transaction tr, BlockTableRecord ms)
            {
                const string layer = "OLCU (BEYKENT)";
                double floor = Y3 + Dy3, bottom = Y4 + Dy4;
                double gap = DetailMeasureLane();
                double xL = XInnerL, xR = XInnerR + DW;
                Point3d toeL = MapPt(new Point3d(XToeL, YGro, 0));
                Point3d toeR = MapPt(new Point3d(XToeR, YGro, 0));
                double topL = Y1 + (DetayBaslik ? DetailDy1L : Dy1), topR = Y1 + (DetayBaslik ? DetailDy1R : Dy1);
                double highest = floor;
                for (int side = 0; side < 2; side++)
                {
                    bool right = side == 1;
                    if (DetayBaslik && !DetailEnabled(right)) continue;
                    double direction = right ? -1 : 1;
                    double face = right ? xR : xL;
                    double heightLane = face + direction * gap;
                    double thicknessLane = face + direction * 2 * gap;
                    double top = right ? topR : topL;
                    double step = Y2 + (DetayBaslik ? (right ? Dy2R : Dy2L) : Dy2);
                    double hatil = TemelCekuruSettings.HatilYuksekligiCm * DuPerCm;
                    highest = Math.Max(highest, top + hatil);
                    // Kademenin alt temele bakan iç yüzünde iki düşey ölçü sırası.
                    AddVDim(tr, ms, face, floor, top, heightLane, layer);
                    AddVDim(tr, ms, face, bottom, floor, heightLane, layer);
                    AddVDim(tr, ms, face, toeL.Y, bottom, heightLane, layer);
                    AddVDim(tr, ms, face, top, top + hatil, heightLane, layer);
                    // İkinci sıra: hatıl + üst temel kalınlığı + grobeton altına kalan bölüm.
                    AddVDim(tr, ms, face, top, top + hatil, thicknessLane, layer);
                    AddVDim(tr, ms, face, step, top, thicknessLane, layer);
                    AddVDim(tr, ms, face, toeL.Y, step, thicknessLane, layer);
                }
                // Yatay ölçüler çizimin üstünde, iki sıra arasında 10 cm.
                if (SingleDetail) highest = Math.Max(highest, floor + 10);
                double chain = highest + gap;
                double total = chain + gap;
                double stepL = XStepL - DSide, stepR = XStepR + DW + DSide;
                if (SingleDetail)
                {
                    // Kesme işareti ölçülendirilmez; yalnız gerçek kenarlar kullanılır.
                    if (DetailLeft)
                    {
                        AddHDim(tr, ms, stepL, xL, topL, chain, layer);
                        AddHDim(tr, ms, stepL, xL, topL, total, layer);
                    }
                    else
                    {
                        AddHDim(tr, ms, xR, stepR, topR, chain, layer);
                        AddHDim(tr, ms, xR, stepR, topR, total, layer);
                    }
                }
                else
                {
                    AddHDim(tr, ms, stepL, xL, topL, chain, layer);
                    AddHDim(tr, ms, xL, xR, Math.Max(topL, topR), chain, layer);
                    AddHDim(tr, ms, xR, stepR, topR, chain, layer);
                    AddHDim(tr, ms, stepL, stepR, Math.Max(topL, topR), total, layer);
                }
                double groLine = toeL.Y - gap;
                if (!DetayBaslik || DetailLeft)
                    AddHDim(tr, ms, toeL.X, stepL, toeL.Y, groLine, layer);
                if (!DetayBaslik || DetailRight)
                    AddHDim(tr, ms, stepR, toeR.X, toeR.Y, groLine, layer);
            }
        }
    }
}
