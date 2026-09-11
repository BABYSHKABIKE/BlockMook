using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlockMook {
internal sealed class ConnectionOutcome {internal bool Running,Verified;internal int Profile;internal ProbeResult[] Results;}
internal static class ConnectionFlow {
    internal static async Task<ConnectionOutcome> Run(int preferred,int mask,bool manual,Func<int,Task> start,
        Func<Task> stop,Action abort,Func<Task<ProbeResult[]>> probe,Action<string> log,
        Action<int,ProbeResult[]> sample,CancellationToken token) {
        bool keep=false;int best=-1,bestScore=0;
        try{
            foreach(int profile in manual?new[]{preferred}:Core.ProfileOrder(preferred)){
                token.ThrowIfCancellationRequested();
                Exception error=null;ProbeResult[] values=null;
                try{
                    log("Запуск: "+Core.Names[profile]);await start(profile);token.ThrowIfCancellationRequested();
                    values=await probe();sample(profile,values);token.ThrowIfCancellationRequested();
                    if(Probes.AllSelected(values,mask)){
                        log("Повторная проверка: "+Core.Names[profile]);values=await probe();sample(profile,values);token.ThrowIfCancellationRequested();
                        if(Probes.AllSelected(values,mask)){log("Проверки выбранных сервисов подтверждены дважды. Обход оставлен включённым.");keep=true;return new ConnectionOutcome{Running=true,Verified=true,Profile=profile,Results=values};}
                    }
                    int score=Probes.Score(values,mask);
                    if(Probes.ControlOk(values)&&score>bestScore){best=profile;bestScore=score;}
                }catch(Exception ex){error=ex;}
                await stop();token.ThrowIfCancellationRequested();
                if(error!=null){if(error is OperationCanceledException || error is System.ComponentModel.Win32Exception && ((System.ComponentModel.Win32Exception)error).NativeErrorCode==1223)throw error;log("Ошибка профиля: "+error.Message);}
            }
            if(best>=0){
                token.ThrowIfCancellationRequested();log("Повторный запуск частично работающего профиля: "+Core.Names[best]);await start(best);
                var values=await probe();sample(best,values);token.ThrowIfCancellationRequested();
                if(Probes.ControlOk(values)&&Probes.Score(values,mask)>0){log("Часть сервисов недоступна. Профиль подключения оставлен включённым.");keep=true;return new ConnectionOutcome{Running=true,Verified=false,Profile=best,Results=values};}
            }
            await stop();log("Рабочий профиль не подтверждён. Обход выключен.");return new ConnectionOutcome();
        }finally{if(!keep)abort();}
    }
}
}
