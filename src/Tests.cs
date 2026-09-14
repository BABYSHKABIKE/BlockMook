using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace BlockMook {
internal static partial class Tests {
    private static List<string> passes=new List<string>();
    private static void Check(string name,bool ok){if(!ok)throw new Exception(name);passes.Add("PASS "+name);}
    private static bool Throws(Action action){try{action();return false;}catch(ArgumentException){return true;}}
    internal static void Run(string path) {
        Check("Reject empty service selection",Throws(()=>Core.Domains(0)));
        Check("Reject undefined selection bits",Throws(()=>Core.Domains(32)));
        Check("Reject undefined strategy",Throws(()=>Core.Arguments(9,3,"hosts.txt")));
        Check("Reject command-line quote injection",Throws(()=>Core.Arguments(0,3,"bad\" --injected")));
        Check("YouTube excludes Discord domains",Core.Domains(1).All(d=>!d.Contains("discord")));
        Check("Discord excludes YouTube domains",Core.Domains(2).All(d=>!d.Contains("youtube")&&!d.Contains("googlevideo")));
        Check("Video CDN included",Core.Domains(1).Contains("googlevideo.com"));
        for(int p=0;p<3;p++)for(int mask=1;mask<=Services.AllMask;mask++) {
            string command=Core.Arguments(p,mask,@"C:\test path\hosts.txt");
            Check("Hostlist and scope "+p+"/"+mask,command.Contains("--hostlist="+Core.Q(@"C:\test path\hosts.txt")) && command.Contains("19294-19344")==((mask&2)!=0) && !command.Contains("--ipset=") && !command.Contains("%"));
        }
        Check("Reject YouTube block page HTTP 200",!Probes.Valid(0,200,"blocked"));
        Check("Accept YouTube expected 204",Probes.Valid(0,204,""));
        Check("Reject Discord HTML with status 200",!Probes.Valid(1,200,"<html>login</html>"));
        Check("Accept Discord gateway response",Probes.Valid(1,200,"{\"url\": \"wss://gateway.discord.gg\"}"));
        Check("Reject foreign gateway domain",!Probes.Valid(1,200,"{\"url\":\"wss://gateway.discord.gg.evil.test\"}"));
        Check("Reject HTTP redirect",!Probes.Valid(0,302,""));
        Check("Unselected service does not improve score",Probes.Score(new[]{new ProbeResult{Index=1,Ok=true},new ProbeResult{Index=2,Ok=true}},1)==0);
        using(var engine=new Engine()){engine.Dispose();engine.Dispose();Check("Engine stop is idempotent",!engine.Alive);}
        using(var bridge=new Bridge()){bridge.Dispose();bridge.Dispose();Check("Bridge shutdown is idempotent",!bridge.Connected);}
        TestJob();
        TestPipe();
        TestDiagnostics(Path.Combine(Path.GetDirectoryName(path),"test-diagnostics"));
        TestConnection();
        TestUpdates(Path.Combine(Path.GetDirectoryName(path),"test-downloads"));
        TestDesktop(Path.Combine(Path.GetDirectoryName(path),"test-diagnostics"));TestNetwork(Path.Combine(Path.GetDirectoryName(path),"test-diagnostics"));
        TestServices(Path.Combine(Path.GetDirectoryName(path),"test-diagnostics"));
        TestPreferences(Path.Combine(Path.GetDirectoryName(path),"test-diagnostics","preferences.txt"));
        Check("Alternative is tried first without duplicate strategies",Core.DefaultProfile==1&&Core.ProfileOrder(1).SequenceEqual(new[]{1,0,2}));
        Check("Saved preference first without duplicate strategies",Core.ProfileOrder(2).SequenceEqual(new[]{2,1,0}));
        Check("Reject HTML containing gateway JSON substring",!Probes.Valid(1,200,"<html>{\"url\":\"wss://gateway.discord.gg\"}</html>"));
        Check("Gateway Hello accepted",DiscordChecks.ValidHello("{\"op\":10,\"d\":{\"heartbeat_interval\":41250}}"));
        Check("Wrong opcode rejected",!DiscordChecks.ValidHello("{\"op\":11,\"d\":{\"heartbeat_interval\":41250}}"));
        Check("Missing heartbeat rejected",!DiscordChecks.ValidHello("{\"op\":10,\"d\":{}}"));
        Check("Invalid manifest rejected",!DiscordChecks.ValidManifest("<html>blocked</html>"));
        Check("Manifest structure accepted",DiscordChecks.ValidManifest("{\"full\":{},\"modules\":{},\"metadata_version\":1}"));
        var serviceChecks=Enumerable.Range(0,4).Select(i=>new ServiceCheck{Name="Test "+i,Url="TEST",Ok=i==0,Detail="SIMULATED"}).ToArray();
        Check("Single passing API cannot mark Discord available",!DiscordChecks.Combine(serviceChecks).Ok);
        foreach(var serviceCheck in serviceChecks)serviceCheck.Ok=true;
        Check("All four Discord components required",DiscordChecks.Combine(serviceChecks).Ok);
        Core.VerifyEngine();Check("Pinned local engine hashes",true);
        File.WriteAllLines(path,passes.Concat(new[]{"TOTAL "+passes.Count+" passed"}));
    }
    private static void TestPreferences(string path) {
        var parsed=Preferences.Parse("services=2\nwelcome=True\nreduceMotion=True\nprofile=2\nunknown=ignored");
        Check("Preference fields parse independently",parsed.Services==2&&parsed.WelcomeDone&&parsed.ReduceMotion);
        parsed.Save(path);var loaded=Preferences.Load(path);
        Check("First-run completion, manual profile and services survive reload",loaded.Services==2&&loaded.WelcomeDone&&loaded.ReduceMotion&&loaded.ManualProfile==2);
        parsed.Services=1;parsed.Save(path);
        Check("Existing preferences saved in verified copies",Preferences.Load(path).Services==1&&SettingsStorage.Decode(File.ReadAllText(path))!=null&&File.ReadAllText(path)==File.ReadAllText(path+".tmp"));
        File.WriteAllText(path,"BLOCKMOOK-SETTINGS-SHA256 interrupted");
        Check("Interrupted preferences write recovers pending copy",Preferences.Load(path).Services==1);
        parsed.Save(path);File.WriteAllText(path+".tmp","incomplete");
        Check("Interrupted pending preferences keep completed selection",Preferences.Load(path).Services==1);
        string legacy=path+".legacy",fresh=path+"."+Guid.NewGuid().ToString("N");File.WriteAllText(legacy,"services=2\nwelcome=True");
        Check("Plaintext legacy preferences remain compatible",Preferences.LoadUser(fresh,legacy).Services==2&&Preferences.Load(legacy).WelcomeDone);
        SettingsStorage.Write(fresh, "services=1\nwelcome=True");File.Delete(fresh);
        Check("Pending current preferences take precedence over legacy",Preferences.LoadUser(fresh,legacy).Services==1);
        Check("Normal launches share a mutex",Program.InstanceMutexName(false)==Program.InstanceMutexName(false));
        Check("UI checks never collide with desktop or each other",Program.InstanceMutexName(true)!=Program.InstanceMutexName(false)&&Program.InstanceMutexName(true)!=Program.InstanceMutexName(true));
        parsed=Preferences.Parse("services=0\nwelcome=garbage\nreduceMotion=maybe\nservices=999\nprofile=9");
        Check("Corrupt preferences restore safe defaults",parsed.Services==3&&!parsed.WelcomeDone&&!parsed.ReduceMotion&&parsed.ManualProfile==-1);
        Check("Missing preferences open onboarding",!Preferences.Load(path+".missing").WelcomeDone);
        parsed.Services=0;Check("Empty preferences cannot overwrite saved selection",Throws(()=>parsed.Save(path))&&Preferences.Load(path).Services==1);
        Check("Engine is relative to portable executable",Core.EngineRoot==Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"engine"));
    }
    private static void TestJob() {
        IntPtr job=Native.CreateKillJob();
        using(var child=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--test-child"){UseShellExecute=false,CreateNoWindow=true})) {
            try {
                Check("Child assigned to kill-on-close job",Native.AssignProcessToJobObject(job,child.Handle));
                Native.CloseHandle(job);job=IntPtr.Zero;
                Check("Closing job terminates only owned child",child.WaitForExit(5000));
            } finally {if(job!=IntPtr.Zero)Native.CloseHandle(job);if(!child.HasExited)child.Kill();}
        }
    }
    private static Task Fault(string message){var completion=new TaskCompletionSource<bool>();completion.SetException(new IOException(message));return completion.Task;}
    private static void TestConnection(){
        TestRecoveryConnection();
        foreach(string mode in new[]{"success","fallback","partial","failure","cancel","uac-cancel","probe-error","stop-error","report-error","confirmation-fails","manual"}){
            var starts=new List<int>();bool active=false,aborted=false;int selected=-1,calls=0;ConnectionOutcome outcome=null;Exception error=null;
            using(var cancellation=new CancellationTokenSource()){
                try{
                    outcome=ConnectionFlow.Run(1,3,mode=="manual",
                        p=>{starts.Add(p);selected=p;active=true;if(mode=="cancel")cancellation.Cancel();if(mode=="uac-cancel")throw new System.ComponentModel.Win32Exception(1223);return Task.FromResult(true);},
                        ()=>{if(mode=="stop-error")return Fault("SIMULATED stop error");active=false;return Task.FromResult(true);},
                        ()=>{aborted=true;active=false;},
                        ()=>{calls++;if(mode=="probe-error")throw new IOException("SIMULATED probe error");bool ok=mode=="success"||mode=="fallback"&&selected==0||mode=="confirmation-fails"&&calls==1;return Task.FromResult(new[]{new ProbeResult{Index=0,Ok=ok||mode=="partial"},new ProbeResult{Index=1,Ok=ok},new ProbeResult{Index=2,Ok=true}});},
                        message=>{},(p,sample)=>{if(mode=="report-error")throw new IOException("SIMULATED report error");},cancellation.Token).GetAwaiter().GetResult();
                }catch(Exception ex){error=ex;}
            }
            if(mode=="success")Check("Connect keeps verified preferred profile running after two probes",outcome.Verified&&active&&!aborted&&calls==2&&starts.SequenceEqual(new[]{1}));
            else if(mode=="fallback")Check("Connect automatically advances to working profile",outcome.Verified&&active&&outcome.Profile==0&&starts.SequenceEqual(new[]{1,0}));
            else if(mode=="partial")Check("Partial YouTube access retained without claiming Discord success",outcome.Running&&!outcome.Verified&&active&&outcome.Results[0].Ok&&!outcome.Results[1].Ok);
            else if(mode=="manual")Check("Manual mode does not try unrelated strategies",starts.SequenceEqual(new[]{1})&&!active&&aborted);
            else Check("Connect "+mode+" leaves no owned engine",!active&&aborted&&(outcome==null||!outcome.Running));
            if(mode=="cancel")Check("Connect cancellation stops before probe and fallback",error is OperationCanceledException&&calls==0&&starts.Count==1);
            if(mode=="uac-cancel")Check("UAC cancellation never prompts for other strategies",error is System.ComponentModel.Win32Exception&&starts.Count==1&&calls==0);
            if(mode=="confirmation-fails")Check("One transient successful probe cannot verify profile",outcome!=null&&!outcome.Verified);
        }
    }
    private static void TestRecoveryConnection() {
        foreach(int successAt in new[]{0,2,3}) {
            DateTime now=new DateTime(2026,9,14,0,0,0,DateTimeKind.Utc);
            var policy=new RecoveryPolicy();policy.Start(now,1);
            for(int i=0;i<3;i++)policy.Observe(now,false);
            bool connected=true,active=false;int attempts=0;
            while(policy.BeginAttempt(now)) {
                attempts++;bool succeeds=attempts==successAt;
                var outcome=ConnectionFlow.Run(1,1,false,
                    p=>{if(!connected)throw new IOException("SIMULATED lost worker");active=true;return Task.FromResult(true);},
                    ()=>{active=false;return Task.FromResult(true);},()=>{connected=false;active=false;},
                    ()=>Task.FromResult(new[]{new ProbeResult{Index=0,Ok=succeeds},new ProbeResult{Index=2,Ok=true}}),
                    message=>{},(p,values)=>{},CancellationToken.None,true).GetAwaiter().GetResult();
                policy.Finished(now,outcome.Verified);
                Check("Recovery keeps worker between attempts "+successAt+"/"+attempts,connected&&active==outcome.Running);
                if(outcome.Verified)break;
                Check("Failed recovery stops filtering "+successAt+"/"+attempts,!active&&!outcome.Running);
                Check("Failed recovery respects retry delay "+successAt+"/"+attempts,!policy.BeginAttempt(policy.RetryAt.AddMilliseconds(-1)));
                now=policy.RetryAt;
            }
            Check("Recovery reaches scheduled outcome "+successAt,attempts==(successAt==0?3:successAt)&&policy.Paused==(successAt==0)&&active==(successAt!=0));
            policy.Stop();Check("Manual stop prevents recovery after failed or successful attempts "+successAt,!policy.BeginAttempt(now.AddHours(1)));
        }
        foreach(string failure in new[]{"cancel","cancel-final-stop","stop-error"}) {
            bool active=false,aborted=false;int stops=0;Exception error=null;
            using(var cancellation=new CancellationTokenSource()) {
                try {
                    ConnectionFlow.Run(1,1,false,p=>{active=true;if(failure=="cancel")cancellation.Cancel();return Task.FromResult(true);},
                        ()=>{if(failure=="stop-error")return Fault("SIMULATED missing STOP acknowledgement");active=false;stops++;if(failure=="cancel-final-stop"&&stops==Core.ProfileOrder(1).Length+1)cancellation.Cancel();return Task.FromResult(true);},
                        ()=>{aborted=true;active=false;},()=>Task.FromResult(new[]{new ProbeResult{Index=0,Ok=false},new ProbeResult{Index=2,Ok=true}}),
                        message=>{},(p,values)=>{},cancellation.Token,true).GetAwaiter().GetResult();
                }catch(Exception ex){error=ex;}
            }
            Check("Recovery closes worker on "+failure,aborted&&!active&&error!=null);
            if(failure=="cancel-final-stop")Check("Cancellation after final STOP does not retain worker",error is OperationCanceledException&&stops==Core.ProfileOrder(1).Length+1);
        }
    }
    private static void TestDiagnostics(string folder) {
        foreach(string mode in new[]{"complete","conflict","start-failure","cancel","stop-failure","probe-failure","report-failure"}) {
            int starts=0,stops=0,probes=0;bool active=false,aborted=false,liveReadable=false;DiagnosticOutcome outcome=null;Exception failure=null;string path;
            using(var cancellation=new CancellationTokenSource())
            using(var report=new DiagnosticReport(folder)) {
                path=report.PathName;report.Add("TEST FIXTURE: simulated callbacks, no network or real engine. "+mode);
                try {
                    outcome=Diagnostics.Run(report,3,()=>mode=="conflict"?"Test VPN active":null,
                        ()=>Task.FromResult(true),
                        ()=>{
                            probes++;
                            if(mode=="report-failure"&&probes==2)report.Dispose();
                            if(mode=="probe-failure"&&probes==2)throw new IOException("Test probe error");
                            return Task.FromResult(new[]{new ProbeResult{Index=0,Ok=true,Detail="SIMULATED"},new ProbeResult{Index=1,Ok=true,Detail="SIMULATED"},new ProbeResult{Index=2,Ok=true,Detail="SIMULATED"}});
                        },
                        selected=>{starts++;active=true;if(mode=="cancel")cancellation.Cancel();if(mode=="start-failure"&&selected==1)return Fault("Test startup failure");return Task.FromResult(true);},
                        ()=>{stops++;if(mode=="stop-failure"&&active)return Fault("Test stop failure");active=false;return Task.FromResult(true);},
                        ()=>{aborted=true;active=false;},cancellation.Token,
                        stage=>{using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var reader=new StreamReader(file)){liveReadable=reader.ReadToEnd().Contains("НАЧАТО");}}
                    ).GetAwaiter().GetResult();
                }catch(Exception ex){failure=ex;}
            }
            string saved=File.ReadAllText(path);
            Check("Diagnostics "+mode+" always releases engine",aborted&&!active&&stops>0);
            if(mode=="complete")Check("All three strategies and both baselines run even when baseline passes",starts==3&&probes==5&&outcome.Status=="ЗАВЕРШЕНО"&&liveReadable&&saved.Contains("ИТОГ: ЗАВЕРШЕНО"));
            if(mode=="conflict")Check("VPN prevents probes and elevation",starts==0&&probes==0&&outcome.Status=="ОШИБКА"&&saved.Contains("Test VPN active"));
            if(mode=="start-failure")Check("Failed startup recorded and next strategy tested",starts==3&&saved.Contains("Test startup failure")&&outcome.StopConfirmed);
            if(mode=="cancel")Check("Cancellation stops before next profile",starts==1&&probes==1&&outcome.Status=="ПРЕРВАНО"&&saved.Contains("ИТОГ: ПРЕРВАНО"));
            if(mode=="stop-failure")Check("Lost stop acknowledgement cannot be reported as stopped",starts==1&&!outcome.StopConfirmed&&outcome.Status=="ОШИБКА"&&saved.Contains("не подтверждена"));
            if(mode=="probe-failure")Check("Probe exception recorded without orphan engine",starts==3&&saved.Contains("Test probe error"));
            if(mode=="report-failure")Check("Report failure aborts loop and retains partial file",failure!=null&&starts==1&&!saved.Contains("ИТОГ:")&&saved.Contains("НАЧАТО"));
        }
    }
    internal static void EngineParameters(string output) {
        Core.VerifyEngine();
        var report=new List<string>();
        string hosts=Path.Combine(Path.GetDirectoryName(output),"engine-test-hosts.txt");
        try {
            for(int profile=0;profile<3;profile++)for(int mask=1;mask<=Services.AllMask;mask++) {
                File.WriteAllLines(hosts,Core.Domains(mask),new UTF8Encoding(false));
                using(var process=new Process{StartInfo=new ProcessStartInfo(Path.Combine(Core.EngineRoot,"winws.exe"),"--dry-run "+Core.Arguments(profile,mask,hosts)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Core.EngineRoot,RedirectStandardOutput=true,RedirectStandardError=true}}) {
                    process.Start();var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(8000)){process.Kill();throw new Exception("Engine dry-run timeout");}
                    Task.WaitAll(stdout,stderr);
                    if(process.ExitCode!=0)throw new Exception("Dry-run "+profile+"/"+mask+" exit "+process.ExitCode+": "+stdout.Result+stderr.Result);
                    report.Add("PASS native winws --dry-run profile="+profile+" mask="+mask);
                }
            }
            File.WriteAllLines(output,report.Concat(new[]{"93 native argument checks passed; traffic interception was not started."}));
        } finally {File.Delete(hosts);}
    }
    private static string ReadReply(StreamReader reader) {
        var task=reader.ReadLineAsync();if(!task.Wait(5000))throw new Exception("IPC reply timeout");return task.Result;
    }
    private static void TestPipe() {
        string token=Guid.NewGuid().ToString("N");
        using(var parent=Process.GetCurrentProcess())
        using(var worker=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--worker "+token+" "+parent.Id+" "+parent.StartTime.ToUniversalTime().Ticks){UseShellExecute=false,CreateNoWindow=true})) {
            try {
                using(var pipe=new NamedPipeClientStream(".","BlockMook-"+token,PipeDirection.InOut)) {
                    pipe.Connect(5000);
                    using(var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true))
                    using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true}) {
                        Check("IPC authenticated parent handshake",ReadReply(reader)=="READY");
                        writer.WriteLine("STATUS");Check("IPC initial stopped state",ReadReply(reader)=="STOPPED");
                        writer.WriteLine("START|99|3");Check("IPC rejects invalid profile before engine",ReadReply(reader).StartsWith("ERROR|"));
                        writer.WriteLine("START|0|0");Check("IPC rejects empty selection",ReadReply(reader).StartsWith("ERROR|"));
                        writer.WriteLine("STOP");Check("IPC repeated stop succeeds",ReadReply(reader)=="STOPPED");
                        var failedRecovery=ConnectionFlow.Run(1,1,false,p=>Task.FromResult(true),
                            ()=>{writer.WriteLine("STOP");if(ReadReply(reader)!="STOPPED")throw new IOException("STOP not acknowledged");return Task.FromResult(true);},
                            ()=>pipe.Dispose(),()=>Task.FromResult(new[]{new ProbeResult{Index=0,Ok=false},new ProbeResult{Index=2,Ok=true}}),
                            message=>{},(p,values)=>{},CancellationToken.None,true).GetAwaiter().GetResult();
                        Check("Failed recovery retains actual worker pipe with filtering stopped",!failedRecovery.Running&&pipe.IsConnected&&!worker.HasExited);
                        writer.WriteLine("STATUS");Check("Retained worker accepts commands for the next recovery attempt",ReadReply(reader)=="STOPPED");
                    }
                }
                Check("IPC EOF exits worker",worker.WaitForExit(5000));
            } finally {if(!worker.HasExited)worker.Kill();}
        }
    }
}
}
