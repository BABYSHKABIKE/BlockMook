using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlockMook {
internal sealed class DiagnosticReport : IDisposable {
    private readonly FileStream file;
    private readonly StreamWriter writer;
    internal readonly string PathName;
    internal static string Folder {get{return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","reports"));}}
    internal DiagnosticReport(string folder):this(folder,false){}
    internal DiagnosticReport(string folder,bool connection) {
        Directory.CreateDirectory(folder);
        PathName=Path.Combine(folder,(connection?"connection-":"diagnostic-")+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".txt");
        file=new FileStream(PathName,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        writer=new StreamWriter(file,new UTF8Encoding(true));
        try {
            Add("BLOCKMOOK "+Updates.CurrentVersion+" · "+(connection?"ПОДКЛЮЧЕНИЕ":"АВТОНОМНАЯ ДИАГНОСТИКА"));
            Add("НАЧАТО. Пока нет строки ИТОГ, проверка не завершена.");
            Add("HTTPS, TLS, публичный WebSocket Hello и STUN для Meet. Вход в аккаунт, видео, голос и скорость не проверяются. Пароли, подписки VPN и история браузера не собираются.");
        } catch {Dispose();throw;}
    }
    internal void Add(string text) {writer.WriteLine(DateTimeOffset.Now.ToString("o")+" | "+text.Replace('\r',' ').Replace('\n',' '));writer.Flush();file.Flush(true);}
    internal void Sample(string stage,ProbeResult[] values,int mask) {
        Add("ЭТАП: "+stage);
        foreach(var value in values) {Add(Probes.Urls[value.Index]+" | "+(value.Ok?"PASS":"FAIL")+" | "+value.Detail);foreach(string check in value.Checks)Add("  "+check);}
        Add("Выбранных проверок соединения успешно: "+Probes.Score(values,mask));
    }
    public void Dispose(){try{writer.Dispose();}finally{file.Dispose();}}
}
internal sealed class DiagnosticOutcome {
    internal string Status;
    internal string Error;
    internal bool StopConfirmed;
}
internal static class Diagnostics {
    // Callbacks allow regression tests without starting a real packet filter.
    internal static async Task<DiagnosticOutcome> Run(DiagnosticReport report,int mask,Func<string> conflict,
        Func<Task> prepare,Func<Task<ProbeResult[]>> probe,Func<int,Task> start,Func<Task> stop,
        Action abort,CancellationToken token,Action<string> progress) {
        var outcome=new DiagnosticOutcome{Status="ЗАВЕРШЕНО"};
        Exception failure=null;
        try {
            Core.Domains(mask);
            report.Add("Выбрано: "+String.Join(", ",Core.Domains(mask)));
            CheckConflict(conflict,token);
            report.Add("Известных конфликтов до проверки нет.");
            progress("Проверка без обхода…");
            report.Sample("Без обхода",await probe(),mask);
            CheckConflict(conflict,token);
            progress("Подтвердите запрос Windows для проверки стратегий");
            await prepare();
            token.ThrowIfCancellationRequested();
            for(int i=0;i<Core.Names.Length;i++) {
                CheckConflict(conflict,token);
                progress("Автономная проверка "+(i+1)+" / 3 · "+Core.Names[i]);
                report.Add("Запуск: "+Core.Names[i]);
                Exception stageError=null;
                try {await start(i);token.ThrowIfCancellationRequested();report.Sample(Core.Names[i],await probe(),mask);}
                catch(Exception ex){stageError=ex;}
                await stop();
                report.Add("Профиль остановлен: "+Core.Names[i]);
                token.ThrowIfCancellationRequested();
                if(stageError!=null){if(stageError is OperationCanceledException)throw stageError;report.Add("ОШИБКА ПРОФИЛЯ: "+stageError.Message);}
            }
            CheckConflict(conflict,token);
            progress("Финальная проверка без обхода…");
            report.Sample("После остановки всех стратегий",await probe(),mask);
            token.ThrowIfCancellationRequested();
        } catch(Exception ex){failure=ex;outcome.Status=ex is OperationCanceledException?"ПРЕРВАНО":"ОШИБКА";outcome.Error=ex.Message;}
        // C# 5 cannot await inside finally. Failures converge on this cleanup.
        try {await stop();outcome.StopConfirmed=true;}
        catch(Exception ex){outcome.Status="ОШИБКА";outcome.Error=(outcome.Error??"")+" Остановка не подтверждена: "+ex.Message;}
        finally {abort();}
        if(failure!=null)report.Add("ПРИЧИНА: "+failure.Message);
        report.Add(outcome.StopConfirmed?"Остановка собственного движка подтверждена.":"Канал к движку закрыт; штатная остановка не подтверждена.");
        report.Add("ИТОГ: "+outcome.Status+(outcome.Error==null?"":" | "+outcome.Error));
        return outcome;
    }
    private static void CheckConflict(Func<string> conflict,CancellationToken token){token.ThrowIfCancellationRequested();string value=conflict();if(value!=null)throw new InvalidOperationException(value);}
}
}
