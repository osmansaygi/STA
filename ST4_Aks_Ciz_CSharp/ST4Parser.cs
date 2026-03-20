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
            bool inFloorsData = false;
            bool inFloorsContinuous = false;
            bool inPolygonCols = false;
            bool inPolygonSection = false;
            bool inContinuousFoundations = false;
            bool inSlabFoundations = false;
            bool inTieBeams = false;
            string pendingContinuousName = null;
            string pendingSlabName = null;
            string pendingTieBeamName = null;
            bool skipNextContinuousLine = false;
            bool skipNextSlabLine = false;
            int? headerNX = null;
            int? headerNY = null;

            for (int i = 0; i < rawLines.Length; i++)
            {
                string line = rawLines[i] ?? string.Empty;
                string u = line.Trim().ToLowerInvariant();

                if (u.Contains("floors data"))
                {
                    inFloorsData = true;
                    inColAxis = false;
                    inBeams = false;
                    inColData = false;
                    continue;
                }

                if ((i == 4 || i == 5) && line.Length > 0)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 3 && St4Text.TryParseInt(p[2], out int keyStep) && (keyStep == 100 || keyStep == 1000))
                    {
                        model.SlabFloorKeyStep = keyStep;
                        if (p.Count >= 5 && St4Text.TryParseInt(p[3], out int beamStep) && (beamStep == 100 || beamStep == 1000))
                            model.BeamFloorKeyStep = beamStep;
                        if (p.Count >= 5 && St4Text.TryParseInt(p[4], out int colStep) && (colStep == 100 || colStep == 1000))
                            model.ColumnFloorKeyStep = colStep;
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
                            // Açılı akslarda yuvarlama yapma; düz akslarda 0.1mm
                            if (Math.Abs(slope) <= 1e-9)
                                val = Math.Round(val, 4);
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
                    if (p.Count >= 2 &&
                        St4Text.TryParseInt(p[0], out int sectionId) &&
                        St4Text.TryParseDouble(p[1], out double cw) &&
                        sectionId >= 100 && cw > 0)
                    {
                        double ch = 0;
                        if (p.Count >= 3) St4Text.TryParseDouble(p[2], out ch);
                        if (ch > 0)
                            model.ColumnDimsBySectionId[sectionId] = (cw, ch);
                        else
                            model.ColumnDimsBySectionId[sectionId] = (cw, cw);
                    }
                    continue;
                }

                if (u.Contains("floors data"))
                {
                    inFloorsData = true;
                    continue;
                }
                if (inFloorsData && u.StartsWith("/"))
                {
                    inFloorsData = false;
                    continue;
                }
                if (u.Contains("floors continuous"))
                {
                    inFloorsContinuous = true;
                    continue;
                }
                if (inFloorsContinuous && u.StartsWith("/"))
                {
                    inFloorsContinuous = false;
                    continue;
                }
                if (inFloorsContinuous && line.Length > 0)
                {
                    continue;
                }
                if (inFloorsData && line.Length > 2)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 12 &&
                        St4Text.TryParseInt(p[0], out int slabId) &&
                        St4Text.TryParseInt(p[8], out int a1) &&
                        St4Text.TryParseInt(p[9], out int a2) &&
                        St4Text.TryParseInt(p[10], out int a3) &&
                        St4Text.TryParseInt(p[11], out int a4) &&
                        slabId > 0)
                    {
                        model.Slabs.Add(new SlabInfo { SlabId = slabId, Axis1 = a1, Axis2 = a2, Axis3 = a3, Axis4 = a4 });
                        if (p.Count >= 25 && p[24] == "1")
                            model.StairSlabIds.Add(slabId);
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

                if (u.Contains("continuous foundations"))
                {
                    inContinuousFoundations = true;
                    inSlabFoundations = false;
                    pendingContinuousName = null;
                    skipNextContinuousLine = false;
                    continue;
                }
                if (inContinuousFoundations && u.StartsWith("/"))
                {
                    inContinuousFoundations = false;
                    continue;
                }
                if (inContinuousFoundations)
                {
                    if (skipNextContinuousLine) { skipNextContinuousLine = false; continue; }
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 10 &&
                        St4Text.TryParseInt(p[4], out int fixedAxis) &&
                        St4Text.TryParseInt(p[5], out int startAxis) &&
                        St4Text.TryParseInt(p[6], out int endAxis) &&
                        fixedAxis >= 1001 && fixedAxis <= 2999 &&
                        (startAxis >= 1001 && startAxis <= 2999) &&
                        (endAxis >= 1001 && endAxis <= 2999))
                    {
                        double widthCm = 80.0;
                        if (p.Count > 2 && St4Text.TryParseDouble(p[2], out double w3) && w3 > 0) widthCm = w3;
                        double startExtCm = 0.0, endExtCm = 0.0;
                        if (p.Count > 7) St4Text.TryParseDouble(p[7], out startExtCm);
                        if (p.Count > 8) St4Text.TryParseDouble(p[8], out endExtCm);
                        int offsetRaw = 0;
                        if (p.Count > 9) St4Text.TryParseInt(p[9], out offsetRaw);
                        int ampatmanAlign = 0;
                        if (p.Count > 0) St4Text.TryParseInt(p[0], out ampatmanAlign);
                        double ampatmanWidthCm = 0.0;
                        if (p.Count > 3) St4Text.TryParseDouble(p[3], out ampatmanWidthCm);
                        double tieBeamWidthCm = 0.0;
                        int tieBeamOffsetRaw = 0;
                        if (p.Count > 11) St4Text.TryParseDouble(p[11], out tieBeamWidthCm);
                        if (p.Count > 13) St4Text.TryParseInt(p[13], out tieBeamOffsetRaw);
                        model.ContinuousFoundations.Add(new ContinuousFoundationInfo
                        {
                            Name = pendingContinuousName ?? "",
                            FixedAxisId = fixedAxis,
                            StartAxisId = startAxis,
                            EndAxisId = endAxis,
                            WidthCm = widthCm,
                            StartExtensionCm = startExtCm,
                            EndExtensionCm = endExtCm,
                            OffsetRaw = offsetRaw,
                            AmpatmanAlign = ampatmanAlign,
                            AmpatmanWidthCm = ampatmanWidthCm,
                            TieBeamWidthCm = tieBeamWidthCm,
                            TieBeamOffsetRaw = tieBeamOffsetRaw
                        });
                        pendingContinuousName = null;
                        skipNextContinuousLine = true;
                    }
                    else if (line.Length > 0 && !line.TrimStart().StartsWith("0", StringComparison.Ordinal) && (line.IndexOf(',') < 0 || line.Trim().Length < 10))
                    {
                        pendingContinuousName = line.Trim();
                    }
                    continue;
                }

                if (u.Contains("slab foundations"))
                {
                    inSlabFoundations = true;
                    inContinuousFoundations = false;
                    inTieBeams = false;
                    pendingSlabName = null;
                    skipNextSlabLine = false;
                    continue;
                }
                if (inSlabFoundations && u.StartsWith("/"))
                {
                    inSlabFoundations = false;
                    continue;
                }
                if (u.Contains("tie beams"))
                {
                    inTieBeams = true;
                    inSlabFoundations = false;
                    pendingTieBeamName = null;
                    continue;
                }
                if (inTieBeams && u.StartsWith("/"))
                {
                    inTieBeams = false;
                    continue;
                }
                if (inTieBeams)
                {
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 6 &&
                        St4Text.TryParseInt(p[2], out int fixedAxis) &&
                        St4Text.TryParseInt(p[3], out int startAxis) &&
                        St4Text.TryParseInt(p[4], out int endAxis) &&
                        fixedAxis >= 1001 && fixedAxis <= 2999 &&
                        (startAxis >= 1001 && startAxis <= 2999) &&
                        (endAxis >= 1001 && endAxis <= 2999))
                    {
                        double widthCm = 30.0;
                        if (p.Count > 0 && St4Text.TryParseDouble(p[0], out double w0) && w0 > 0) widthCm = w0;
                        int offsetRaw = 0;
                        if (p.Count > 5) St4Text.TryParseInt(p[5], out offsetRaw);
                        model.TieBeams.Add(new TieBeamInfo
                        {
                            Name = pendingTieBeamName ?? "",
                            FixedAxisId = fixedAxis,
                            StartAxisId = startAxis,
                            EndAxisId = endAxis,
                            WidthCm = widthCm,
                            OffsetRaw = offsetRaw
                        });
                        pendingTieBeamName = null;
                    }
                    else if (line.Length > 0 && line.Trim().Length > 0 && (line.IndexOf(',') < 0 || line.Trim().Length < 8))
                    {
                        pendingTieBeamName = line.Trim();
                    }
                    continue;
                }
                if (inSlabFoundations)
                {
                    if (skipNextSlabLine) { skipNextSlabLine = false; continue; }
                    var p = St4Text.SplitCsv(line);
                    if (p.Count >= 5 &&
                        St4Text.TryParseDouble(p[0], out double thickness) &&
                        thickness > 0 &&
                        St4Text.TryParseInt(p[1], out int a1) &&
                        St4Text.TryParseInt(p[2], out int a2) &&
                        St4Text.TryParseInt(p[3], out int a3) &&
                        St4Text.TryParseInt(p[4], out int a4))
                    {
                        int x1 = 0, x2 = 0, y1 = 0, y2 = 0;
                        if (a1 >= 1001 && a1 <= 1999 && a2 >= 1001 && a2 <= 1999 && a3 >= 2001 && a3 <= 2999 && a4 >= 2001 && a4 <= 2999)
                        { x1 = a1; x2 = a2; y1 = a3; y2 = a4; }
                        else if (a1 >= 2001 && a1 <= 2999 && a2 >= 2001 && a2 <= 2999 && a3 >= 1001 && a3 <= 1999 && a4 >= 1001 && a4 <= 1999)
                        { y1 = a1; y2 = a2; x1 = a3; x2 = a4; }
                        else
                        {
                            var xList = new List<int>(); var yList = new List<int>();
                            if (a1 >= 1001 && a1 <= 1999) xList.Add(a1); else if (a1 >= 2001 && a1 <= 2999) yList.Add(a1);
                            if (a2 >= 1001 && a2 <= 1999) xList.Add(a2); else if (a2 >= 2001 && a2 <= 2999) yList.Add(a2);
                            if (a3 >= 1001 && a3 <= 1999) xList.Add(a3); else if (a3 >= 2001 && a3 <= 2999) yList.Add(a3);
                            if (a4 >= 1001 && a4 <= 1999) xList.Add(a4); else if (a4 >= 2001 && a4 <= 2999) yList.Add(a4);
                            if (xList.Count == 2 && yList.Count == 2) { x1 = xList[0]; x2 = xList[1]; y1 = yList[0]; y2 = yList[1]; }
                        }
                        if (x1 != 0 && y1 != 0)
                        {
                            model.SlabFoundations.Add(new SlabFoundationInfo
                            {
                                Name = pendingSlabName ?? "",
                                ThicknessCm = thickness,
                                AxisX1 = x1,
                                AxisX2 = x2,
                                AxisY1 = y1,
                                AxisY2 = y2
                            });
                            pendingSlabName = null;
                            skipNextSlabLine = true;
                        }
                    }
                    else if (line.Length > 0 && line.IndexOf(',') < 0 && line.Trim().Length > 0 && char.IsLetter(line.Trim()[0]))
                    {
                        pendingSlabName = line.Trim();
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
