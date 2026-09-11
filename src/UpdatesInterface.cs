using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BlockMook {
internal sealed partial class MainWindow {
    private ReleaseInfo availableRelease;
    private CancellationTokenSource updateCancellation;
    private string downloadedUpdate;
    private bool closeAfterUpdate;
    private void SetupUpdates() {
        Find<TextBlock>("AppVersion").Text=Updates.CurrentVersion;
        Find<TextBlock>("UpdateTitle").Text="BlockMook "+Updates.CurrentVersion;
        Find<Button>("CheckUpdates").Click+=async(s,e)=>await UpdateOperation(false);
        Find<Button>("DownloadUpdate").Click+=async(s,e)=>await UpdateOperation(true);
        Find<Button>("CancelUpdate").Click+=(s,e)=>{if(updateCancellation!=null)updateCancellation.Cancel();};
        Find<Button>("OpenUpdate").Click+=(s,e)=>{try{if(downloadedUpdate==null||!File.Exists(downloadedUpdate))throw new IOException("Архив перемещён. Скачай его ещё раз.");Process.Start("explorer.exe","/select,"+Core.Q(downloadedUpdate));}catch(Exception ex){Find<TextBlock>("UpdateStatus").Text=ex.Message;}};
        Link("ReleasePage",Updates.RepoUrl+"/releases");Link("SourcePage",Updates.RepoUrl);
        Link("CreditZapret","https://github.com/bol-van/zapret");Link("CreditFlowseal","https://github.com/Flowseal/zapret-discord-youtube");
        Link("CreditWinDivert","https://github.com/basil00/WinDivert");Link("CreditCygwin","https://cygwin.com/");
    }
    private void Link(string control,string url) {Find<Button>(control).Click+=(s,e)=>{try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){Write("Не удалось открыть ссылку: "+ex.Message);MessageBox.Show(Window,"Не удалось открыть браузер. Адрес: "+url,"BlockMook",MessageBoxButton.OK,MessageBoxImage.Information);}};}
    private async Task UpdateOperation(bool download) {
        if(updateCancellation!=null)return;
        var status=Find<TextBlock>("UpdateStatus");
        using(var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(download?180:20))) {
            updateCancellation=cancellation;
            Find<Button>("CheckUpdates").IsEnabled=false;Find<Button>("DownloadUpdate").IsEnabled=false;
            Find<Button>("CancelUpdate").Visibility=Visibility.Visible;
            try {
                if(download) {
                    if(availableRelease==null)throw new InvalidOperationException("Сначала проверь обновления.");
                    Find<ProgressBar>("UpdateProgress").Value=0;Find<ProgressBar>("UpdateProgress").Visibility=Visibility.Visible;
                    status.Text="Загружаем архив…";
                    downloadedUpdate=await Updates.Download(availableRelease,new Progress<int>(p=>{if(updateCancellation==cancellation){Find<ProgressBar>("UpdateProgress").Value=p;status.Text=p==100?"Проверяем SHA-256…":"Загружаем · "+p+" %";}}),cancellation.Token);
                    status.Text="Архив готов. Контрольная сумма проверена.";
                    Find<Button>("OpenUpdate").Visibility=Visibility.Visible;
                } else {
                    availableRelease=null;Find<Button>("DownloadUpdate").Visibility=Visibility.Collapsed;status.Text="Проверяем GitHub…";
                    availableRelease=await Updates.Latest(cancellation.Token);
                    if(availableRelease==null){status.Text="Публичных релизов пока нет.";return;}
                    bool newer=availableRelease.Version>Updates.Installed;
                    status.Text=newer?"Доступна версия "+availableRelease.Version.ToString(3):"У тебя актуальная версия.";
                    Find<TextBlock>("ReleaseNotes").Text=availableRelease.Notes;
                    Find<Button>("DownloadUpdate").Content=newer?"Скачать "+availableRelease.Version.ToString(3):"Скачать эту версию";
                    Find<Button>("DownloadUpdate").Visibility=availableRelease.Version>=Updates.Installed?Visibility.Visible:Visibility.Collapsed;
                }
            } catch(OperationCanceledException){status.Text="Операция отменена или время ожидания истекло. Можно повторить.";}
            catch(Exception ex){status.Text=ex is HttpRequestException?"Не удалось связаться с GitHub. Проверь интернет и попробуй ещё раз.":ex.Message;Write("Обновления: "+ex.Message);}
            finally {
                updateCancellation=null;Find<Button>("CheckUpdates").IsEnabled=true;Find<Button>("DownloadUpdate").IsEnabled=true;
                Find<Button>("CancelUpdate").Visibility=Visibility.Collapsed;Find<ProgressBar>("UpdateProgress").Visibility=Visibility.Collapsed;
                if(closeAfterUpdate)Window.Close();
            }
        }
    }
}
}
