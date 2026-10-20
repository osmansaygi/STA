using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private void PrepareKolonApplicationRebar(string st4SourcePath, Editor ed)
        {
            // Düşey açılımla aynı kaynaklar, alt kat adedi ve net aralık çap düzeltmesi.
            _kolonDuseyGpr = null;
            _kolonDuseyGprHcr = null;
            _kolonDuseyGprSarilmaKol = null;
            _kolonDuseyAllowedFloors = null;
            _kolonDuseyFloorColNo = null;
            _kolonDuseyAbsorbedColNos = null;
            _kolonDuseyContinuationOwner = null;
            _poligonCizilenDonatiImza.Clear();
            ClearFloorGeometrySessionCaches();
            string path = ResolveGprPathNextToSt4(st4SourcePath);
            if (!string.IsNullOrEmpty(path))
            {
                var lines = WindowsAnsiEncodings.ReadAllLines(path);
                _kolonDuseyGpr = KolonDonatiTableDrawer.ParseKolonBetonarmeFromFile(path, out _, out _kolonDuseyGprHcr);
                _kolonDuseyGprSarilmaKol = KolonDonatiTableDrawer.ParseKolonSarilmaKolFromLines(lines);
                GprPerdePanelDonatiParser.TryParseLines(lines, out _gprPerdePanelDonati, out _);
                GprPerdePanelDonatiParser.TryReadMaterialsFromLines(lines, out _rebarFckMPa, out _rebarFykMPa);
            }
            ApplyRebarMaterialsFromSt4(st4SourcePath);
            EnsureKapamaPerdeDonatiFromSwx(st4SourcePath);
            if (_kolonDuseyGpr == null || _kolonDuseyGpr.Count == 0)
                TryFillKolonDuseyFromSmx(st4SourcePath, ed);
            ApplyKolonDuseyNetAralikCapBuyutme(st4SourcePath, ed);
        }

        private void AppendKolonApplicationRebarLabel(
            Transaction tr, BlockTableRecord btr, FloorInfo floor, int columnNo,
            Point2d lastLabelPos, double angleRad, double height, double lineStep)
        {
            if (!_kolonApplicationActive || !_kolonApplicationInformationLabels || floor == null) return;
            var info = ResolveApplicationColumnInfo(floor, columnNo);
            string note = ApplicationGroupNote(floor, f => ResolveApplicationColumnInfo(f, columnNo).note);
            AppendApplicationLabelLines(tr, btr, lastLabelPos, angleRad, height, lineStep, info.rebar, note);
        }

        private (string rebar, string note) ResolveApplicationColumnInfo(FloorInfo floor, int columnNo)
        {
            int fi = _model.Floors.IndexOf(floor);
            var col = _model.Columns.FirstOrDefault(c => c.ColumnNo == columnNo);
            if (fi < 0 || col == null) return (null, null);
            var g = GetColumnPolygonForTable(floor, col, 0, 0, _ntsDrawFactory);
            if (g == null || g.IsEmpty) return (null, null);
            var c = g.Centroid;
            var rot = AffineTransformation.RotationInstance(-col.AngleDeg * Math.PI / 180.0, c.X, c.Y);
            var local = rot.Transform(g);
            var e = local.EnvelopeInternal;
            SortedDictionary<int, int> byDia;
            if (IsKolonKesitPoligonKesit(local, e))
                byDia = CollectPoligonArmKesitAdetByDia(g, g, rot, fi, columnNo);
            else
            {
                string raw = null;
                if (_kolonDuseyGpr != null)
                    KolonDonatiTableDrawer.TryGetKolonBetonarmeCell(
                        _kolonDuseyGpr, _model.Floors, fi, columnNo, out _, out raw, out _);
                if (!KolonDonatiTableDrawer.TryParseKolonKesitDuseyDonatiByDia(raw, out byDia)
                    || byDia == null || byDia.Count == 0)
                    byDia = new SortedDictionary<int, int>
                    {
                        [14] = IsDepremPerdeBoyOrani(Math.Max(e.Width, e.Height), Math.Min(e.Width, e.Height)) ? 18 : 8
                    };
                if (IsDepremPerdeBoyOrani(Math.Max(e.Width, e.Height), Math.Min(e.Width, e.Height)))
                    OverlayPerdeKesitDuseyAdet(byDia, e, fi, columnNo);
                else
                    OverlayKolonBaslikDuseyAdet(fi, columnNo, ref byDia);
            }
            string rebar = FormatApplicationRebar(byDia, ResolveKolonDuseyEtriyeDiaMm(fi, columnNo));
            var upFloor = fi + 1 < _model.Floors.Count ? _model.Floors[fi + 1] : null;
            bool continues = upFloor != null && HasColumnOnFloor(upFloor, col);
            var up = continues ? GetColumnPolygonForTable(upFloor, col, 0, 0, _ntsDrawFactory) : null;
            string note = ApplicationContinuationNote(continues, ApplicationSectionChanged(g, up));
            return (rebar, note);
        }

        private (string rebar, string note) ResolveApplicationWallInfo(FloorInfo floor, BeamInfo beam)
        {
            int wallNo = GetBeamNumero(beam.BeamId);
            string rebar = null;
            if (TryFindGprPerdePanel(floor, wallNo, out var panel))
            {
                // Kapama perde düşey açılımındaki katman/adet ve enine donatı çapı.
                int n = (panel.LayerCount > 0 ? panel.LayerCount : 2) * Math.Max(panel.BarCount, 2);
                rebar = FormatApplicationRebar(new SortedDictionary<int, int>
                {
                    [panel.DiaMm > 0 ? panel.DiaMm : 12] = n
                }, panel.YatayDiaMm > 0 ? panel.YatayDiaMm : 8);
            }
            int fi = _model.Floors.IndexOf(floor);
            var upFloor = fi >= 0 && fi + 1 < _model.Floors.Count ? _model.Floors[fi + 1] : null;
            var upBeam = upFloor == null ? null : MergeSameIdBeamsOnFloor(upFloor.FloorNo)
                .FirstOrDefault(b => b.IsWallFlag == 1 && GetBeamNumero(b.BeamId) == wallNo);
            bool changed = false;
            if (upBeam != null && _axisService.TryIntersect(beam.FixedAxisId, beam.StartAxisId, out Point2d a)
                && _axisService.TryIntersect(beam.FixedAxisId, beam.EndAxisId, out Point2d b)
                && _axisService.TryIntersect(upBeam.FixedAxisId, upBeam.StartAxisId, out Point2d ua)
                && _axisService.TryIntersect(upBeam.FixedAxisId, upBeam.EndAxisId, out Point2d ub))
            {
                NormalizeBeamDirection(beam.FixedAxisId, ref a, ref b);
                NormalizeBeamDirection(upBeam.FixedAxisId, ref ua, ref ub);
                changed = ApplicationSectionChanged(BuildBeamSegmentPolygon(a, b, beam, _ntsDrawFactory),
                    BuildBeamSegmentPolygon(ua, ub, upBeam, _ntsDrawFactory));
            }
            string note = ApplicationContinuationNote(upBeam != null, changed);
            return (rebar, note);
        }

        private static string ApplicationContinuationNote(bool continues, bool changed)
        {
            if (!continues) return "(not: kolon üst katta devam etmiyor)";
            return changed ? "(not: kolon kesiti üst katta değişiyor)" : null;
        }

        private static bool ApplicationSectionChanged(Geometry current, Geometry upper)
        {
            if (current == null || upper == null || current.IsEmpty || upper.IsEmpty) return false;
            // Poligonlarda dış sınır aynı kalırken kol/boşluk değişmesi de kesit değişimidir.
            return current.SymmetricDifference(upper).Area > 1.0;
        }

        private static string FormatApplicationRebar(SortedDictionary<int, int> byDia, int etriyeDia)
        {
            if (byDia == null || byDia.Count == 0) return null;
            var parts = byDia.Where(p => p.Key >= 6 && p.Value > 0)
                .Select(p => p.Value.ToString(CultureInfo.InvariantCulture) + "ø" + p.Key.ToString(CultureInfo.InvariantCulture));
            string bars = string.Join(" + ", parts);
            return bars.Length == 0 ? null : "(" + bars + " - etr.ø" + etriyeDia.ToString(CultureInfo.InvariantCulture) + ")";
        }

        private void AppendApplicationLabelLines(
            Transaction tr, BlockTableRecord btr, Point2d lastLabelPos, double angleRad,
            double height, double lineStep, string rebar, string note)
        {
            int line = 0;
            foreach (string value in new[] { rebar, note })
            {
                if (string.IsNullOrEmpty(value)) continue;
                line++;
                var p = new Point3d(lastLabelPos.X + line * lineStep * Math.Sin(angleRad),
                    lastLabelPos.Y - line * lineStep * Math.Cos(angleRad), 0);
                var text = new DBText
                {
                    Layer = LayerYazi,
                    Height = 10.0 * TemelFoundationAnnotMul,
                    TextStyleId = GetOrCreateYaziBeykentTextStyle(tr, btr.Database),
                    TextString = KolonDonatiTableDrawer.NormalizeDiameterSymbol(value),
                    Position = p,
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode = TextVerticalMode.TextBottom,
                    AlignmentPoint = p,
                    Rotation = angleRad
                };
                if (value == note)
                    text.ColorIndex = 1;
                AppendEntity(tr, btr, text);
            }
        }
    }
}
