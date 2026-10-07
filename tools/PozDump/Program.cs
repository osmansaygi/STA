using System;
using System.IO;
using System.Text;
using UglyToad.PdfPig;

class Program
{
    static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : @"C:\Users\BEYKENT\Documents\CURSOR\Osman_donusum\tools\eylul_bf.pdf";
        var sb = new StringBuilder();
        using (var doc = PdfDocument.Open(path))
        {
            foreach (var page in doc.GetPages())
                sb.Append(page.Text).Append('\n');
        }
        string text = sb.ToString();
        string[] keys = { "15.150.1005", "15.150.1006", "15.180.1003" };
        foreach (string key in keys)
        {
            int i = text.IndexOf(key, StringComparison.Ordinal);
            Console.WriteLine("==== " + key + " idx=" + i);
            if (i >= 0)
                Console.WriteLine(text.Substring(i, Math.Min(700, text.Length - i)).Replace('\n', '|'));
        }
    }
}
