using System;
using System.IO;
namespace BlockMook {
internal sealed class NetworkOptions {
    internal bool Megabytes;
    internal string Target="1.1.1.1",Adapter="";
    internal static string PathName {get{return Path.Combine(Core.DataRoot,"network-v1.txt");}}
    internal static NetworkOptions Parse(string text){
        var p=new NetworkOptions();foreach(string line in text.Split('\n')){
            var pair=line.Trim().Split(new[]{'='},2);if(pair.Length!=2)continue;string k=pair[0],v=pair[1];bool b;
            if(k=="megabytes"&&Boolean.TryParse(v,out b))p.Megabytes=b;
            if(k=="target"&&NetworkMonitor.ValidTarget(v))p.Target=v;
            if(k=="adapter")p.Adapter=v;
        }return p;
    }
    internal string Serialize(){return "megabytes="+Megabytes+"\ntarget="+Target+"\nadapter="+Adapter+"\n";}
    internal static NetworkOptions Load(){return Load(PathName,Path.Combine(Core.DataRoot,"overlay-v1.txt"));}
    internal static NetworkOptions Load(string path,string legacyPath){
        // Import only measurement settings. Old window/hotkey settings have no effect.
        string text=SettingsStorage.Read(path);return Parse(text.Length==0?SettingsStorage.Read(legacyPath):text);
    }
    internal void Save(){SettingsStorage.Write(PathName,Serialize());}
}
}
