// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 Benjamin Preuß; see LICENSE and NOTICE.md.
using System;
using System.Runtime.InteropServices;

// Interop field layouts derived from the installed SpaceControl 2.9.3 SDK.
// C++ bool is one byte; default Windows struct alignment is four here.
[StructLayout(LayoutKind.Sequential)]
internal struct ScAdvSens
{
    public int mXSens;
    public int mYSens;
    public int mZSens;
    public int mASens;
    public int mBSens;
    public int mCSens;
}
[StructLayout(LayoutKind.Sequential)]
internal struct ScBasicSettings
{
    public byte mIsDom;
    public int mTraSens;
    public int mRotSens;
    public int mNullRadius;
    public int mBrightnessBlue;
    public int mBrightnessDspl;
}
[StructLayout(LayoutKind.Sequential)]
internal struct ScAdvancedSettings
{
    public ScAdvSens mSens;
    public byte mIsX;
    public byte mIsY;
    public byte mIsZ;
    public byte mIsXRev;
    public byte mIsYRev;
    public byte mIsZRev;
    public int mXRemap;
    public int mYRemap;
    public int mZRemap;
    public byte mIsA;
    public byte mIsB;
    public byte mIsC;
    public byte mIsARev;
    public byte mIsBRev;
    public byte mIsCRev;
    public int mARemap;
    public int mBRemap;
    public int mCRemap;
    public int mMouseDirX;
    public int mMouseDirY;
    public int mMouseDirZ;
    public int mMouseDirA;
    public int mMouseDirB;
    public int mMouseDirC;
    public byte mIsMouseMove;
    public byte mIsMouseToRestore;
    public byte mIsMouseAbs;
    public int mSendDelay;
    public int mAverageNum;
    public byte mIsRemap;
    public short mTraLmBnd;
    public short mTraMhBnd;
    public short mRotLmBnd;
    public short mRotMhBnd;
    public byte mIsToSendToFgWin;
    public byte mIsSpcFuncToSend;
    public byte mIsWheelAssShown;
    public byte mIsWsFuncToSend;
    public byte mIsCapFuncToSend;
    public byte mAreChangesToSend;
    public int mBrightnessRed;
    public int mBrightnessGreen;
    public byte mAreDevLights;
    public int mRotCenterMode;
    public int mMoveMode;
    public byte mIsRightHandMode;
    public byte mIsClock;
    public byte mIsAutoFit;
    public int mLanguage;
    public int mPicture;
}
[StructLayout(LayoutKind.Sequential)]
internal struct ScKeyAssignment
{
    public short m1;
    public short m2;
    public short m3;
    public short m4;
    public short m5;
    public short m6;
    public short mCtrl;
    public short mAlt;
    public short mShift;
    public short mEsc;
    public short mFront;
    public short mRight;
    public short mTop;
    public short mFit;
    public short m2D3D;
    public short mPanel;
    public short mMenue;
    public short m1B;
    public short m2B;
    public short m3B;
    public short m4B;
    public short m5B;
    public short m6B;
    public short mCtrlB;
    public short mAltB;
    public short mShiftB;
    public short mEscB;
    public short mFrontB;
    public short mRightB;
    public short mTopB;
    public short mFitB;
    public short m2D3DB;
    public short mPanelB;
    public short mMenueB;
    public short mWheelLeft;
    public short mWheelRight;
    public short mWheelL;
    public short mWheelR;
    public short mWheelLr;
    public short mWheelRl;
    public short mWheelLrl;
    public short mWheelRlr;
    public short mWheelLl;
    public short mWheelRr;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=28)] public short[] mWheelSlot;
    public short mXll;
    public short mXlm;
    public short mXlh;
    public short mXrl;
    public short mXrm;
    public short mXrh;
    public short mYdl;
    public short mYdm;
    public short mYdh;
    public short mYul;
    public short mYum;
    public short mYuh;
    public short mZfl;
    public short mZfm;
    public short mZfh;
    public short mZbl;
    public short mZbm;
    public short mZbh;
    public short mAnyTraL;
    public short mAnyTraM;
    public short mAnyTraH;
    public short mArl;
    public short mArm;
    public short mArh;
    public short mAll;
    public short mAlm;
    public short mAlh;
    public short mBrl;
    public short mBrm;
    public short mBrh;
    public short mBll;
    public short mBlm;
    public short mBlh;
    public short mCrl;
    public short mCrm;
    public short mCrh;
    public short mCll;
    public short mClm;
    public short mClh;
    public short mAnyRotL;
    public short mAnyRotM;
    public short mAnyRotH;
    public short mAnyL;
    public short mAnyM;
    public short mAnyH;
    public short mAnyStarted;
    public short mAnyStopped;
}
[StructLayout(LayoutKind.Sequential)]
internal struct ScFunc
{
    public int mIdx;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=32)] public byte[] mName;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=256)] public byte[] mFunc;
}
[StructLayout(LayoutKind.Sequential)]
internal struct ScDevPars
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=64)] public byte[] mName;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=256)] public byte[] mFileName;
    public ScBasicSettings mBs;
    public ScAdvancedSettings mAs;
    public ScKeyAssignment mKa;
    public int mSysFuncNum;
    public int mExecApplLoadCfgNum;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst=140)] public ScFunc[] mFuncList;
}

