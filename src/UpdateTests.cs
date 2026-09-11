using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BlockMook {
internal static partial class Tests {
    private static bool Rejected(Action action) {try{action();return false;}catch(InvalidDataException){return true;}}
    private static string ReleaseFixture() {
        return "{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v1.2.3\",\"body\":\"TEST FIXTURE release notes\",\"assets\":[{\"name\":\""+Updates.AssetName+"\",\"state\":\"uploaded\",\"size\":123,\"digest\":\"sha256:"+new string('a',64)+"\",\"browser_download_url\":\""+Updates.RepoUrl+"/releases/download/v1.2.3/"+Updates.AssetName+"\"}]}";
    }
    private static void TestUpdates(string folder) {
        string fixture=ReleaseFixture();var info=Updates.Parse(fixture);
        Check("Release reads version, notes and exact Windows archive",info.Version==new Version(1,2,3)&&info.Size==123&&info.Notes.StartsWith("TEST FIXTURE"));
        Check("Versions compared numerically",Updates.ReadVersion("v1.10.0")>Updates.ReadVersion("1.9.9"));
        foreach(string invalid in new[]{"1","v1.2.3-beta","../1.2.3","v01.2.3","999999999999.1.0","1.2.3.4"})Check("Reject invalid release version "+invalid,Rejected(()=>Updates.ReadVersion(invalid)));
        var current=Updates.Parse(fixture.Replace("v1.2.3","v1.0"));
        Check("Public 1.0 tag normalizes version and retains exact download path",current.Version==new Version(1,0,0)&&current.Tag=="v1.0"&&current.Url.EndsWith("/v1.0/"+Updates.AssetName));
        Check("Future 1.1 is newer than consolidated 1.0",Updates.ReadVersion("v1.1")>Updates.Installed);
        foreach(var pair in new[]{
            new[]{"\"draft\":false","\"draft\":true"},new[]{"\"prerelease\":false","\"prerelease\":true"},
            new[]{Updates.RepoUrl,"https://github.com/foreign/BlockMook"},new[]{"https://github.com/","http://github.com/"},
            new[]{"sha256:"+new string('a',64),"sha256:bad"},new[]{"\"size\":123","\"size\":0"},
            new[]{"\"size\":123","\"size\":104857601"},new[]{"\"state\":\"uploaded\"","\"state\":\"new\""},
            new[]{Updates.AssetName,"../BlockMook.exe"}})
            Check("Reject unsafe release field "+pair[1],Rejected(()=>Updates.Parse(fixture.Replace(pair[0],pair[1]))));
        string asset=fixture.Substring(fixture.IndexOf("[{",StringComparison.Ordinal)+1);asset=asset.Substring(0,asset.Length-2);
        Check("Reject ambiguous duplicate archives",Rejected(()=>Updates.Parse(fixture.Replace("["+asset+"]","["+asset+","+asset+"]"))));
        foreach(string url in new[]{"http://github.com/file","https://github.com.evil.test/file","https://user@github.com/file","https://github.com:444/file","file:///C:/file","https://example.com/file"})
            Check("Reject unsafe redirect "+url,!Updates.AllowedRedirect(new Uri(url)));
        Check("Allow GitHub release CDN over HTTPS",Updates.AllowedRedirect(new Uri("https://release-assets.githubusercontent.com/file?token=public-download")));
        byte[] data=Encoding.UTF8.GetBytes("TEST FIXTURE - archive bytes, never executed");string hash;
        using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(data)).Replace("-","");
        using(var input=new MemoryStream(data))using(var output=new MemoryStream()) {
            Updates.CopyVerified(input,output,data.Length,hash,null,CancellationToken.None).GetAwaiter().GetResult();
            Check("Verified archive bytes preserved exactly",output.ToArray().SequenceEqual(data));
        }
        foreach(string mode in new[]{"digest","short","oversized"}) {
            using(var input=new MemoryStream(data))using(var output=new MemoryStream())
                Check("Reject download "+mode,Rejected(()=>Updates.CopyVerified(input,output,mode=="short"?data.Length+1:mode=="oversized"?data.Length-1:data.Length,mode=="digest"?new string('0',64):hash,null,CancellationToken.None).GetAwaiter().GetResult()));
        }
        using(var cancellation=new CancellationTokenSource())using(var input=new MemoryStream(data))using(var output=new MemoryStream()) {
            cancellation.Cancel();bool cancelled=false;
            try{Updates.CopyVerified(input,output,data.Length,hash,null,cancellation.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Check("Cancellation prevents a completed download",cancelled&&output.Length==0);
        }
        string first,second;
        using(var input=new MemoryStream(data))first=Updates.SaveDownload(input,folder,data.Length,hash,null,CancellationToken.None).GetAwaiter().GetResult();
        using(var input=new MemoryStream(data))second=Updates.SaveDownload(input,folder,data.Length,hash,null,CancellationToken.None).GetAwaiter().GetResult();
        Check("Repeat downloads preserve previous verified archives",first!=second&&File.ReadAllBytes(first).SequenceEqual(data)&&File.ReadAllBytes(second).SequenceEqual(data));
        int count=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Length;
        using(var input=new MemoryStream(data))Check("Bad download digest rejected before exposing result",Rejected(()=>Updates.SaveDownload(input,folder,data.Length,new string('0',64),null,CancellationToken.None).GetAwaiter().GetResult()));
        Check("Failed archive removed without touching good downloads",Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Length==count&&File.ReadAllBytes(first).SequenceEqual(data));
        using(var cancellation=new CancellationTokenSource())using(var input=new MemoryStream(data)){
            cancellation.Cancel();bool cancelled=false;
            try{Updates.SaveDownload(input,folder,data.Length,hash,null,cancellation.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Check("Cancelled file download leaves no archive",cancelled&&Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Length==count);
        }
    }
}
}
