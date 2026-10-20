using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NetTopologySuite.Geometries;
using ST4AksCizCSharp;

namespace ST4PlanIdCiz
{
    public sealed partial class PlanIdDrawingManager
    {
        private bool _kolonApplicationInformationLabels = true;
        private bool _kolonApplicationActive;
        private List<int> _kolonApplicationFloorGroup;

        private List<List<int>> BuildApplicationFloorGroups(bool merge)
        {
            var ordered = Enumerable.Range(0, _model.Floors.Count)
                .OrderBy(i => _model.Floors[i].ElevationM).ToList();
            if (!merge) return BuildKolonDuseyHerKatAyriGruplari(ordered);

            var result = new List<List<int>>();
            // Önce düşey açılımın mevcut kat tipi hesabı; sonra aplikasyon yerleşimi.
            foreach (var candidates in BuildKolonDuseyBenzerKatGruplari(ordered))
            {
                var byPlan = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                foreach (int fi in candidates)
                {
                    string signature = ApplicationFloorPlanSignature(fi);
                    if (!byPlan.TryGetValue(signature, out var group))
                    {
                        group = new List<int>();
                        byPlan.Add(signature, group);
                        result.Add(group);
                    }
                    group.Add(fi);
                }
            }
            return result.OrderBy(g => _model.Floors[g[0]].ElevationM).ToList();
        }

        private static string ApplicationGeometrySignature(Geometry geometry)
        {
            if (geometry == null || geometry.IsEmpty) return "";
            var copy = geometry.Copy();
            copy.Normalize();
            return copy.AsText();
        }

        private string ApplicationFloorPlanSignature(int fi)
        {
            var floor = _model.Floors[fi];
            var signature = new StringBuilder();
            foreach (var col in _model.Columns.OrderBy(c => c.ColumnNo))
            {
                if (!HasColumnOnFloor(floor, col)) continue;
                signature.Append("S").Append(col.ColumnNo).Append('=')
                    .Append(ApplicationGeometrySignature(GetColumnPolygonForTable(floor, col, 0, 0, _ntsDrawFactory)))
                    .Append(':').Append(ResolveApplicationColumnInfo(floor, col.ColumnNo).rebar).Append(';');
            }
            foreach (var beam in MergeSameIdBeamsOnFloor(floor.FloorNo)
                .Where(b => b.IsWallFlag == 1).OrderBy(b => GetBeamNumero(b.BeamId)))
            {
                signature.AppendFormat(CultureInfo.InvariantCulture,
                    "P{0}={1}/{2}/{3}/{4}/{5}/{6}/{7}/{8}:", GetBeamNumero(beam.BeamId),
                    beam.FixedAxisId, beam.StartAxisId, beam.EndAxisId, beam.WidthCm,
                    beam.OffsetRaw, beam.HeightCm, beam.Point1KotCm, beam.Point2KotCm);
                signature.Append(ResolveApplicationWallInfo(floor, beam).rebar).Append(';');
            }
            return signature.ToString();
        }

        private IEnumerable<FloorInfo> ApplicationGroupFloors(FloorInfo fallback)
        {
            if (_isKolon50Mode && _kolonApplicationFloorGroup != null && _kolonApplicationFloorGroup.Count > 0)
                return _kolonApplicationFloorGroup.Select(fi => _model.Floors[fi]);
            return new[] { fallback };
        }

        private string ApplicationElementName(string prefix, FloorInfo floor, string number)
        {
            return prefix + (!string.IsNullOrEmpty(floor?.ShortName) ? floor.ShortName
                : floor != null ? floor.FloorNo.ToString(CultureInfo.InvariantCulture) : "B") + number;
        }

        private string ApplicationGroupTitle(FloorInfo floor)
        {
            return BuildKolonPlanAntetTitle(floor);
        }

        private string ApplicationGroupSimilarFloorText(FloorInfo floor)
        {
            return BuildKalipBenzerKatSatiri(ApplicationGroupFloors(floor)
                .Where(f => f.FloorNo != floor.FloorNo).ToList());
        }

        private string ApplicationGroupNote(FloorInfo floor, Func<FloorInfo, string> getNote)
        {
            var floors = ApplicationGroupFloors(floor).ToList();
            if (floors.Count == 1) return getNote(floors[0]);
            var notes = floors.Select(f => new { floor = f, note = getNote(f) })
                .Where(x => !string.IsNullOrEmpty(x.note)).ToList();
            if (notes.Count == floors.Count && notes.Select(x => x.note).Distinct().Count() == 1)
                return notes[0].note;
            // Birleşen katların yalnız bazılarında olan not, ilgili kat adıyla yazılır.
            return string.Join("  ", notes.GroupBy(x => x.note).Select(g => "("
                + string.Join(" / ", g.Select(x => string.IsNullOrWhiteSpace(x.floor.Name)
                    ? x.floor.ShortName : x.floor.Name)) + ": " + g.Key.Trim('(', ')') + ")"));
        }
    }
}