internal sealed class VendorButtonLease : IDisposable
{
    [DllImport("spc_ctrlr_64.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int scGetDevPars(int device,IntPtr data);
    [DllImport("spc_ctrlr_64.dll", CallingConvention=CallingConvention.Cdecl)] private static extern int scSetDevPars(int device,IntPtr data);
    private readonly Func<IntPtr,int> read;
    private readonly Func<IntPtr,int> write;
    private byte[] original;
    internal VendorButtonLease(Func<IntPtr,int> read=null,Func<IntPtr,int> write=null)
    {this.read=read??(p=>scGetDevPars(0,p));this.write=write??(p=>scSetDevPars(0,p));}
    private static int AdvancedOffset(string field)
    {return Marshal.OffsetOf(typeof(ScDevPars),"mAs").ToInt32()+Marshal.OffsetOf(typeof(ScAdvancedSettings),field).ToInt32();}
    private static readonly string[] MappingFlags={"mIsSpcFuncToSend","mIsWsFuncToSend","mIsCapFuncToSend"};
    internal void Acquire()
    {
        if(original!=null)throw new InvalidOperationException("Herstellerbelegung bereits übernommen.");
        int size=Marshal.SizeOf(typeof(ScDevPars));
        IntPtr ptr=Marshal.AllocHGlobal(size);
        try
        {
            if(read(ptr)!=0)throw new InvalidOperationException("Hersteller-Tastenbelegung konnte nicht gelesen werden.");
            original=new byte[size];Marshal.Copy(ptr,original,0,size);
            int count=Marshal.ReadInt32(ptr,Marshal.OffsetOf(typeof(ScDevPars),"mSysFuncNum").ToInt32());
            if(count<1 || count>140){original=null;throw new InvalidOperationException("SDK-Geräteeinstellungen haben ein unerwartetes Format.");}
            int offset=Marshal.OffsetOf(typeof(ScDevPars),"mKa").ToInt32();
            int length=Marshal.SizeOf(typeof(ScKeyAssignment));
            // INACTIVE (index zero): the Hub owns all assignments while running.
            Marshal.Copy(new byte[length],0,IntPtr.Add(ptr,offset),length);
            // Disable all manufacturer's function dispatch paths as well.
            // Do not subsequently invoke WHEEL_TO_SENS or another wheel preset.
            foreach(string flag in MappingFlags)Marshal.WriteByte(ptr,AdvancedOffset(flag),0);
            // Retain the original snapshot on failure: outer Dispose still
            // needs to restore it if the SDK applied only part of the update.
            if(write(ptr)!=0)throw new InvalidOperationException("Hersteller-Tastenaktionen konnten nicht deaktiviert werden.");
            if(read(ptr)!=0)throw new InvalidOperationException("Neutrale Herstellerbelegung konnte nicht geprüft werden.");
            for(int i=0;i<length;i++)if(Marshal.ReadByte(ptr,offset+i)!=0)throw new InvalidOperationException("Herstellerbelegung wurde erneut aktiviert.");
            foreach(string flag in MappingFlags)if(Marshal.ReadByte(ptr,AdvancedOffset(flag))!=0)throw new InvalidOperationException("Hersteller-Funktionsausgabe ist weiterhin aktiv.");
        }
        finally {Marshal.FreeHGlobal(ptr);}
    }
    public void Dispose()
    {
        if(original==null)return;
        IntPtr ptr=Marshal.AllocHGlobal(original.Length);
        try {Marshal.Copy(original,0,ptr,original.Length);write(ptr);}
        finally {Marshal.FreeHGlobal(ptr);original=null;}
    }
}

