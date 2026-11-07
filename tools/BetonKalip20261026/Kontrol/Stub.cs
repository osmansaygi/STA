namespace ST4Yardimci {
internal static class RadyeOtomatikSource {
  internal static byte[] ReadShared(string path) { using var i = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var o = new MemoryStream(); i.CopyTo(o); return o.ToArray(); }
  internal static string ReadRadText(string path) => System.Text.Encoding.GetEncoding(1254).GetString(ReadShared(path));
} }
