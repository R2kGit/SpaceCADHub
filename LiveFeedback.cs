// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Diagnostics;

// UI feedback is independent of CAD actions. A short display pulse makes a
// button tap visible even when press and release fall between two UI frames.
internal sealed class LiveFeedback
{
    private readonly object gate=new object();
    private readonly long[] until=new long[19];
    private int held;
    internal void Buttons(int mask){ButtonsAt(mask,Stopwatch.GetTimestamp());}
    internal void ButtonsAt(int mask,long now)
    {
        lock(gate)
        {
            int pressed=mask & ~held;
            for(int i=0;i<17;i++)if((pressed&(1<<i))!=0)until[i]=now+Stopwatch.Frequency*350/1000;
            held=mask & 0x1FFFF;
        }
    }
    internal void Ring(int direction){PulseAt(direction<0?17:18,Stopwatch.GetTimestamp());}
    internal void PulseAt(int index,long now)
    {
        lock(gate)until[index]=now+Stopwatch.Frequency*350/1000;
    }
    internal int Mask {get{return MaskAt(Stopwatch.GetTimestamp());}}
    internal int MaskAt(long now)
    {
        lock(gate){int result=held;for(int i=0;i<19;i++)if(until[i]>now)result|=1<<i;return result;}
    }
    internal void Clear(){lock(gate){held=0;Array.Clear(until,0,until.Length);}}
}
