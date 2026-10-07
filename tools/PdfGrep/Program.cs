using System;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;

class Program
{
    static void Main(string[] args)
    {
        string path = args[0];
        string[] keys = args.Skip(1).ToArray();
        Console.OutputEncoding = Encoding.UTF8;
        using (var doc = PdfDocument.Open(path))
        {
            int n = 0;
            foreach (var page in doc.GetPages())
            {
                n++;
                string t = page.Text ?? "";
                bool hit = keys.Length == 0;
                foreach (var k in keys)
                {
                    if (t.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                }
                if (!hit) continue;
                Console.WriteLine("===== PAGE " + n + " =====");
                Console.WriteLine(t.Replace("\r", " "));
                Console.WriteLine();
            }
        }
    }
}
