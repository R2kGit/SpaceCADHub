// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Diagnostics;

internal sealed class LcdWriteResult
{
    internal readonly int Status,Written;
    internal LcdWriteResult(int status,int written){Status=status;Written=written;}
}
internal sealed class LcdWriter
{
    private readonly string[] cache=new string[7];
    private readonly long[] retryAfter=new long[7];
    private readonly int[] lastFailure=new int[7];
    private int cursor;
    internal void Reset(){Array.Clear(cache,0,7);Array.Clear(retryAfter,0,7);Array.Clear(lastFailure,0,7);cursor=0;}
    internal bool Tick(long now,string[] frame,Func<int,string,LcdWriteResult> write,Action<string> log)
    {
        if(frame==null||frame.Length!=7)throw new ArgumentException("LCD frame must contain seven rows.");
        for(int offset=0;offset<7;offset++)
        {
            int row=(cursor+offset)%7;
            if(cache[row]==frame[row]||now<retryAfter[row])continue;
            // Advance even after failure: one bad row must never starve the rest.
            cursor=(row+1)%7;
            string payload=string.IsNullOrEmpty(frame[row])?" ":frame[row];
            LcdWriteResult result;
            try{result=write(row,payload);}catch{result=new LcdWriteResult(-1,0);}
            int failure=result.Status!=0?result.Status:(result.Written<=0&&payload!=" "?-2:0);
            if(failure==0)
            {
                cache[row]=frame[row];retryAfter[row]=0;
                if(lastFailure[row]!=0)log("Zeile "+(row+1)+": Schreiben wieder erfolgreich.");
                lastFailure[row]=0;
                if(result.Written>0&&result.Written<payload.Length)log("Zeile "+(row+1)+": SDK hat "+result.Written+" von "+payload.Length+" Zeichen übernommen.");
            }
            else
            {
                retryAfter[row]=now+Stopwatch.Frequency*2;
                if(lastFailure[row]!=failure)log("Zeile "+(row+1)+": SDK="+result.Status+", übernommen="+result.Written+", angefordert="+payload.Length+". Andere Zeilen werden weitergeschrieben.");
                lastFailure[row]=failure;
            }
            return true;
        }
        return false;
    }
}
