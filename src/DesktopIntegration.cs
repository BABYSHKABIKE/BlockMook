using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BlockMook {
internal static class DesktopIntegration {
    private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string Executable {get{return Assembly.GetExecutingAssembly().Location;}}
    internal static string StartupCommand(string executable){
        if(!Path.IsPathRooted(executable)||!String.Equals(Path.GetExtension(executable),".exe",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Нужен полный путь к приложению.");
        string command=Core.Q(executable)+" --startup";
        if(command.Length>260)throw new ArgumentException("Для автозапуска перемести приложение в папку с более коротким путём.");
        return command;
    }
    internal static bool StartupEnabled(){using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null&&String.Equals(key.GetValue("BlockMook") as string,StartupCommand(Executable),StringComparison.OrdinalIgnoreCase);}
    internal static void SetStartup(bool enabled){
        using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){
            string command=StartupCommand(Executable),existing=key.GetValue("BlockMook") as string;
            if(enabled)key.SetValue("BlockMook",command,RegistryValueKind.String);
            else if(String.Equals(existing,command,StringComparison.OrdinalIgnoreCase))key.DeleteValue("BlockMook",false);
        }
    }
    internal static string CreateShortcut(string directory){
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"BlockMook.lnk");
        object shell=null,shortcut=null;
        try{
            var type=Type.GetTypeFromProgID("WScript.Shell",true);shell=Activator.CreateInstance(type);
            // Late binding avoids any COM/Office build dependency.
            shortcut=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});
            var shortcutType=shortcut.GetType();
            if(File.Exists(path)){
                string target=shortcutType.InvokeMember("TargetPath",BindingFlags.GetProperty,null,shortcut,null) as string;
                if(!String.Equals(target,Executable,StringComparison.OrdinalIgnoreCase))throw new IOException("Ярлык BlockMook уже ведёт в другую папку. Сохрани его или переименуй перед созданием нового.");
            }
            shortcutType.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{Executable});
            shortcutType.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{Path.GetDirectoryName(Executable)});
            shortcutType.InvokeMember("IconLocation",BindingFlags.SetProperty,null,shortcut,new object[]{Executable+",0"});
            shortcutType.InvokeMember("Description",BindingFlags.SetProperty,null,shortcut,new object[]{"BlockMook — управление подключением"});
            shortcutType.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
            return path;
        }finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}
    }
}
}
