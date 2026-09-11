using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BlockMook {
internal static partial class Tests {
    private static void TestServices(string folder){
        Check("Catalog service bits and probe indices unique",Services.Items.Select(s=>s.Bit).Distinct().Count()==5&&Services.Items.Select(s=>s.Index).Distinct().Count()==5);
        Check("Legacy preferences preserve selection",Preferences.Parse("services=3").Services==3);
        Check("Meet search is case insensitive",Services.Search("  MEET ").Single().Bit==16&&Services.Search("мит").Single().Bit==16);
        Check("Empty catalog search",Services.Search("unlisted").Length==0);
        foreach(int mask in Enumerable.Range(1,31)){
            var selected=Services.Selected(mask);var indices=Services.ProbeIndices(mask);
            var values=indices.Select(i=>new ProbeResult{Index=i,Ok=true}).Reverse().ToArray();
            Check("Selected probes plus control only "+mask,indices.Length==selected.Length+1&&indices.Contains(2)&&Probes.AllSelected(values,mask));
            Check("Missing control cannot verify "+mask,!Probes.AllSelected(values.Where(r=>r.Index!=2).ToArray(),mask));
            var first=selected[0].Index;
            Check("Duplicate result cannot hide missing service "+mask,!Probes.AllSelected(values.Where(r=>r.Index!=first).Concat(values.Where(r=>r.Index!=first)).ToArray(),mask));
            string path=Path.Combine(folder,"services.txt");new Preferences{Services=mask}.Save(path);
            Check("Service selection round trip "+mask,Preferences.Load(path).Services==mask);
        }
        Check("Meet includes TLS media and narrow web domains",Core.Domains(16).Contains("meet.turns.goog")&&Core.Domains(16).Contains("workspace.turns.goog")&&!Core.Domains(16).Contains("google.com"));
        Check("Signal excludes unrelated Meet and Viber",Core.Domains(4).Contains("signal.org")&&!Core.Domains(4).Any(d=>d.Contains("google")||d.Contains("viber")));
        Check("Meet UDP rule requires Google media networks and STUN",Core.Arguments(1,16,"hosts").Contains("--filter-udp=443,3478,19302-19309 --ipset-ip="+Services.MeetMedia+" --filter-l7=stun"));
        Check("Legacy strategy excludes Meet rules",!Core.Arguments(1,3,"hosts").Contains("3478")&&!Core.Arguments(1,3,"hosts").Contains("--ipset-ip"));
        var request=new byte[20];request[1]=1;request[4]=0x21;request[5]=0x12;request[6]=0xA4;request[7]=0x42;
        for(int i=8;i<20;i++)request[i]=(byte)i;
        var response=new byte[32];Array.Copy(request,response,20);response[0]=1;response[3]=12;response[21]=0x20;response[23]=8;response[25]=1;
        Check("Matching STUN success with mapped address accepted",ServiceChecks.ValidStun(request,response));
        response[8]^=1;Check("Foreign STUN transaction rejected",!ServiceChecks.ValidStun(request,response));response[8]^=1;
        Check("Truncated STUN rejected",!ServiceChecks.ValidStun(request,response.Take(31).ToArray()));
        response[21]=9;Check("No mapped address rejected",!ServiceChecks.ValidStun(request,response));response[21]=0x20;
        response[23]=24;Check("Malformed STUN attribute rejected",!ServiceChecks.ValidStun(request,response));response[23]=8;
        response[0]=0;Check("STUN request cannot pass as response",!ServiceChecks.ValidStun(request,response));
        bool cancelled=false;try{ServiceChecks.Run(5,new CancellationToken(true)).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
        Check("Cancelled service probe starts no network work",cancelled);
        TestSignalTrust();
        TestSparseConnection().GetAwaiter().GetResult();
    }
    private static void TestSignalTrust(){
        using(var official=SignalTrust.Root())Check("Pinned Signal CA is embedded and unexpired",official.NotAfter.ToUniversalTime()>DateTime.UtcNow);
        using(var key=RSA.Create()){
            var request=new CertificateRequest("CN=BlockMook test root",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign,true));
            var now=DateTimeOffset.UtcNow;
            using(var root=request.CreateSelfSigned(now.AddDays(-10),now.AddDays(10))){
                var leafRequest=new CertificateRequest("CN=chat.signal.org",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
                leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,true));
                leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new Oid("1.3.6.1.5.5.7.3.1")},false));
                using(var leaf=leafRequest.Create(root,now.AddDays(-1),now.AddDays(1),new byte[]{1,2,3,4})){
                    Check("Custom CA validates signed server chain",SignalTrust.ValidateChain(leaf,null,SslPolicyErrors.RemoteCertificateChainErrors,root));
                    Check("Hostname mismatch rejected even with valid CA",!SignalTrust.ValidateChain(leaf,null,SslPolicyErrors.RemoteCertificateNameMismatch,root));
                    using(var official=SignalTrust.Root())Check("Foreign CA cannot impersonate Signal",!SignalTrust.ValidateChain(leaf,null,SslPolicyErrors.RemoteCertificateChainErrors,official));
                }
                using(var expired=leafRequest.Create(root,now.AddDays(-4),now.AddDays(-2),new byte[]{4,3,2,1}))Check("Expired server certificate rejected",!SignalTrust.ValidateChain(expired,null,SslPolicyErrors.RemoteCertificateChainErrors,root));
                Check("CA cannot be used as a server leaf",!SignalTrust.ValidateChain(root,null,SslPolicyErrors.None,root));
                Check("Missing certificate rejected",!SignalTrust.ValidateChain(null,null,SslPolicyErrors.None,root));
            }
        }
    }
    private static async Task TestSparseConnection(){
        int starts=0,aborts=0;
        var values=new[]{new ProbeResult{Index=5,Ok=true},new ProbeResult{Index=2,Ok=true},new ProbeResult{Index=3,Ok=false}};
        var result=await ConnectionFlow.Run(1,20,false,profile=>{starts++;return Task.FromResult(0);},()=>Task.FromResult(0),()=>aborts++,()=>Task.FromResult(values),message=>{},(profile,sample)=>{},CancellationToken.None);
        Check("Sparse reordered probes retain partial Meet access",result.Running&&!result.Verified&&starts==4&&aborts==0);
    }
}
}
