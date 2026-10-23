using System;
using System.Collections.Generic;
namespace Autodesk.AutoCAD.ApplicationServices
{
    public class SystemVariableChangedEventArgs : EventArgs { public string Name; }
    public class Editor { public bool IsQuiescent; public void WriteMessage(string text) { } }
    public class Document
    {
        public Editor Editor = new Editor();
        public List<string> Commands = new List<string>();
        public void SendStringToExecute(string text, bool a, bool b, bool c) { Commands.Add(text); }
    }
    public class Documents { public Document MdiActiveDocument; }
    public static class Application
    {
        public static Documents DocumentManager = new Documents();
        public static event EventHandler Idle;
        public static event EventHandler<SystemVariableChangedEventArgs> SystemVariableChanged;
        public static void Tick() { Idle?.Invoke(null, EventArgs.Empty); }
        public static void Workspace() { SystemVariableChanged?.Invoke(null, new SystemVariableChangedEventArgs { Name = "WSCURRENT" }); }
        public static int IdleCount => Idle?.GetInvocationList().Length ?? 0;
    }
}
