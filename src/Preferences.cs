using System;
using System.IO;

namespace BlockMook {
internal sealed class Preferences {
    internal int Services=3;
    internal int ManualProfile=-1;
    internal bool WelcomeDone,ReduceMotion;
    internal bool CloseToTray=true,AutoRecover=true,ReconnectOnChange=true,Notifications=true;
    internal bool AutoConnect,TrayExplained;
    internal static string DefaultPath {get{return Path.Combine(Core.DataRoot,"interface-v1.txt");}}
    internal static Preferences LoadUser() {
        return LoadUser(DefaultPath,Path.Combine(Core.LegacyDataRoot,"interface-v1.txt"));
    }
    internal static Preferences LoadUser(string path,string legacyPath) {
        string text=SettingsStorage.Read(path);return Parse(text.Length==0?SettingsStorage.Read(legacyPath):text);
    }
    internal static Preferences Parse(string text) {
        var value=new Preferences();
        foreach(string line in text.Split('\n')) {
            var pair=line.Trim().Split('=');if(pair.Length!=2)continue;
            int mask;bool flag;
            if(pair[0]=="services"&&Int32.TryParse(pair[1],out mask)&&BlockMook.Services.ValidMask(mask))value.Services=mask;
            if(pair[0]=="profile"&&Int32.TryParse(pair[1],out mask)&&mask>=-1&&mask<Core.Names.Length)value.ManualProfile=mask;
            if(pair[0]=="welcome"&&Boolean.TryParse(pair[1],out flag))value.WelcomeDone=flag;
            if(pair[0]=="reduceMotion"&&Boolean.TryParse(pair[1],out flag))value.ReduceMotion=flag;
            if(pair[0]=="closeToTray"&&Boolean.TryParse(pair[1],out flag))value.CloseToTray=flag;
            if(pair[0]=="autoRecover"&&Boolean.TryParse(pair[1],out flag))value.AutoRecover=flag;
            if(pair[0]=="reconnectOnChange"&&Boolean.TryParse(pair[1],out flag))value.ReconnectOnChange=flag;
            if(pair[0]=="notifications"&&Boolean.TryParse(pair[1],out flag))value.Notifications=flag;
            if(pair[0]=="autoConnect"&&Boolean.TryParse(pair[1],out flag))value.AutoConnect=flag;
            if(pair[0]=="trayExplained"&&Boolean.TryParse(pair[1],out flag))value.TrayExplained=flag;
        }
        return value;
    }
    internal static Preferences Load(string path) {
        return Parse(SettingsStorage.Read(path));
    }
    internal void Save(string path) {
        Core.Domains(Services);
        SettingsStorage.Write(path,"services="+Services+"\nwelcome="+WelcomeDone+"\nreduceMotion="+ReduceMotion+"\nprofile="+ManualProfile+"\ncloseToTray="+CloseToTray+"\nautoRecover="+AutoRecover+"\nreconnectOnChange="+ReconnectOnChange+"\nnotifications="+Notifications+"\nautoConnect="+AutoConnect+"\ntrayExplained="+TrayExplained+"\n");
    }
}
}
