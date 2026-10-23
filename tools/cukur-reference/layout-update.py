from pathlib import Path
p=Path('ST4_Yardimci/TemelCekuruRunner.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('                    bool gonyeTop = (g0.Y + g1.Y) * 0.5 > vmy;','                    bool gonyeTop = (g0.Y + g1.Y) * 0.5 > vmy;')
s=s.replace('(h0.Y + h1.Y) * 0.5 + (hookBottom ? -14.0 : 14.0), 0));','(h0.Y + h1.Y) * 0.5, 0), normalY: hookBottom ? -1 : 1);')
s=s.replace('(g0.Y + g1.Y) * 0.5 + (gonyeTop ? -14.0 : 14.0), 0));','(g0.Y + g1.Y) * 0.5, 0), normalY: gonyeTop ? 1 : -1);')
s=s.replace('Point3d at, bool vertical = false)','Point3d at, bool vertical = false, int normalY = 0)')
s=s.replace('                else MoveTextCenter(best, ms.Database, at);','                else if (normalY > 0) SeatMinY(best, ms.Database, at.X, at.Y + 8);\n                else if (normalY < 0) SeatMaxY(best, ms.Database, at.X, at.Y - 8);\n                else MoveTextCenter(best, ms.Database, at);')
a=s.index('            private void PlaceYatayUstLabels(');b=s.index('            /// <summary>\n            /// Yan açılımda',a)
t=s[a:b]
t=t.replace('                    if (HooksUp(pl)) continue;\n','')
t=t.replace('                var gonye = new List<DBText>();','                var gonye = new List<DBText>();\n                var lengths = new List<DBText>();')
t=t.replace('                    if (s.IndexOf("ÜST", StringComparison.Ordinal) >= 0\n                        && s.IndexOf("L=", StringComparison.Ordinal) >= 0\n                        && s.IndexOf("DİKEY", StringComparison.Ordinal) < 0)','                    if (IsYatayAcilim(s, out _) && s.IndexOf("DİKEY", StringComparison.Ordinal) < 0)')
t=t.replace('                    if (ParseCm(tx) == gCm) gonye.Add(tx);','                    if (ParseCm(tx) == gCm) gonye.Add(tx);\n                    if (ParseCm(tx) > 0) lengths.Add(tx);')
t=t.replace('out double x1, out _)) continue;\n                    double midX', 'out double x1, out double mainLength)) continue;\n                    bool upper = !HooksUp(pl);\n                    double midX')
t=t.replace('                        if (used.Contains(tx)) continue;\n                        if (tx.Position.X < x0', '                        if (used.Contains(tx)) continue;\n                        if (!IsYatayAcilim(tx.TextString, out bool isUpper) || isUpper != upper) continue;\n                        if (tx.Position.X < x0')
t=t.replace('                        SeatMaxY(call, ms.Database, midX, hy - 8.0);','                        SeatMinY(call, ms.Database, midX, hy + 8.0);')
t=t.replace('                    int n = pl.NumberOfVertices;','''                    DBText length = null;
                    double lengthDistance = double.MaxValue;
                    foreach (DBText tx in lengths)
                    {
                        if (used.Contains(tx) || Math.Abs(ParseCm(tx) * DuPerCm - mainLength) > 12) continue;
                        double d = Math.Abs(tx.Position.X - midX) + Math.Abs(tx.Position.Y - hy);
                        if (d < lengthDistance) { lengthDistance = d; length = tx; }
                    }
                    if (length != null)
                    {
                        used.Add(length);
                        SeatMaxY(length, ms.Database, midX, hy - 8);
                    }
                    int n = pl.NumberOfVertices;''',1)
t=t.replace('inward: 1.0);','inward: upper ? -1.0 : 1.0);').replace('inward: -1.0);','inward: upper ? -1.0 : 1.0);')
t=t.replace('                    if (tx.Position.X <= x0 + 12.0 || tx.Position.X >= x1 - 12.0) continue;\n','')
t=t.replace('                best.Rotation = 0;','                best.Rotation = Math.PI / 2;')
s=s[:a]+t+s[b:];p.write_text(s,encoding='utf-8-sig')
