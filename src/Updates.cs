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

[assembly:System.Reflection.AssemblyInformationalVersion(BlockMook.Updates.CurrentVersion+"+build."+BlockMook.Updates.CurrentBuild)]

namespace BlockMook {
internal enum UpdateAvailability { NewVersion, NewBuild, Current, Older, Unknown }

internal sealed class ReleaseInfo {
    internal Version Version;
    internal string Notes,Url,Digest,Tag;
    internal long Size;
    internal int? BuildNumber;
}

internal static class Updates {
    internal const string CurrentVersion="1.0.0";
    internal const string DisplayVersion="1.0";
    // Increment for each distinct published build, independently of the product version.
    internal const string CurrentBuild="1";
    internal static readonly bool DevelopmentBuild=false;
    internal const string Repository="BABYSHKABIKE/BlockMook";
    internal const string AssetName="BlockMook-Windows-x64.zip";
    internal const long MaxDownload=100*1024*1024;
    internal const string RepoUrl="https://github.com/"+Repository;
    internal static readonly Version Installed=new Version(CurrentVersion);
    internal static readonly int InstalledBuild=Int32.Parse(CurrentBuild,System.Globalization.CultureInfo.InvariantCulture);

    internal static UpdateAvailability Availability(ReleaseInfo release,Version installed,int installedBuild) {
        if(release.Version>installed)return UpdateAvailability.NewVersion;
        if(release.Version<installed)return UpdateAvailability.Older;
        if(!release.BuildNumber.HasValue)return UpdateAvailability.Unknown;
        if(release.BuildNumber.Value>installedBuild)return UpdateAvailability.NewBuild;
        return release.BuildNumber.Value==installedBuild?UpdateAvailability.Current:UpdateAvailability.Older;
    }

    internal static bool CanDownload(UpdateAvailability availability) {
        return availability==UpdateAvailability.NewVersion||availability==UpdateAvailability.NewBuild||availability==UpdateAvailability.Current;
    }

    internal static string AvailabilityText(UpdateAvailability availability,ReleaseInfo release) {
        switch(availability) {
            case UpdateAvailability.NewVersion:return "Доступна версия "+release.Version.ToString(3)+".";
            case UpdateAvailability.NewBuild:return "Доступно обновление установленной версии.";
            case UpdateAvailability.Current:return "Установлена актуальная сборка.";
            case UpdateAvailability.Older:return "Установленная сборка новее опубликованной.";
            default:return "У релиза нет сведений о сборке. Сравнение недоступно.";
        }
    }

    private static int? ReadBuild(ref string notes,string digest) {
        var prefixes=Regex.Matches(notes,@"<!--\s*blockmook-build\b",RegexOptions.CultureInvariant);
        if(prefixes.Count==0)return null;
        var markers=Regex.Matches(notes,@"<!--\s*blockmook-build:\s*([1-9][0-9]{0,9});\s*sha256:\s*([a-fA-F0-9]{64})\s*-->",RegexOptions.CultureInvariant);
        int build;
        if(prefixes.Count!=1||markers.Count!=1||prefixes[0].Index!=markers[0].Index||
            !Int32.TryParse(markers[0].Groups[1].Value,out build))
            throw new InvalidDataException("Сведения о сборке в релизе некорректны.");
        if(!String.Equals(markers[0].Groups[2].Value,digest,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Сведения о сборке не соответствуют архиву. Повторите проверку после завершения публикации.");
        notes=notes.Remove(markers[0].Index,markers[0].Length).Trim();
        return build;
    }

    internal static Version ReadVersion(string text) {
        Version result;
        if(text==null || !Regex.IsMatch(text,@"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*))?$") || !Version.TryParse(text.TrimStart('v'),out result))
            throw new InvalidDataException("Релиз содержит неизвестный формат версии.");
        return new Version(result.Major,result.Minor,Math.Max(0,result.Build));
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
        string tag=StringField(root,"tag_name");var version=ReadVersion(tag);
        if(!root.TryGetValue("assets",out assets)||!(assets is IEnumerable))throw new InvalidDataException("У релиза нет файлов.");
        ReleaseInfo result=null;
        foreach(object item in (IEnumerable)assets) {
            var asset=item as IDictionary<string,object>;
            if(asset==null||StringField(asset,"name")!=AssetName)continue;
            if(result!=null)throw new InvalidDataException("У релиза несколько архивов с одним именем.");
            string url=StringField(asset,"browser_download_url"),digest=StringField(asset,"digest");
            string expected=RepoUrl+"/releases/download/"+tag+"/"+AssetName;
            if(url!=expected)throw new InvalidDataException("Адрес загрузки не принадлежит релизу BlockMook.");
            if(StringField(asset,"state")!="uploaded"||digest==null||!Regex.IsMatch(digest,@"^sha256:[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("У архива пока нет подтверждённой контрольной суммы. Повторите попытку позже.");
            object sizeValue;long size;
            if(!asset.TryGetValue("size",out sizeValue)||!Int64.TryParse(Convert.ToString(sizeValue,System.Globalization.CultureInfo.InvariantCulture),out size)||size<=0||size>MaxDownload)
                throw new InvalidDataException("Недопустимый размер обновления.");
            string notes=StringField(root,"body")??"Описание изменений отсутствует.";
            int? build=ReadBuild(ref notes,digest.Substring(7));
            result=new ReleaseInfo {Version=version,Tag=tag,Url=url,Digest=digest.Substring(7),Size=size,BuildNumber=build,Notes=notes.Length>16000?notes.Substring(0,16000)+"…":notes};
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
            if((int)response.StatusCode==403||(int)response.StatusCode==429)throw new IOException("GitHub ограничил частоту запросов. Повторите попытку позже.");
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
            if(total!=expectedSize)throw new InvalidDataException("Загрузка оборвалась. Повторите загрузку.");
            string actual=BitConverter.ToString(hash.Hash).Replace("-","");
            if(!String.Equals(actual,digest,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Контрольная сумма не совпала. Архив не сохранён.");
        }
    }

    internal static async Task<string> SaveDownload(Stream input,string folder,long size,string digest,IProgress<int> progress,CancellationToken token) {
        token.ThrowIfCancellationRequested();
        string destination=Path.Combine(folder,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(destination);
        string final=Path.Combine(destination,AssetName);bool created=false,complete=false;
        try {
            // A unique file needs no rename/replace, which can fail in EFS folders.
            using(var output=new FileStream(final,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true)) {
                created=true;await CopyVerified(input,output,size,digest,progress,token);output.Flush(true);
            }
            token.ThrowIfCancellationRequested();complete=true;return final;
        } finally {if(created&&!complete)File.Delete(final);}
    }

    internal static async Task<string> Download(ReleaseInfo release,IProgress<int> progress,CancellationToken token) {
        if(!CanDownload(Availability(release,Installed,InstalledBuild)))
            throw new InvalidDataException("Опубликованная сборка старее установленной либо её номер неизвестен. Загрузка из приложения недоступна.");
        if(ReadVersion(release.Tag)!=release.Version)throw new InvalidDataException("Версия и тег обновления не совпадают.");
        string expected=RepoUrl+"/releases/download/"+release.Tag+"/"+AssetName;
        if(release.Url!=expected)throw new InvalidDataException("Неизвестный источник обновления.");
        string folder=Path.Combine(Core.DataRoot,"Updates",release.Version.ToString(3));
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
                        return await SaveDownload(input,folder,release.Size,release.Digest,progress,token);
                }
            }
            throw new IOException("Слишком много перенаправлений при загрузке.");
        }
    }
}
}
