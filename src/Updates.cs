using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BlockMook {
internal sealed class ReleaseInfo {
    internal Version Version;
    internal string Notes,Url,Digest;
    internal long Size;
}

internal static class Updates {
    internal const string CurrentVersion="1.0.0";
    internal const string Repository="BABYSHKABIKE/BlockMook";
    internal const string AssetName="BlockMook-Windows-x64.zip";
    internal const long MaxDownload=100*1024*1024;
    internal const string RepoUrl="https://github.com/"+Repository;
    internal static readonly Version Installed=new Version(CurrentVersion);

    internal static Version ReadVersion(string text) {
        Version result;
        if(text==null || !Regex.IsMatch(text,@"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") || !Version.TryParse(text.TrimStart('v'),out result))
            throw new InvalidDataException("Релиз содержит неизвестный формат версии.");
        return result;
    }

    private static string StringField(IDictionary<string,object> obj,string name) {
        object value;return obj.TryGetValue(name,out value)?value as string:null;
    }

    internal static ReleaseInfo Parse(string json) {
        if(json==null || json.Length>1024*1024)throw new InvalidDataException("Ответ GitHub слишком большой.");
        var root=new JavaScriptSerializer {MaxJsonLength=1024*1024,RecursionLimit=32}.DeserializeObject(json) as IDictionary<string,object>;
        if(root==null)throw new InvalidDataException("GitHub вернул некорректный ответ.");
        object draft,preview,assets;
        if(!root.TryGetValue("draft",out draft)||!(draft is bool)||!root.TryGetValue("prerelease",out preview)||!(preview is bool)||(bool)draft||(bool)preview)
            throw new InvalidDataException("Доступны только стабильные опубликованные версии.");
        var version=ReadVersion(StringField(root,"tag_name"));
        if(!root.TryGetValue("assets",out assets)||!(assets is IEnumerable))throw new InvalidDataException("У релиза нет файлов.");
        ReleaseInfo result=null;
        foreach(object item in (IEnumerable)assets) {
            var asset=item as IDictionary<string,object>;
            if(asset==null||StringField(asset,"name")!=AssetName)continue;
            if(result!=null)throw new InvalidDataException("У релиза несколько архивов с одним именем.");
            string url=StringField(asset,"browser_download_url"),digest=StringField(asset,"digest");
            string expected=RepoUrl+"/releases/download/v"+version.ToString(3)+"/"+AssetName;
            if(url!=expected)throw new InvalidDataException("Адрес загрузки не принадлежит релизу BlockMook.");
            if(StringField(asset,"state")!="uploaded"||digest==null||!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("У архива пока нет подтверждённой контрольной суммы. Попробуй позже.");
            object sizeValue;long size;
            if(!asset.TryGetValue("size",out sizeValue)||!Int64.TryParse(Convert.ToString(sizeValue,System.Globalization.CultureInfo.InvariantCulture),out size)||size<=0||size>MaxDownload)
                throw new InvalidDataException("Недопустимый размер обновления.");
            string notes=StringField(root,"body")??"Описание изменений отсутствует.";
            result=new ReleaseInfo {Version=version,Url=url,Digest=digest.Substring(7),Size=size,Notes=notes.Length>16000?notes.Substring(0,16000)+"…":notes};
        }
        if(result==null)throw new InvalidDataException("Архив для Windows ещё не опубликован.");
        return result;
    }

    private static HttpClient Client() {
        var client=new HttpClient(new HttpClientHandler {AllowAutoRedirect=false,UseCookies=false}) {Timeout=Timeout.InfiniteTimeSpan};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("BlockMook/"+CurrentVersion);
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
        return client;
    }

    internal static async Task<ReleaseInfo> Latest(CancellationToken token) {
        using(var client=Client())
        using(var response=await client.GetAsync("https://api.github.com/repos/"+Repository+"/releases/latest",HttpCompletionOption.ResponseHeadersRead,token)) {
            if(response.StatusCode==HttpStatusCode.NotFound)return null;
            if((int)response.StatusCode==403||(int)response.StatusCode==429)throw new IOException("GitHub ограничил частоту запросов. Попробуй позже.");
            response.EnsureSuccessStatusCode();
            using(var stream=await response.Content.ReadAsStreamAsync())
            using(var buffer=new MemoryStream()) {
                var bytes=new byte[8192];int count;
                while((count=await stream.ReadAsync(bytes,0,bytes.Length,token))>0){if(buffer.Length+count>1024*1024)throw new InvalidDataException("Ответ GitHub слишком большой.");await buffer.WriteAsync(bytes,0,count,token);}
                return Parse(Encoding.UTF8.GetString(buffer.ToArray()));
            }
        }
    }

    internal static bool AllowedRedirect(Uri uri) {
        return uri!=null&&uri.IsAbsoluteUri&&uri.Scheme=="https"&&uri.IsDefaultPort&&String.IsNullOrEmpty(uri.UserInfo)&&
            (uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com");
    }

    internal static async Task CopyVerified(Stream input,Stream output,long expectedSize,string digest,IProgress<int> progress,CancellationToken token) {
        if(expectedSize<=0||expectedSize>MaxDownload||digest==null||!Regex.IsMatch(digest,@"^[a-fA-F0-9]{64}$"))throw new InvalidDataException("Некорректные параметры загрузки.");
        using(var hash=SHA256.Create()) {
            var bytes=new byte[65536];long total=0;int count,last=-1;
            while((count=await input.ReadAsync(bytes,0,bytes.Length,token))>0) {
                token.ThrowIfCancellationRequested();total+=count;
                if(total>expectedSize)throw new InvalidDataException("Архив больше размера, указанного GitHub.");
                hash.TransformBlock(bytes,0,count,bytes,0);await output.WriteAsync(bytes,0,count,token);
                int percent=(int)(total*100/expectedSize);if(percent!=last&&progress!=null){progress.Report(percent);last=percent;}
            }
            token.ThrowIfCancellationRequested();hash.TransformFinalBlock(new byte[0],0,0);
            if(total!=expectedSize)throw new InvalidDataException("Загрузка оборвалась. Попробуй скачать ещё раз.");
            string actual=BitConverter.ToString(hash.Hash).Replace("-","");
            if(!String.Equals(actual,digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Контрольная сумма не совпала. Архив не сохранён.");
        }
    }

    internal static async Task<string> Download(ReleaseInfo release,IProgress<int> progress,CancellationToken token) {
        string expected=RepoUrl+"/releases/download/v"+release.Version.ToString(3)+"/"+AssetName;
        if(release.Url!=expected)throw new InvalidDataException("Неизвестный источник обновления.");
        string folder=Path.Combine(Core.DataRoot,"Updates",release.Version.ToString(3));Directory.CreateDirectory(folder);
        string partial=Path.Combine(folder,Guid.NewGuid().ToString("N")+".partial"),final=Path.Combine(folder,AssetName);
        try {
            using(var client=Client()) {
                var url=new Uri(release.Url);
                for(int redirects=0;redirects<=5;redirects++) {
                    if(!AllowedRedirect(url))throw new InvalidDataException("GitHub перенаправил загрузку на неизвестный сервер.");
                    using(var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token)) {
                        int code=(int)response.StatusCode;
                        if(code==301||code==302||code==303||code==307||code==308) {
                            if(response.Headers.Location==null)throw new InvalidDataException("Нет адреса загрузки.");
                            url=new Uri(url,response.Headers.Location);continue;
                        }
                        response.EnsureSuccessStatusCode();
                        using(var input=await response.Content.ReadAsStreamAsync())
                        using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                            await CopyVerified(input,output,release.Size,release.Digest,progress,token);
                        token.ThrowIfCancellationRequested();
                        if(File.Exists(final))File.Replace(partial,final,null);else File.Move(partial,final);
                        return final;
                    }
                }
                throw new IOException("Слишком много перенаправлений при загрузке.");
            }
        } finally {if(File.Exists(partial))File.Delete(partial);}
    }
}
}
