from pathlib import Path
p=Path('ST4_Yardimci/TemelCekuruRunner.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('altTx, PhysicalDot(8), PhysicalDot(15), AltAdet','altTx, PhysicalDot(7), PhysicalDot(16), AltAdet')
s=s.replace('                    // The bottom row also contains the two side extensions.\n                    if (!vertical && dy > 0) { lo = PhysicalDot(8).X; hi = PhysicalDot(15).X; }\n','')
s=s.replace('                    if (!dots.Exists(p => p.DistanceTo(dot) < NamedDotTolerance)) dots.Add(dot);','''                    // Corner dots have their own bar identities; the vertical group counts exclude them.
                    if (vertical)
                    {
                        bool corner = false;
                        for (int number = 1; number <= 16; number++)
                            if (PhysicalDot(number).DistanceTo(dot) < NamedDotTolerance) { corner = true; break; }
                        if (corner) continue;
                    }
                    if (!dots.Exists(p => p.DistanceTo(dot) < NamedDotTolerance)) dots.Add(dot);''')
p.write_text(s,encoding='utf-8-sig')
