from pathlib import Path
p=Path('ST4_Yardimci/TemelCekuruRunner.cs');s=p.read_text(encoding='utf-8-sig');s=s.replace('// Corner dots have their own bar identities; the vertical group counts exclude them.', '// Inner groups exclude their junction; outer groups include the intermediate corner.')
s=s.replace('                            if (PhysicalDot(number).DistanceTo(dot) < NamedDotTolerance) { corner = true; break; }','''                            if (PhysicalDot(number).DistanceTo(dot) < NamedDotTolerance)
                            {
                                bool outerJunction = Math.Abs(dx) == 15 && (number == 5 || number == 12 || number == 14 || number == 3)
                                    && dot.DistanceTo(start) > NamedDotTolerance && dot.DistanceTo(end) > NamedDotTolerance;
                                corner = !outerJunction; break;
                            }''')
p.write_text(s,encoding='utf-8-sig')
