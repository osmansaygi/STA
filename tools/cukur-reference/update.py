from pathlib import Path
p=Path('ST4_Yardimci/TemelCekuruRunner.cs')
s=p.read_text(encoding='utf-8-sig')
a=s.index('                if (ustBar != null && ustTx != null',s.index('private void PlaceSectionEtiket'))
b=s.index('                var left =',a)
s=s[:a]+'''                if (ustTx != null)
                    PlaceSectionGroup(tr, ms, ustTx, PhysicalDot(6), PhysicalDot(13), UstAdet, 0, -10);
                if (altTx != null)
                    PlaceSectionGroup(tr, ms, altTx, PhysicalDot(8), PhysicalDot(15), AltAdet, 0, 10);

'''+s[b:]
a=s.index('                if (left.Count >= 1 && disL != null)',a)
b=s.index('            private static DBText TakeCenter',a)
s=s[:a]+'''                if (disL != null) PlaceSectionGroup(tr, ms, disL, NamedDot(3), NamedDot(7), DisAdet, -15, 0);
                if (icL != null) PlaceSectionGroup(tr, ms, icL, NamedDot(2), NamedDot(6), IcAdet, -10, 0);
                if (disR != null) PlaceSectionGroup(tr, ms, disR, NamedDot(12), NamedDot(16), SideAdet(true, true), 15, 0);
                if (icR != null) PlaceSectionGroup(tr, ms, icR, NamedDot(9), NamedDot(13), SideAdet(false, true), 10, 0);
            }

            // The reference DWG labels dot rows, rather than the adjacent continuous bars.
            private void PlaceSectionGroup(Transaction tr, BlockTableRecord ms, DBText text,
                Point3d start, Point3d end, int count, double dx, double dy)
            {
                if (count <= 0) { text.Erase(); return; }
                bool vertical = dx != 0;
                var dots = new List<Point3d>();
                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity entity) || !TryDotCenter(entity, out Point3d dot)) continue;
                    if (entity.Layer.IndexOf("DONATI", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    double cross = vertical ? Math.Abs(dot.X - start.X) : Math.Abs(dot.Y - start.Y);
                    double t = vertical ? dot.Y : dot.X;
                    double lo = vertical ? Math.Min(start.Y, end.Y) : Math.Min(start.X, end.X);
                    double hi = vertical ? Math.Max(start.Y, end.Y) : Math.Max(start.X, end.X);
                    // The bottom row also contains the two side extensions.
                    if (!vertical && dy > 0) { lo = PhysicalDot(8).X; hi = PhysicalDot(15).X; }
                    if (cross > NamedDotTolerance || t < lo - NamedDotTolerance || t > hi + NamedDotTolerance) continue;
                    if (!dots.Exists(p => p.DistanceTo(dot) < NamedDotTolerance)) dots.Add(dot);
                }
                double center = vertical ? (start.Y + end.Y) / 2 : (start.X + end.X) / 2;
                dots.Sort((a, b) => Math.Abs((vertical ? a.Y : a.X) - center).CompareTo(Math.Abs((vertical ? b.Y : b.X) - center)));
                if (dots.Count > count) dots.RemoveRange(count, dots.Count - count);
                dots.Sort((a, b) => (vertical ? a.Y : a.X).CompareTo(vertical ? b.Y : b.X));
                if (dots.Count == 0) return;
                const string layer = "ETIKET CIZGISI (BEYKENT)";
                EnsureKotLayer(tr, ms.Database, layer);
                var offset = new Vector3d(dx, dy, 0);
                Point3d first = dots[0] + offset, last = dots[dots.Count - 1] + offset;
                void LineTo(Point3d a, Point3d b)
                {
                    if (a.DistanceTo(b) < NamedDotTolerance) return;
                    var line = new Line(a, b) { Layer = layer, ColorIndex = 256 };
                    ms.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
                }
                LineTo(first, last);
                foreach (Point3d dot in dots) LineTo(dot, dot + offset);
                double x = (first.X + last.X) / 2, y = (first.Y + last.Y) / 2;
                text.Rotation = 0;
                if (vertical) SeatBoxOffEdge(text, ms.Database, x, y, Math.Sign(dx), 0, 4);
                else if (dy < 0) SeatMaxY(text, ms.Database, x, y - 4);
                else SeatMinY(text, ms.Database, x, y + 4);
            }

'''+s[b:]
# Readable segment dimensions: align text with the actual segment before seating its extents.
s=s.replace('                    SeatBoxOffEdge(tx, ms.Database, mx2, my2, nx, ny, 8.0);','''                    double angle = Math.Atan2(gy, gx);
                    if (angle > Math.PI / 2) angle -= Math.PI;
                    if (angle <= -Math.PI / 2) angle += Math.PI;
                    tx.Rotation = angle;
                    SeatBoxOffEdge(tx, ms.Database, mx2, my2, nx, ny, 8.0);''')
s=s.replace('new Point3d(vx + 14.0, vmy, 0));','new Point3d(vx + 14.0, vmy, 0), true);')
s=s.replace('double segLen, Point3d at)\n','double segLen, Point3d at, bool vertical = false)\n')
s=s.replace('                MoveTextCenter(best, ms.Database, at);','''                best.Rotation = vertical ? Math.PI / 2 : 0;
                if (vertical) SeatBoxOffEdge(best, ms.Database, at.X - 14, at.Y, 1, 0, 8);
                else MoveTextCenter(best, ms.Database, at);''')
p.write_text(s,encoding='utf-8-sig')
