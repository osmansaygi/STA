using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;

public class RadyeCleanupCheck
{
    [CommandMethod("RADYE_CLEANUP_CHECK")]
    public void Run()
    {
        string log = Environment.GetEnvironmentVariable("RADYE_RESULT");
        try
        {
            var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("RADYE_PLUGIN"));
            var commands = assembly.GetType("ST4Yardimci.CommandEntry", true).GetMethods()
                .SelectMany(method => method.GetCustomAttributesData())
                .Where(attribute => attribute.AttributeType.Name == "CommandMethodAttribute")
                .SelectMany(attribute => attribute.ConstructorArguments)
                .Select(argument => argument.Value as string).Where(value => value != null).ToArray();
            foreach (string name in new[] { "RADYETEMELPLANIOTOMATIK", "RADYEOTOMATIK" })
                if (commands.Contains(name)) throw new InvalidOperationException("Wrong module still registers " + name);
            foreach (string name in new[] { "RADYEILAVE", "RADYETEMEL", "DONATIMETRAJ", "HASIRDONATI" })
                if (!commands.Contains(name)) throw new InvalidOperationException("Existing command lost: " + name);
            var icons = assembly.GetType("ST4Yardimci.BaCommandIcons", true);
            var tools = (IEnumerable)icons.GetField("Tools", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            int count = 0;
            foreach (object tool in tools)
            {
                string command = (string)tool.GetType().GetField("Cmd").GetValue(tool);
                if (command == "RADYETEMELPLANIOTOMATIK") throw new InvalidOperationException("Wrong panel button remains");
                count++;
            }
            var bounds = (int[,])icons.GetField("MonolitBounds", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (count != bounds.GetLength(0)) throw new InvalidOperationException("Monolit atlas mapping shifted");
            File.WriteAllText(log, "PASS: wrong command aliases and panel button absent; existing radye/metraj/hasir commands preserved; Monolit tool/atlas count=" + count + Environment.NewLine + "COMPLETE");
        }
        catch (System.Exception exception) { File.WriteAllText(log, exception.ToString()); }
    }
}
