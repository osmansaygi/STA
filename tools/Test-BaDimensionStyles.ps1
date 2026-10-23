# Run the production style policy against an in-memory AutoCAD API fixture.
# The actual plugin build separately checks compatibility with the AutoCAD SDK.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $root 'ST4_Yardimci/BaDimensionStyles.cs'))
$settings = [IO.File]::ReadAllText((Join-Path $root 'Ortak/BeykentStil/BeykentStilAyar.cs'))
$shared = [IO.File]::ReadAllText((Join-Path $root 'Ortak/BeykentStil/BeykentStilUygula.cs'))
function Extract-SharedMethod([string]$name) {
    $match = [regex]::Match($shared, '(?m)^\s*internal static [^\r\n]+\b' + $name + '\(')
    if (!$match.Success) { throw "Shared style method missing: $name" }
    $start=$match.Index
    $open=$shared.IndexOf('{',$start); $depth=1; $end=$open+1
    while($depth -gt 0) { if($shared[$end] -eq '{'){$depth++}; if($shared[$end] -eq '}'){$depth--}; $end++ }
    $shared.Substring($start,$end-$start)
}
$styleMethods = (@('YaziOzellikYaz','YaziStili','OlcuStiliYapilandir','OlcuStiliYaz') | ForEach-Object { Extract-SharedMethod $_ }) -join "`n"
$sharedSubset = "namespace BeykentOrtak { internal static class BeykentStilUygula {`n$styleMethods`n} }"
$usings = ([regex]::Matches(($source+"`n"+$settings+"`n"+$shared), '(?m)^using [^\r\n]+;') | ForEach-Object Value | Sort-Object -Unique) -join "`n"
$source = $usings + "`n" + [regex]::Replace($source+"`n"+$settings,'(?m)^using [^\r\n]+;\r?\n','') + "`n" + $sharedSubset
$objectIds = 'Dimtxsty','Dimblk','Dimblk1','Dimblk2','Dimldrblk','Dimltype','Dimltex1','Dimltex2'
$booleans = 'Dimsah','DimfxlenOn','Dimse1','Dimse2','Dimsd1','Dimsd2','Dimtih','Dimtoh','Dimtofl','Dimtix','Dimsoxd','Dimalt','Dimtol','Dimlim'
$shorts = 'Dimtad','Dimjust','Dimatfit','Dimtmove','Dimdec','Dimtdec','Dimzin','Dimlunit','Dimaunit','Dimadec','Dimtfill'
$colors = 'Dimclrd','Dimclre','Dimclrt'
$properties = foreach ($name in ([regex]::Matches($source, 'record\.(Dim\w+|Annotative)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)) {
    $type = if ($name -in $objectIds) { 'ObjectId' }
        elseif ($name -in $booleans) { 'bool' }
        elseif ($name -in $shorts) { 'short' }
        elseif ($name -in $colors) { 'Autodesk.AutoCAD.Colors.Color' }
        elseif ($name -in @('Dimlwd','Dimlwe')) { 'LineWeight' }
        elseif ($name -in @('Dimpost','Dimapost')) { 'string' }
        elseif ($name -eq 'Annotative') { 'AnnotativeStates' }
        else { 'double' }
    "public $type $name { get; set; }"
}
$fixture = @'
namespace Autodesk.AutoCAD.Colors {
 public enum ColorMethod {ByAci,ByLayer}
 public class Color {
  public short ColorIndex; public ColorMethod Method;
  public static Color FromColorIndex(ColorMethod method,short index){return new Color{ColorIndex=index,Method=method};}
 }
}
namespace Autodesk.AutoCAD.GraphicsInterface {
 public struct FontDescriptor {
  public string Name;
  public FontDescriptor(string name,bool bold,bool italic,int charset,int pitch){Name=name;}
 }
}
namespace Autodesk.AutoCAD.DatabaseServices {
 public enum OpenMode {ForRead,ForWrite}
 public enum LineWeight {ByLayer}
 public enum AnnotativeStates {False,True}
 public struct ObjectId {
  public int Value;
  public static ObjectId Null => new ObjectId();
  public bool IsNull => Value==0;
  public bool IsValid => Value!=0;
  public bool IsErased => false;
 }
 public class DBObject {
  public string Name; public ObjectId ObjectId; public bool IsWriteEnabled;
  public void UpgradeOpen(){IsWriteEnabled=true;}
 }
 public class DimStyleTableRecord : DBObject {
'@ + ($properties -join "`n") + @'
 }
 public class TextStyleTableRecord : DBObject {
  public double TextSize,XScale,ObliquingAngle;
  public string FileName;
  public Autodesk.AutoCAD.GraphicsInterface.FontDescriptor Font;
 }
 public class SymbolTable<T> : DBObject where T:DBObject {
  readonly Database db;
  readonly System.Collections.Generic.Dictionary<string,ObjectId> ids = new System.Collections.Generic.Dictionary<string,ObjectId>(System.StringComparer.OrdinalIgnoreCase);
  public SymbolTable(Database owner){db=owner;}
  public bool Has(string name){return ids.ContainsKey(name);}
  public ObjectId this[string name] => ids[name];
  public ObjectId Add(T record){ObjectId id=db.Add(record);ids.Add(record.Name,id);return id;}
 }
 public class DimStyleTable : SymbolTable<DimStyleTableRecord> {public DimStyleTable(Database db):base(db){}}
 public class TextStyleTable : SymbolTable<TextStyleTableRecord> {public TextStyleTable(Database db):base(db){}}
 public class Database {
  public ObjectId DimStyleTableId,TextStyleTableId,ContinuousLinetype;
  public readonly System.Collections.Generic.Dictionary<int,DBObject> Objects = new System.Collections.Generic.Dictionary<int,DBObject>();
  static int serial;
  public Database(){DimStyleTableId=Add(new DimStyleTable(this));TextStyleTableId=Add(new TextStyleTable(this));ContinuousLinetype=Add(new DBObject());}
  public ObjectId Add(DBObject record){ObjectId id=new ObjectId{Value=++serial};record.ObjectId=id;Objects.Add(id.Value,record);return id;}
 }
 public class Transaction {
  readonly Database db;
  public Transaction(Database owner){db=owner;}
  public DBObject GetObject(ObjectId id,OpenMode mode){DBObject record=db.Objects[id.Value];if(mode==OpenMode.ForWrite)record.UpgradeOpen();return record;}
  public void AddNewlyCreatedDBObject(DBObject record,bool add){}
 }
 public class Dimension : DimStyleTableRecord {
  public ObjectId DimensionStyle;
  public Autodesk.AutoCAD.Colors.Color Color;
  public LineWeight LineWeight;
  public int CopyCount,RecomputeCount;
  public void SetDimstyleData(DimStyleTableRecord record){
   CopyCount++;
   foreach(var property in typeof(DimStyleTableRecord).GetProperties())property.SetValue(this,property.GetValue(record,null),null);
  }
  public void RecomputeDimensionBlock(bool force){RecomputeCount++;}
 }
}
namespace ST4Yardimci {
 public static class DimensionStylesRegression {
  static void Near(double actual,double expected,string label){if(System.Math.Abs(actual-expected)>0.000001)throw new System.Exception(label+": "+actual+" != "+expected);}
  static void Check(bool value,string label){if(!value)throw new System.Exception(label);}
  public static string Run(){
   // Pin actual production defaults in memory; do not read/write the user's saved settings.
   var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static;
   typeof(BeykentOrtak.BeykentStilAyar).GetField("_aktif",flags).SetValue(null,BeykentOrtak.BeykentStilAyar.Varsayilan());
   typeof(BeykentOrtak.BeykentStilAyar).GetField("_sonKontrol",flags).SetValue(null,System.DateTime.UtcNow.AddHours(1));
   var db=new Autodesk.AutoCAD.DatabaseServices.Database();
   var tr=new Autodesk.AutoCAD.DatabaseServices.Transaction(db);
   var table=(Autodesk.AutoCAD.DatabaseServices.DimStyleTable)tr.GetObject(db.DimStyleTableId,Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
   var poisoned=new Autodesk.AutoCAD.DatabaseServices.DimStyleTableRecord {
    Name="PLAN_OLCU",Dimtxt=99,Dimlfac=25,Dimscale=100,Dimtsz=0,Dimse1=true,Dimse2=true,DimfxlenOn=true,
    Dimdec=8,Dimrnd=0.01,Dimtad=0,Dimpost="wrong units",Dimalt=true,Dimtol=true,Dimlim=true,
    Dimblk=db.ContinuousLinetype,Dimblk1=db.ContinuousLinetype,Dimblk2=db.ContinuousLinetype
   };
   table.Add(poisoned);
   var id=BaDimensionStyles.Ensure(tr,db,"PLAN_OLCU");
   Check(id.Value==poisoned.ObjectId.Value,"repair existing style in place");
   Near(poisoned.Dimtxt,12,"plan text");Near(poisoned.Dimlfac,1,"plan factor");Near(poisoned.Dimscale,1,"plan scale");
   Near(poisoned.Dimtsz,3,"oblique tick");Near(poisoned.Dimexo,3,"extension offset");Near(poisoned.Dimexe,6,"minimum extension");
   Near(poisoned.Dimdec,1,"precision");Near(poisoned.Dimrnd,0.5,"half cm rounding");Near(poisoned.Dimzin,12,"zero suppression");
   Check(poisoned.Dimblk.IsNull&&poisoned.Dimblk1.IsNull&&poisoned.Dimblk2.IsNull,"clear stale arrow blocks");
   Check(!poisoned.Dimse1&&!poisoned.Dimse2&&!poisoned.DimfxlenOn,"clear stale extension suppression");
   Check(!poisoned.Dimalt&&!poisoned.Dimtol&&!poisoned.Dimlim&&poisoned.Dimpost=="","clear stale units and tolerances");
   Check(poisoned.Dimclrt.ColorIndex==7&&poisoned.Dimclrd.ColorIndex==256,"text and line colors");
   var dimension=new Autodesk.AutoCAD.DatabaseServices.Dimension{Dimtxt=400,Dimlfac=17,Dimscale=300,Dimtsz=0,Dimse1=true,Dimpost="old override"};
   BaDimensionStyles.Apply(tr,db,dimension,id);
   Near(dimension.Dimtxt,12,"reset entity text override");Near(dimension.Dimlfac,1,"reset entity measurement override");
   Check(dimension.CopyCount==1&&dimension.Dimpost==""&&!dimension.Dimse1,"apply full style to entity");
   var plan25=BaDimensionStyles.Ensure(tr,db,"PLAN_OLCU_25");
   BaDimensionStyles.Apply(tr,db,dimension,plan25);
   Near(dimension.Dimlfac,0.5,"1/25 factor");Near(dimension.Dimfxlen,5,"1/25 fixed extension");
   Check(dimension.DimfxlenOn,"1/25 fixed extension enabled");
   var rebar25=BaDimensionStyles.Ensure(tr,db,"PLAN_OLCU_DONATI 25");
   BaDimensionStyles.Apply(tr,db,dimension,rebar25);Near(dimension.Dimtxt,10,"rebar text");Near(dimension.Dimlfac,0.5,"rebar 1/25 factor");
   var arrow=BaDimensionStyles.Ensure(tr,db,"DONATI OKU (BEYKENT)");
   BaDimensionStyles.Apply(tr,db,dimension,arrow);Near(dimension.Dimasz,10,"closed filled arrow size");Near(dimension.Dimtsz,0,"no tick for rebar arrow");
   Check(dimension.Dimse1&&dimension.Dimse2&&dimension.Dimtad==0,"arrow suppresses extensions and centers text");
   BaDimensionStyles.Apply(tr,db,dimension,Autodesk.AutoCAD.DatabaseServices.ObjectId.Null);
   Near(dimension.Dimtxt,12,"null style fallback");Near(dimension.Dimtsz,3,"fallback restores ticks");
   poisoned.Dimtxt=700;
   var next=new Autodesk.AutoCAD.DatabaseServices.Transaction(db);
   BaDimensionStyles.Ensure(next,db,"PLAN_OLCU");Near(poisoned.Dimtxt,12,"next command repairs changed style");
   var custom=new Autodesk.AutoCAD.DatabaseServices.DimStyleTableRecord{Name="CUSTOM",Dimtxt=77,Dimlfac=2,Annotative=Autodesk.AutoCAD.DatabaseServices.AnnotativeStates.True};
   table.Add(custom);
   BaDimensionStyles.Apply(next,db,dimension,custom.ObjectId);Near(dimension.Dimtxt,77,"selected custom style preserved");Near(dimension.Dimlfac,2,"custom factor preserved");Check(dimension.Annotative==Autodesk.AutoCAD.DatabaseServices.AnnotativeStates.True,"custom annotation preserved");
   var empty=new Autodesk.AutoCAD.DatabaseServices.Database();var emptyTr=new Autodesk.AutoCAD.DatabaseServices.Transaction(empty);
   var fresh=BaDimensionStyles.Ensure(emptyTr,empty,"AKS_OLCU");BaDimensionStyles.Apply(emptyTr,empty,dimension,fresh);
   Near(dimension.Dimtxt,12,"empty drawing bootstrap");Near(dimension.Dimrnd,0.5,"fresh axes precision");
   return "PASS: fresh drawing; poisoned existing styles; stale entity overrides; plan/rebar/1:25/arrow profiles; command-to-command refresh; custom style and fallback.";
  }
 }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $fixture)
[ST4Yardimci.DimensionStylesRegression]::Run()

# Every measurement-producing command uses the policy directly or through the factory.
foreach ($file in @('OtoOlcuRunner.cs','OtoOlcuAksRunner.cs','PlanOlcuRunner.cs','MerdivenRunner.cs','TemelCekuruRunner.cs','TemelHatiliRunner.cs','KuranglezRunner.cs','OtoDonatiRunner.cs','HasirDonatiRunner.cs','SehpaDonatiRunner.cs','DonatiOkuRunner.cs','RadyeIlaveRunner.cs')) {
    $text = [IO.File]::ReadAllText((Join-Path $root "ST4_Yardimci/$file"))
    if (!$text.Contains('BaDimensionStyles.Ensure(')) { throw "Missing style integration: $file" }
}
Write-Output 'PASS: all 12 measurement command implementations share the policy (Temel Detayi uses the Temel Cukuru core).'
