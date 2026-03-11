using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

// ST4AKS komutu sadece ST4_Plan_ID_Ciz icinden kaydedilir; bu DLL referans olarak yuklendiginde
// cift komut gorunmesin diye burada CommandClass/CommandMethod kullanilmiyor.
[assembly: ExtensionApplication(typeof(ST4AksCizCSharp.PluginLifecycle))]

namespace ST4AksCizCSharp
{
    public class PluginLifecycle : IExtensionApplication
    {
        public void Initialize() { }
        public void Terminate() { }
    }
}
