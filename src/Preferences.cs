using System;
using System.IO;

namespace BlockMook {
internal sealed class Preferences {
    internal int Services=3;
    internal int ManualProfile=-1;
    internal bool WelcomeDone,ReduceMotion;
    internal static string DefaultPath {get{return Path.Combine(Core.DataRoot,"interface-v1.txt");}}
    internal static Preferences LoadUser() {
        return Load(File.Exists(DefaultPath)?DefaultPath:Path.Combine(Core.LegacyDataRoot,"interface-v1.txt"));
    }
    internal static Preferences Parse(string text) {
        var value=new Preferences();
        foreach(string line in text.Split('\n')) {
            var pair=line.Trim().Split('=');if(pair.Length!=2)continue;
            int mask;bool flag;
            if(pair[0]=="services"&&Int32.TryParse(pair[1],out mask)&&mask>=1&&mask<=3)value.Services=mask;
            if(pair[0]=="profile"&&Int32.TryParse(pair[1],out mask)&&mask>=-1&&mask<Core.Names.Length)value.ManualProfile=mask;
            if(pair[0]=="welcome"&&Boolean.TryParse(pair[1],out flag))value.WelcomeDone=flag;
            if(pair[0]=="reduceMotion"&&Boolean.TryParse(pair[1],out flag))value.ReduceMotion=flag;
        }
        return value;
    }
    internal static Preferences Load(string path) {
        try{return Parse(File.ReadAllText(path));}catch(IOException){return new Preferences();}catch(UnauthorizedAccessException){return new Preferences();}
    }
    internal void Save(string path) {
        Core.Domains(Services);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary=path+".tmp";
        File.WriteAllText(temporary,"services="+Services+"\nwelcome="+WelcomeDone+"\nreduceMotion="+ReduceMotion+"\nprofile="+ManualProfile+"\n");
        if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
    }
}
}
