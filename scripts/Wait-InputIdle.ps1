[CmdletBinding()]param([int]$IdleSeconds=10,[int]$TimeoutSeconds=120)
$ErrorActionPreference='Stop'
if(-not('AxonInputIdle' -as [type])){Add-Type @'
using System;using System.Runtime.InteropServices;
public static class AxonInputIdle {
[StructLayout(LayoutKind.Sequential)]public struct Info{public uint Size,Tick;}
[DllImport("user32.dll")]private static extern bool GetLastInputInfo(ref Info value);
public static uint Milliseconds(){var value=new Info{Size=(uint)Marshal.SizeOf<Info>()};if(!GetLastInputInfo(ref value))throw new InvalidOperationException("Cannot read input idle state.");return unchecked((uint)Environment.TickCount-value.Tick);}
}
'@}
$until=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
while([AxonInputIdle]::Milliseconds() -lt ($IdleSeconds*1000)){if([DateTime]::UtcNow -ge $until){throw 'Desktop remains in active use; no native input test started. Resume when the pointer is idle.'};Start-Sleep -Milliseconds 500}
