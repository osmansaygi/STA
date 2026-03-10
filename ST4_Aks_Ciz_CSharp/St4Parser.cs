using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.Geometry;

namespace ST4AksCizCSharp
{
    public sealed class St4Parser
    {
        public St4Model Parse(string filePath)
        {
            var model = new St4Model();
            var allValues = new List<double>();
            var allSlopes = new List<double>();
            var storyLines = new List<string>();
            var rawLines = File.ReadAllLines(filePath);

            bool inAxis = false;
            bool inStory = false;
            bool inColAxis = false;
            bool inBeams = false;
            bool inColData = false;
            bool inPolygonCols = false;
            bool inPolygonSection = false;
            int? headerNX = null;
            int? headerNY = null;

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i] ?? string.Empty;
                string u = line.Trim().ToLowerInvariant();

                if ((i == 4 || i == 5) && line.Length > 0)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 3 && (p[2] == "100" || p[2] == "1000"))
                    {
                        if (St4Text.TryParseInt(p[0], out int nx) && St4Text.TryParseInt(p[1], out int ny))
                        {
                            headerNX = nx;
                            headerNY = ny;
                        }
                    }
                }

                if (u.StartsWith("/story/"))
                {
                    inStory = true;
                    continue;
                }
                if (inStory && u.StartsWith("/axis data/"))
                {
                    inStory = false;
                    inAxis = true;
                    continue;
                }
                if (inStory && line.Length > 0)
                {
                    storyLines.Add(line.Trim());
                    continue;
                }

                if (u.StartsWith("/axis data/"))
                {
                    inAxis = true;
                    continue;
                }
                if (inAxis && u.StartsWith("/circle axis/"))
                {
                    inAxis = false;
                    continue;
                }
                if (inAxis && line.Length > 0)
                {
                    var axisParts = St4Text.SplitCsv(line);
                    if (axisParts.Count >= 2 &&
                        St4Text.TryParseDouble(axisParts[0], out double slope) &&
                        St4Text.TryParseDouble(axisParts[1], out double val))
                    {
                        if (Math.Abs(val) < 100.0)
                        {
                            val *= 100.0;
                        }
                        allValues.Add(val);
                        allSlopes.Add(slope);
                    }
                    continue;
                }

                if (u.Contains("column axis data"))
                {
                    inColAxis = true;
                    continue;
                }
                if (inColAxis && u.StartsWith("/"))
                {
                    inColAxis = false;
                    continue;
                }
                if (inColAxis && line.Length > 2)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 3 &&
                        St4Text.TryParseInt(p[1], out int axId) &&
                        St4Text.TryParseInt(p[2], out int ayId) &&
                        axId >= 1001 && axId <= 1999 &&
                        ayId >= 2001 && ayId <= 2999)
                    {
                        int colType = 1;
                        int off1 = -1;
                        int off2 = -1;
                        double ang = 0;
                        if (p.Count >= 1 && St4Text.TryParseInt(p[0], out int ct)) colType = ct;
                        if (p.Count >= 4 && St4Text.TryParseInt(p[3], out int o1)) off1 = o1;
                        if (p.Count >= 5 && St4Text.TryParseInt(p[4], out int o2)) off2 = o2;
                        if (p.Count >= 7 && St4Text.TryParseDouble(p[6], out double a)) ang = a;
                        colType = Math.Max(1, Math.Min(3, colType));

                        model.Columns.Add(new ColumnAxisInfo
                        {
                            ColumnNo = model.Columns.Count + 1,
                            ColumnType = colType,
                            AxisXId = axId,
                            AxisYId = ayId,
                            OffsetXRaw = off1,
                            OffsetYRaw = off2,
                            AngleDeg = ang
                        });
                    }
                    continue;
                }

                if (u.Contains("beams data"))
                {
                    inBeams = true;
                    continue;
                }
                if (inBeams && u.StartsWith("/"))
                {
                    inBeams = false;
                    continue;
                }
                if (inBeams && line.Length > 2)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 8 &&
                        St4Text.TryParseInt(p[0], out int beamId) &&
                        St4Text.TryParseInt(p[4], out int fixedAxis) &&
                        St4Text.TryParseInt(p[5], out int startAxis) &&
                        St4Text.TryParseInt(p[6], out int endAxis) &&
                        ((fixedAxis >= 1001 && fixedAxis <= 1999) || (fixedAxis >= 2001 && fixedAxis <= 2999)))
                    {
                        double width = 40.0;
                        double height = 0.0;
                        int off = 0;
                        int wallFlag = 0;
                        if (St4Text.TryParseDouble(p[1], out double w)) width = w;
                        if (St4Text.TryParseDouble(p[2], out double h)) height = h;
                        if (p.Count >= 8 && St4Text.TryParseInt(p[7], out int o)) off = o;
                        if (p.Count >= 15 && St4Text.TryParseInt(p[14], out int wf)) wallFlag = wf;
                        if (width <= 0) width = 40.0;
                        if (beamId >= 100)
                        {
                            model.Beams.Add(new BeamInfo
                            {
                                BeamId = beamId,
                                FixedAxisId = fixedAxis,
                                StartAxisId = startAxis,
                                EndAxisId = endAxis,
                                WidthCm = width,
                                HeightCm = height,
                                OffsetRaw = off,
                                IsWallFlag = wallFlag
                            });
                        }
                    }
                    continue;
                }

                if (u.Contains("columns data"))
                {
                    inColData = true;
                    continue;
                }
                if (inColData && u.StartsWith("/"))
                {
                    inColData = false;
                    continue;
                }
                if (inColData && line.Length > 2)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 3 &&
                        St4Text.TryParseInt(p[0], out int sectionId) &&
                        St4Text.TryParseDouble(p[1], out double cw) &&
                        St4Text.TryParseDouble(p[2], out double ch) &&
                        sectionId >= 100 && cw > 0 && ch > 0)
                    {
                        model.ColumnDimsBySectionId[sectionId] = (cw, ch);
                    }
                    continue;
                }

                if (u.Contains("polygon columns"))
                {
                    inPolygonCols = true;
                    continue;
                }
                if (u.Contains("polygon section"))
                {
                    inPolygonCols = false;
                    inPolygonSection = true;
                    continue;
                }
                if (inPolygonCols && u.StartsWith("/"))
                {
                    inPolygonCols = false;
                    continue;
                }
                if (inPolygonCols && line.Length > 2)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 2 &&
                        St4Text.TryParseInt(p[0], out int positionSectionId) &&
                        St4Text.TryParseInt(p[1], out int polygonSectionId) &&
                        positionSectionId >= 100)
                    {
                        model.PolygonColumnSectionByPositionSectionId[positionSectionId] = polygonSectionId;
                    }
                    continue;
                }
                if (inPolygonSection && u.StartsWith("/"))
                {
                    inPolygonSection = false;
                    continue;
                }
                if (inPolygonSection && line.Length > 2)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 3 &&
                        St4Text.TryParseDouble(p[0], out double vx) &&
                        St4Text.TryParseDouble(p[1], out double vy) &&
                        St4Text.TryParseInt(p[2], out int sectionId))
                    {
                        if (sectionId != 0)
                        {
                            if (!model.PolygonSections.TryGetValue(sectionId, out List<Point2d> list))
                            {
                                list = new List<Point2d>();
                                model.PolygonSections[sectionId] = list;
                            }
                            list.Add(new Point2d(vx, vy));
                        }
                    }
                    continue;
                }
            }

            int nxFinal;
            int nyFinal;
            if (headerNX.HasValue && headerNY.HasValue && headerNX.Value > 0 && headerNY.Value > 0 && allValues.Count >= headerNX.Value)
            {
                nxFinal = headerNX.Value;
                nyFinal = headerNY.Value;
            }
            else
            {
                int maxX = 1000;
                int maxY = 2000;
                foreach (var col in model.Columns)
                {
                    maxX = Math.Max(maxX, col.AxisXId);
                    maxY = Math.Max(maxY, col.AxisYId);
                }
                nxFinal = Math.Max(0, maxX - 1000);
                nyFinal = Math.Max(0, maxY - 2000);
            }

            if (nxFinal > 0 && nyFinal > 0 && allValues.Count >= nxFinal)
            {
                for (int i = 0; i < allValues.Count; i++)
                {
                    if (i < nxFinal)
                    {
                        double slope = i < allSlopes.Count ? -allSlopes[i] : 0.0;
                        model.AxisX.Add(new AxisLine(1001 + i, AxisKind.X, allValues[i], slope));
                    }
                    else if (i < nxFinal + nyFinal)
                    {
                        double slope = i < allSlopes.Count ? allSlopes[i] : 0.0;
                        model.AxisY.Add(new AxisLine(2001 + (i - nxFinal), AxisKind.Y, allValues[i], slope));
                    }
                }
            }
            else
            {
                int split = -1;
                for (int i = 1; i < allValues.Count; i++)
                {
                    if (allValues[i] < allValues[i - 1])
                    {
                        split = i;
                        break;
                    }
                }
                if (split < 0) split = allValues.Count;
                for (int i = 0; i < split; i++)
                {
                    model.AxisX.Add(new AxisLine(1001 + i, AxisKind.X, allValues[i], 0.0));
                }
                for (int i = split; i < allValues.Count; i++)
                {
                    model.AxisY.Add(new AxisLine(2001 + (i - split), AxisKind.Y, allValues[i], 0.0));
                }
            }

            if (model.AxisX.Count == 0 && model.AxisY.Count == 0)
            {
                throw new InvalidOperationException("Axis data bolumunde gecerli eksen verisi bulunamadi.");
            }

            ParseFloors(storyLines, model);
            if (model.Floors.Count == 0)
            {
                model.Floors.Add(new FloorInfo(1, "KAT 1", "1", 0.0));
            }

            return model;
        }

        private static void ParseFloors(List<string> storyLines, St4Model model)
        {
            if (storyLines.Count < 3) return;
            for (int i = 0; i < storyLines.Count; i++)
            {
                var p = St4Text.SplitCsv(storyLines[i]);
                if (p.Count < 2) continue;
                if (!St4Text.TryParseInt(p[1], out int floorNo) || floorNo <= 0 || i < 2) continue;

                double elev = 0.0;
                if (p.Count > 0) St4Text.TryParseDouble(p[0], out elev);
                string name = storyLines[i - 2].Trim();
                string shortName = storyLines[i - 1].Trim();
                model.Floors.Add(new FloorInfo(floorNo, name, shortName, elev));
            }
        }
    }
}
