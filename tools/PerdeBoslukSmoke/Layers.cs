using Autodesk.AutoCAD.DatabaseServices;
namespace ST4Yardimci {
 internal static class BeykentKatmanYukle {
  internal static void Ensure(Transaction tr, Database db, string name) {
   var table=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
   if(table.Has(name))return;
   table.UpgradeOpen();var layer=new LayerTableRecord{Name=name};
   table.Add(layer);tr.AddNewlyCreatedDBObject(layer,true);
  }
 }
}
