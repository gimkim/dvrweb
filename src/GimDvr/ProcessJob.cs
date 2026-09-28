using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace GimDvr;

// Every FFmpeg lifetime belongs to the web process, including abnormal IIS termination.
sealed class ProcessJob : IDisposable
{
    readonly SafeFileHandle handle;
    ProcessJob(SafeFileHandle handle)=>this.handle=handle;
    public static IDisposable? Attach(Process process)
    {
        if(!OperatingSystem.IsWindows())return null;
        var h=CreateJobObject(IntPtr.Zero,null);if(h.IsInvalid)throw new InvalidOperationException("สร้าง process job ไม่สำเร็จ");
        var info=new Extended{Basic=new Basic{LimitFlags=0x2000}};
        if(!SetInformationJobObject(h,9,ref info,(uint)Marshal.SizeOf<Extended>())||!AssignProcessToJobObject(h,process.Handle))
        {h.Dispose();throw new InvalidOperationException("ผูก FFmpeg กับอายุแอปไม่สำเร็จ");}
        return new ProcessJob(h);
    }
    public void Dispose()=>handle.Dispose();
    [StructLayout(LayoutKind.Sequential)]struct Basic{public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)]struct Io{public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
    [StructLayout(LayoutKind.Sequential)]struct Extended{public Basic Basic;public Io Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(SafeFileHandle job,int infoClass,ref Extended info,uint length);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}


