// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Diagnostics;

internal sealed class CalibrationInfo
{
    internal readonly bool Busy,CanCancel,Success,Failed,Settling;
    internal readonly string Text;
    internal CalibrationInfo(bool busy,bool cancel,bool success,bool failed,bool settling,string text){Busy=busy;CanCancel=cancel;Success=success;Failed=failed;Settling=settling;Text=text;}
}
internal sealed class Calibration
{
    private readonly object gate=new object();
    private readonly Func<long> clock;
    internal Calibration(Func<long> clock=null){this.clock=clock??Stopwatch.GetTimestamp;}
    private enum Phase { Idle,Countdown,Applying,Settling,Done,Failed,Cancelled }
    private Phase phase;
    private long deadline;
    private string message="Kappe vollständig loslassen.";
    internal CalibrationInfo Info {get{return InfoAt(Stopwatch.GetTimestamp());}}
    internal CalibrationInfo InfoAt(long now)
    {
        lock(gate)
        {
            bool busy=phase==Phase.Countdown || phase==Phase.Applying || phase==Phase.Settling;
            string text=phase==Phase.Countdown ? "Kappe vollständig loslassen · "+Math.Max(1,(int)Math.Ceiling((deadline-now)/(double)Stopwatch.Frequency))+" s" : message;
            return new CalibrationInfo(busy,phase==Phase.Countdown,phase==Phase.Done,phase==Phase.Failed,phase==Phase.Applying||phase==Phase.Settling,text);
        }
    }
    internal bool Begin(long now)
    {
        lock(gate)
        {
            if(phase==Phase.Countdown || phase==Phase.Applying || phase==Phase.Settling)return false;
            phase=Phase.Countdown;deadline=now+Stopwatch.Frequency*3;message="Kappe loslassen";return true;
        }
    }
    internal bool Cancel()
    {
        lock(gate){if(phase!=Phase.Countdown)return false;phase=Phase.Cancelled;message="Abgebrochen · Nullpunkt unverändert.";return true;}
    }
    internal void Abort(string reason)
    {
        lock(gate){if(phase!=Phase.Countdown && phase!=Phase.Applying && phase!=Phase.Settling)return;phase=Phase.Failed;message=reason;}
    }
    internal bool ReadStatus(int status)
    {
        // The official SDK returns SC_COMMUNICATION_ERROR (1) after its
        // receive timeout when the idle cap sends no new packet. That alone
        // is not a disconnection; SetHardwareZero checks presence separately.
        if(status==0 || status==1)return false;
        if(!Info.Busy)return false;
        Abort("Geräteabfrage fehlgeschlagen (SDK "+status+").");return true;
    }
    // Called only by the controller thread. The native SDK operation runs
    // outside the state lock so it cannot block the GUI's status polling.
    internal bool Tick(long now,Func<int> setZero)
    {
        lock(gate)
        {
            if(phase==Phase.Settling && now>=deadline){phase=Phase.Done;message="Nullstellung übernommen.";return true;}
            if(phase!=Phase.Countdown || now<deadline)return false;
            phase=Phase.Applying;message="Nullpunkt wird übernommen …";
        }
        int status;
        try {status=setZero();}
        catch(Exception e){Abort("Nullstellung fehlgeschlagen: "+e.Message);return true;}
        lock(gate)
        {
            if(status==0){phase=Phase.Settling;deadline=Math.Max(now,clock())+Stopwatch.Frequency/2;message="Nullpunkt übernommen · kurz warten …";}
            else {phase=Phase.Failed;message="Nullstellung fehlgeschlagen (SDK "+status+").";}
        }
        return true;
    }
}
