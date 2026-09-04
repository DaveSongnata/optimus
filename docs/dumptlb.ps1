$ErrorActionPreference = 'Stop'
$src = @'
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

public class Sink : ITypeLibImporterNotifySink {
    public void ReportEvent(ImporterEventKind kind, int code, string msg) { }
    public Assembly ResolveRef(object tl) {
        var conv = new TypeLibConverter();
        return conv.ConvertTypeLibToAssembly(tl, "ref_" + Guid.NewGuid().ToString("N") + ".dll", 0, new Sink(), null, null, null, null);
    }
}

public class TlbDump {
    [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void LoadTypeLibEx(string file, int regkind, [MarshalAs(UnmanagedType.Interface)] out object tl);

    public static Assembly Load(string path, string outName) {
        object tl;
        LoadTypeLibEx(path, 0 /*REGKIND_NONE*/, out tl);
        var conv = new TypeLibConverter();
        return conv.ConvertTypeLibToAssembly(tl, outName, 0, new Sink(), null, null, null, null);
    }
}
'@
Add-Type -TypeDefinition $src -ReferencedAssemblies System.Runtime.InteropServices.dll -ErrorAction Stop

$outDir = 'C:\Users\dave\AppData\Local\Temp\claude\C--Projetos-Rust-siscut-optimus\09b53b0e-8481-4fbd-b94e-ed56aeb240a7\scratchpad'
$asm = [TlbDump]::Load('C:\Projetos\Rust\siscut\siscut\shared\VGCoreAuto.tlb', (Join-Path $outDir 'VGCoreInterop.dll'))
Write-Output "ASM: $($asm.FullName)"
Write-Output "TYPES: $($asm.GetTypes().Count)"

$sb = New-Object System.Text.StringBuilder
foreach ($t in ($asm.GetTypes() | Sort-Object FullName)) {
    if ($t.IsEnum) {
        [void]$sb.AppendLine("ENUM $($t.Name)")
        foreach ($n in [Enum]::GetNames($t)) {
            $v = [int][Enum]::Parse($t, $n)
            [void]$sb.AppendLine("    $n = $v")
        }
        [void]$sb.AppendLine()
    } elseif ($t.IsInterface) {
        [void]$sb.AppendLine("INTERFACE $($t.Name)  [$($t.GUID)]")
        foreach ($m in $t.GetMembers()) {
            if ($m -is [System.Reflection.MethodInfo]) {
                $mi = [System.Reflection.MethodInfo]$m
                $ps = ($mi.GetParameters() | ForEach-Object {
                    $mod = ''
                    if ($_.IsOut) { $mod = 'out ' } elseif ($_.ParameterType.IsByRef) { $mod = 'ref ' }
                    $opt = ''
                    if ($_.IsOptional) { $opt = '?' }
                    "$mod$($_.ParameterType.Name) $($_.Name)$opt"
                }) -join ', '
                [void]$sb.AppendLine("    $($mi.ReturnType.Name) $($mi.Name)($ps)")
            }
        }
        [void]$sb.AppendLine()
    } elseif ($t.IsValueType) {
        [void]$sb.AppendLine("STRUCT $($t.Name)")
        foreach ($f in $t.GetFields()) { [void]$sb.AppendLine("    $($f.FieldType.Name) $($f.Name)") }
        [void]$sb.AppendLine()
    }
}
$path = Join-Path $outDir 'vgcore_dump.txt'
[System.IO.File]::WriteAllText($path, $sb.ToString())
Write-Output "WROTE $path  ($((Get-Item $path).Length) bytes)"
