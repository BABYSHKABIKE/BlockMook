using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace BlockMook {
internal sealed partial class MainWindow {
    private readonly RecoveryPolicy recovery=new RecoveryPolicy();
    private bool recovering,healthChecking,lastConnectionHealthy;
    private CancellationTokenSource healthCancellation;
    private void ConnectionEnvironmentChanged(string message){
        if(!preferences.ReconnectOnChange||!recovery.Wanted||recovery.Paused||exitRequested)return;
        RecordNetworkEvent(message);recovery.Changed(DateTime.UtcNow);Find<System.Windows.Controls.TextBlock>("RecoveryStatus").Text=message;
    }
    private async Task UserToggle(){
        if(exitRequested)return;
        if(running||recovery.Wanted||connectionCancellation!=null){
            recovery.Stop();userStopRequested=true;
            if(connectionCancellation!=null)connectionCancellation.Cancel();if(healthCancellation!=null)healthCancellation.Cancel();
            if(!busy){userStopRequested=false;await Guard(Stop);}
        }else await Guard(async()=>{
            await ConnectAutomatically();
            if(running)recovery.Start(DateTime.UtcNow,lastProvenMask);
        });
        Controls();
    }
    private int lastProvenMask;
    private void RecoveryPaused(string reason){
        bool first=!recovery.Paused;recovery.Paused=true;
        Find<System.Windows.Controls.TextBlock>("RecoveryStatus").Text=reason;
        if(first){Write(reason);NotifyFailure(reason);}
    }
    private async Task MonitorConnection(){
        if(exitRequested)return;
        if(running){
            try{if(await bridge.Send("STATUS")!="RUNNING"){running=false;state.Text="Соединение прервано";}}
            catch(Exception ex){bridge.Dispose();running=false;Write(ex.Message);}
        }
        if(!recovery.Wanted||!preferences.AutoRecover||recovery.Paused){if(!running)UpdateEnvironment();return;}
        if(!bridge.Connected){RecoveryPaused("Нужно заново подтвердить запуск сетевого компонента. Нажми «Отключить», затем «Подключить».");return;}
        DateTime now=DateTime.UtcNow;if(now<recovery.NextCheck)return;
        if(!NetworkInterface.GetIsNetworkAvailable()){recovery.NextCheck=now.AddSeconds(30);Find<System.Windows.Controls.TextBlock>("RecoveryStatus").Text="Ждём подключения компьютера к интернету.";return;}
        string conflict=await bridge.Send("CONFLICT");
        if(conflict!="CLEAR"){
            // Only stop our own filter when another network application takes over.
            if(running)await Stop();
            recovery.NextCheck=now.AddSeconds(30);Find<System.Windows.Controls.TextBlock>("RecoveryStatus").Text="Восстановление ожидает отключения другого VPN или другой сетевой инструмент.";return;
        }
        if(running){
            healthChecking=true;Controls();
            using(var cancellation=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)){
                healthCancellation=cancellation;
                try{
                    var values=await Probes.All(recovery.ProvenMask,cancellation.Token);
                    if(!recovery.Wanted||exitRequested||!preferences.AutoRecover)return;
                    if(!Probes.ControlOk(values)){recovery.NextCheck=now.AddSeconds(30);Find<System.Windows.Controls.TextBlock>("RecoveryStatus").Text="Контрольный адрес интернета не ответил. Перезапуск отложен.";return;}
                    recovery.Observe(now,Probes.AllSelected(values,recovery.ProvenMask));
                }catch(OperationCanceledException){if(!userStopRequested&&!exitRequested)throw;return;}
                finally{healthCancellation=null;healthChecking=false;}
            }
        }else recovery.Observe(now,false);
        Find<System.Windows.Controls.TextBlock>("RecoveryStatus").Text=recovery.Failures==0?"Соединение проверено · "+DateTime.Now.ToString("HH:mm"):"Неудачных проверок подряд: "+recovery.Failures+" из 3";
        if(!preferences.AutoRecover||!recovery.Wanted||exitRequested)return;
        if(!recovery.BeginAttempt(now)){
            if(recovery.Paused)NotifyFailure("Лимит восстановления исчерпан. Открой диагностику или подключись вручную.");
            return;
        }
        recovering=true;Controls();
        try{
            RecordNetworkEvent("Автовосстановление · попытка "+recovery.Attempts+" из 3");Write("Автовосстановление · попытка "+recovery.Attempts+" из 3");
            await Stop();if(!recovery.Wanted||exitRequested)return;
            await ConnectAutomatically();
            recovery.Finished(DateTime.UtcNow,running&&lastConnectionHealthy);RecordNetworkEvent(running&&lastConnectionHealthy?"Соединение восстановлено":"Восстановление не подтверждено");
            Find<System.Windows.Controls.TextBlock>("RecoveryStatus").Text=running&&lastConnectionHealthy?"Соединение восстановлено · "+DateTime.Now.ToString("HH:mm"):"Восстановление не подтверждено.";
        }catch(Exception ex){
            bridge.Dispose();running=false;recovery.Finished(DateTime.UtcNow,false);Write("Восстановление: "+ex.Message);
            if(!exitRequested&&recovery.Wanted)RecoveryPaused("Восстановление остановлено. Открой диагностику или подключись вручную.");
        }finally{recovering=false;}
        if(recovery.Paused&&!exitRequested&&recovery.Wanted)NotifyFailure("Соединение не восстановилось. Открой диагностику.");
    }
}
}
