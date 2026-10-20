
param([string]$Exe, [string]$TypeName, [string]$OutFile, [string]$CecilPath)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$cecilAsm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($CecilPath))
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Exe)
$sb = New-Object System.Text.StringBuilder
function Emit-Type($t) {
  [void]$sb.AppendLine('########## ' + $t.FullName + '  (base=' + $t.BaseType + ')')
  foreach ($f in $t.Fields) { [void]$sb.AppendLine('  FIELD ' + $f.Name + ' : ' + $f.FieldType.Name + ' ' + $(if ($f.HasConstant) { '= ' + $f.Constant } else { '' })) }
  foreach ($m in $t.Methods) {
    [void]$sb.AppendLine('  ----- METHOD ' + $m.FullName + '  ilSize=' + $(if ($m.HasBody) { $m.Body.CodeSize } else { 'n/a' }) + ' eh=' + $(if ($m.HasBody) { $m.Body.ExceptionHandlers.Count } else { 0 }))
    if ($m.HasBody) {
      foreach ($i in $m.Body.Instructions) { [void]$sb.AppendLine(('    IL_{0:X4}: {1,-22} {2}' -f $i.Offset, $i.OpCode.Name, $i.Operand)) }
    }
  }
  foreach ($n in $t.NestedTypes) { Emit-Type $n }
}
$target = $asm.MainModule.Types | Where-Object { $_.FullName -eq $TypeName }
if (-not $target) { 'type not found: ' + $TypeName; exit 1 }
Emit-Type $target
[IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
'wrote ' + $OutFile + ' chars=' + $sb.Length
