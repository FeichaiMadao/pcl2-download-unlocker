
param([string]$Exe, [string]$OutFile, [string]$CecilPath)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$null = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($CecilPath))
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Exe)
$sb = New-Object System.Text.StringBuilder
$keys = @('SystemSystemUpdate','ToolUpdateSnapshot','ToolUpdateRelease','ToolUpdateSnapshotLast','ToolUpdateReleaseLast','IsUpdateWaitingRestart')
function Scan-Type($t) {
  foreach ($m in $t.Methods) {
    if (-not $m.HasBody) { continue }
    foreach ($i in $m.Body.Instructions) {
      $op = $i.Operand
      $hit = $null
      if ($op -is [Mono.Cecil.MethodReference]) {
        $dt = $op.DeclaringType
        if ($dt -and $dt.FullName -eq 'PCL.ModSecret' -and $op.Name -match 'Update|DownloadLatestPCL') { $hit = 'CALL ' + $op.Name }
      }
      if ($op -is [string] -and ($keys -contains $op)) { $hit = 'STR ' + $op }
      if ($hit) { [void]$sb.AppendLine(('{0}::{1}  IL_{2:X4}  {3}  {4}' -f $t.FullName, $m.Name, $i.Offset, $i.OpCode.Name, $hit)) }
    }
  }
  foreach ($n in $t.NestedTypes) { Scan-Type $n }
}
foreach ($t in $asm.MainModule.Types) { Scan-Type $t }
[IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
'wrote ' + $OutFile + ' chars=' + $sb.Length
