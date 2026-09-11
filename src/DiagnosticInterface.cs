using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BlockMook {
internal static class DiagnosticSummary {
    internal static string Build(int mask,ProbeResult[] values,bool otherConnection){
        var text=new StringBuilder();text.AppendLine("BlockMook "+Updates.CurrentVersion+" · проверка соединений");
        if(otherConnection)text.AppendLine("Активен другой VPN или сетевой компонент. Эти результаты не подтверждают работу BlockMook отдельно.");
        text.AppendLine(Probes.ControlOk(values)?"Контрольный адрес интернета ответил.":"Контрольный адрес не ответил. Это может быть обрыв сети или недоступность самого адреса.");
        foreach(var service in Services.Selected(mask)){
            var result=values.FirstOrDefault(v=>v.Index==service.Index);
            text.AppendLine(service.Name+": "+(result==null?"не проверен":result.Ok?(service.Index>=3?"TLS подтверждён":"проверки соединения пройдены"):"соединение не подтверждено"));
        }
        text.AppendLine();
        text.AppendLine(!Probes.ControlOk(values)?"Что сделать: проверь другие сайты и подключение Windows к сети.":Probes.AllSelected(values,mask)?"Что сделать: открой сервис и проверь нужное действие — видео, сообщение или звонок.":"Что сделать: попробуй автоматический подбор. Если проблема остаётся — запусти проверку профилей ниже.");
        text.AppendLine("Вход в аккаунт, сообщения и звонки автоматически не проверяются.");
        return text.ToString();
    }
}
internal sealed partial class MainWindow {
    private CancellationTokenSource quickCancellation;
    private string shareableReport;
    private void SetupDiagnostics(){
        Find<Button>("QuickDiagnostic").Click+=async(s,e)=>await Guard(QuickDiagnostic);
        Find<Button>("CancelQuick").Click+=(s,e)=>{if(quickCancellation!=null)quickCancellation.Cancel();};
        Find<Button>("ExportDiagnostic").Click+=(s,e)=>{
            if(shareableReport==null)return;
            if(isSmoke)return;
            var dialog=new SaveFileDialog{Title="Сохранить показанный отчёт",FileName="BlockMook-diagnostic-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".txt",Filter="Текстовый отчёт (*.txt)|*.txt"};
            try{if(dialog.ShowDialog(Window)==true)File.WriteAllText(dialog.FileName,shareableReport,new UTF8Encoding(true));}catch(Exception ex){Find<TextBlock>("DiagnosticAdvice").Text="Не удалось сохранить отчёт: "+ex.Message;}
        };
    }
    private async Task QuickDiagnostic(){
        using(var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)){
            quickCancellation=cancellation;Find<Button>("CancelQuick").Visibility=Visibility.Visible;Controls();
            try{await Check();}
            catch(OperationCanceledException){Find<TextBlock>("DiagnosticAdvice").Text="Проверка отменена. Подключение не менялось.";}
            finally{quickCancellation=null;Find<Button>("CancelQuick").Visibility=Visibility.Collapsed;}
        }
    }
    private void PaintDiagnostic(ProbeResult[] values,bool otherConnection){
        shareableReport="Время: "+DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")+"\n"+DiagnosticSummary.Build(Mask,values,otherConnection);
        Find<TextBox>("DiagnosticPreview").Text=shareableReport;
        Find<TextBlock>("DiagnosticAdvice").Text=Probes.AllSelected(values,Mask)?"Соединения подтверждены. Проверь работу самих приложений.":"Есть неподтверждённые соединения. Рекомендации — в отчёте ниже.";
        Find<Button>("ExportDiagnostic").IsEnabled=true;
    }
}
}
