using System.Text;

namespace StatikRaporClaude.Uretec;

/// <summary>Üreteçlerin kodlama ve program bilgileri (GPR okumaz).</summary>
public static class GprKod
{
    public static readonly Encoding Enc;
    static GprKod() { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); Enc = Encoding.GetEncoding(1254); }
}
