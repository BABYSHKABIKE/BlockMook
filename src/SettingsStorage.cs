using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace BlockMook {
// Two small checksummed copies recover an interrupted write without renaming EFS files.
internal static class SettingsStorage {
    private const string Header="BLOCKMOOK-SETTINGS-SHA256 ";
    internal static string Wrap(string content){using(var sha=SHA256.Create())return Header+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(content))).Replace("-","")+"\n"+content;}
    internal static string Decode(string value){
        if(value==null||!value.StartsWith(Header,StringComparison.Ordinal))return null;
        int newline=value.IndexOf('\n');if(newline<0)return null;string content=value.Substring(newline+1);
        return String.Equals(Wrap(content),value,StringComparison.Ordinal)?content:null;
    }
    private static string ReadFile(string path){try{return File.ReadAllText(path);}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}}
    internal static string Read(string path){
        string pending=ReadFile(path+".tmp"),latest=Decode(pending);if(latest!=null)return latest;
        string main=ReadFile(path),verified=Decode(main);if(verified!=null)return verified;
        return main!=null&&!main.StartsWith("BLOCKMOOK-",StringComparison.Ordinal)?main:"";
    }
    internal static void Write(string path,string content){
        Directory.CreateDirectory(Path.GetDirectoryName(path));byte[] bytes=Encoding.UTF8.GetBytes(Wrap(content));
        foreach(string target in new[]{path+".tmp",path})using(var stream=new FileStream(target,FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)){
            stream.Write(bytes,0,bytes.Length);stream.SetLength(bytes.Length);stream.Flush(true);
        }
    }
}
}
