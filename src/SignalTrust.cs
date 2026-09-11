using System;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BlockMook {
internal static class SignalTrust {
    internal static X509Certificate2 Root(){
        using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("signal-root.cer"))
        using(var bytes=new MemoryStream()){
            input.CopyTo(bytes);byte[] raw=bytes.ToArray();
            using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(raw)).Replace("-","")!="DDB0F92BB95C8D6FD202EA6E8CC5CCD182B544F8CD696F47D580659DDC9DF65A")throw new IOException("Сертификат Signal не совпадает с проверенной версией.");
            return new X509Certificate2(raw);
        }
    }
    internal static bool Validate(object sender,X509Certificate certificate,X509Chain supplied,SslPolicyErrors errors){
        using(var root=Root())return ValidateChain(certificate,supplied,errors,root);
    }
    // Used only by Signal's two fixed probe hosts. No certificate is installed into a system store.
    internal static bool ValidateChain(X509Certificate certificate,X509Chain supplied,SslPolicyErrors errors,X509Certificate2 root){
        if(certificate==null||(errors&~SslPolicyErrors.RemoteCertificateChainErrors)!=0)return false;
        using(var leaf=new X509Certificate2(certificate))
        using(var chain=new X509Chain()){
            if(leaf.Extensions.OfType<X509BasicConstraintsExtension>().Any(e=>e.CertificateAuthority))return false;
            chain.ChainPolicy.ExtraStore.Add(root);
            if(supplied!=null)foreach(var item in supplied.ChainElements)chain.ChainPolicy.ExtraStore.Add(item.Certificate);
            chain.ChainPolicy.VerificationFlags=X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.RevocationMode=X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            if(!chain.Build(leaf)||chain.ChainElements.Count<2)return false;
            if(chain.ChainStatus.Any(s=>s.Status!=X509ChainStatusFlags.NoError&&s.Status!=X509ChainStatusFlags.UntrustedRoot))return false;
            return chain.ChainElements[chain.ChainElements.Count-1].Certificate.RawData.SequenceEqual(root.RawData);
        }
    }
}
}
