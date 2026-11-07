namespace StatikRaporClaude.Uretec;

public static class ModGraf
{
    /// CQC: 1. satır mod sayısı n; n satır (T, baskın yön 1=X 2=Y 4=burulma, Mx%, My%, Mz%); n satır mod vektörü (Dx[K], Dy[K], Rz[K]).
    public static List<string> Uret(Proj p, int adet)
    {
        var c = p.Lines(".CQC"); int n = (int)Proj.D(c[0]);
        int K = (int)Proj.D(p.Lines(".EAR")[0].Split(',')[0]);
        var yon = Enumerable.Range(0, n).Select(i => (int)Proj.D(c[1 + i].Split(',')[1])).ToArray();
        var vek = Enumerable.Range(0, n).Select(i => c[1 + n + i].Split(',').Select(Proj.D).ToArray()).ToArray();
        var o = new List<string> { "MODAL ANALİZ MOD GRAFİĞİ (1000 x Dep. vektörü)" };
        foreach (var (ad, y, off) in new[] { ("X yönü", 1, 0), ("Y yönü", 2, K) })
        {
            o.Add(ad);
            var ms = Enumerable.Range(0, n).Where(i => yon[i] == y).Take(Math.Min(adet, K)).ToList();
            for (int j = 0; j < ms.Count; j++)
            {
                o.Add($" {j + 1}. MOD");
                for (int k = 0; k < K; k++) o.Add(Fmt.N(vek[ms[j]][off + k] * 100, 1));
            }
        }
        return o;
    }
}
