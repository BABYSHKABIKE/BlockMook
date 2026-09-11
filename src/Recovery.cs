using System;

namespace BlockMook {
// Pure policy: no timers, sockets or privilege prompts. UTC times are supplied by the caller.
internal sealed class RecoveryPolicy {
    internal bool Wanted,Paused;
    internal int Failures,Attempts,ProvenMask;
    internal DateTime NextCheck,RetryAt;
    private DateTime windowStart;
    internal void Start(DateTime now,int provenMask){Wanted=true;Paused=false;Failures=Attempts=0;ProvenMask=provenMask;windowStart=now;NextCheck=now.AddSeconds(30);RetryAt=now;}
    internal void Stop(){Wanted=false;Failures=0;Paused=false;}
    internal void Changed(DateTime now){if(Wanted&&!Paused)NextCheck=now.AddSeconds(5);}
    internal void Observe(DateTime now,bool healthy){
        NextCheck=now.AddSeconds(30);
        if(!Wanted||Paused)return;
        if(healthy){Failures=0;return;}
        Failures=Math.Min(3,Failures+1);
    }
    internal bool BeginAttempt(DateTime now){
        if(!Wanted||Paused||Failures<3||now<RetryAt)return false;
        if(now-windowStart>=TimeSpan.FromMinutes(15)){Attempts=0;windowStart=now;}
        if(Attempts>=3){Paused=true;return false;}
        Attempts++;return true;
    }
    internal void Finished(DateTime now,bool healthy){
        if(!Wanted)return;
        if(healthy){Failures=0;NextCheck=now.AddSeconds(30);return;}
        Failures=3;RetryAt=now.AddSeconds(Attempts==1?15:30);NextCheck=RetryAt;
        if(Attempts>=3)Paused=true;
    }
}
}
