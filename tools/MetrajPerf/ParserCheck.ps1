param([string]$SourcePath = 'ST4_Yardimci/DonatiMetrajRunner.cs', [string]$ClassName = 'ParserBefore')
$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Resolve-Path $SourcePath))
function Extract([string]$name) {
    $match = [regex]::Match($source, '(?m)^        private static [^\r\n]+\b' + $name + '\(')
    if (!$match.Success) { throw "Missing $name" }
    $start = $match.Index
    $open = $source.IndexOf('{', $start); $depth = 1; $end = $open + 1
    while ($depth -gt 0) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    $source.Substring($start, $end - $start)
}
$members = @('NormalizeDonatiPlain','TryParse','TryParseAdetCap','TryAdetProduct','PhiAlt','AddPhi','IsPhi') | ForEach-Object { Extract $_ }
$code = @'
using System;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
static class DonatiMetrajSettings { public static string FiIsareti = "ø"; }
public static class CLASSNAME {
MEMBERS
public static void Run() {
 string[] values = {"2x26ø8/9 L=220 MONTAJ", "ø8 L=138", "4Ø16 L = 321,5", "3x2x14ø12 L=600", "2ø16 (mon)", "2ø16 DÜZ MONTAJ DONATISI 1 2 3 4 5 6 7 8 9", "BENZER KATLAR 1 2 3 4 5 6 7", "12345678901234567890", "Q188/188 500x215", "7%%c14 L=300", "2ø16 MALZEME BİLGİSİ UZUN ETİKET L=510", "2ø16 L=0"};
 foreach(string s in values) { int a,c,b; double raw; bool ok=TryParse(s,out a,out c,out b,out raw); Console.WriteLine(s+" => "+ok+"/"+a+"/"+c+"/"+b+"/"+raw.ToString(CultureInfo.InvariantCulture)); }
 for(int sample=0;sample<3;sample++) {
  var sw=Stopwatch.StartNew(); long sum=0;
  for(int i=0;i<24000;i++) {int a,c,b; double raw; if(TryParse(values[i%values.Length],out a,out c,out b,out raw))sum+=a+b+c;}
  Console.WriteLine("BENCH_MS="+sw.ElapsedMilliseconds+" CHECKSUM="+sum);
 }
}
}
'@
$code = $code.Replace('CLASSNAME', $ClassName).Replace('MEMBERS', ($members -join "`n"))
Add-Type -TypeDefinition $code
([type]$ClassName)::Run()
