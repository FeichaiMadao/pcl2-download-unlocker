
param(
  [Parameter(Mandatory=$true)][string]$Exe,
  [Parameter(Mandatory=$true)][string]$Mode,
  [string]$Pattern = '',
  [string]$TypeName = '',
  [string]$MethodName = '',
  [string]$OutFile = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
foreach ($n in @('PresentationFramework','PresentationCore','WindowsBase','System.Xaml','System.Windows.Forms','System.Drawing','System.IO.Compression','System.IO.Compression.FileSystem','System.Net.Http','System.Runtime.Serialization','System.ServiceModel','System.Web.Extensions','System.Xml','System.Xml.Linq','Accessibility')) { try { Add-Type -AssemblyName $n -ErrorAction Stop } catch {} }
try { $asm = [Reflection.Assembly]::LoadFrom($Exe) }
catch { $asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($Exe)) }
$mod = $asm.ManifestModule

$opmap = @{}
foreach ($f in [Reflection.Emit.OpCodes].GetFields([Reflection.BindingFlags]'Public,Static')) {
  $op = $f.GetValue($null)
  $k = [int]$op.Value
  if ($op.Size -eq 2) { $k = 0xFE00 -bor ($k -band 0xFF) }
  $opmap[$k] = $op
}

function Get-Types() {
  try { return $asm.GetTypes() } catch [Reflection.ReflectionTypeLoadException] { return $_.Exception.Types | Where-Object { $_ } }
}
function Get-OperandSize($op, $il, $pos) {
  switch ($op.OperandType) {
    'InlineNone' { return 0 }
    'ShortInlineVar' { return 1 }
    'ShortInlineI' { return 1 }
    'ShortInlineBrTarget' { return 1 }
    'InlineVar' { return 2 }
    'InlineBrTarget' { return 4 }
    'InlineI' { return 4 }
    'InlineField' { return 4 }
    'InlineMethod' { return 4 }
    'InlineSig' { return 4 }
    'InlineString' { return 4 }
    'InlineTok' { return 4 }
    'InlineType' { return 4 }
    'InlineI8' { return 8 }
    'InlineR' { return 8 }
    'ShortInlineR' { return 4 }
    'InlineSwitch' { $n = [BitConverter]::ToInt32($il, $pos); return 4 + 4*$n }
    default { return 0 }
  }
}
function Disassemble([Reflection.MethodBase]$mb) {
  $body = $mb.GetMethodBody()
  if ($null -eq $body) { return $null }
  $il = $body.GetILAsByteArray()
  $res = New-Object System.Collections.Generic.List[object]
  $pos = 0
  while ($pos -lt $il.Length) {
    $start = $pos
    $b0 = $il[$pos]; $pos++
    if ($b0 -eq 0xFE) { $b1 = $il[$pos]; $pos++; $key = 0xFE00 -bor $b1 } else { $key = [int]$b0 }
    $op = $opmap[$key]
    if ($null -eq $op) { [void]$res.Add([pscustomobject]@{ Off=$start; Len=1; Text=('db 0x{0:X2}' -f $key) }); continue }
    $size = Get-OperandSize $op $il $pos
    $text = $op.Name
    if ($op.OperandType -eq 'InlineString') {
      $tok = [BitConverter]::ToInt32($il, $pos); $pos += 4
      try { $text = $op.Name + ' "' + $mod.ResolveString($tok) + '" #0x' + ('{0:X8}' -f $tok) } catch { $text = $op.Name + ' #0x' + ('{0:X8}' -f $tok) }
    }
    elseif ($op.OperandType -eq 'InlineMethod') {
      $tok = [BitConverter]::ToInt32($il, $pos); $pos += 4
      try { $text = $op.Name + ' ' + $mod.ResolveMethod($tok).ToString() + ' #0x' + ('{0:X8}' -f $tok) } catch { $text = $op.Name + ' #0x' + ('{0:X8}' -f $tok) }
    }
    elseif ($op.OperandType -eq 'InlineField') {
      $tok = [BitConverter]::ToInt32($il, $pos); $pos += 4
      try { $text = $op.Name + ' ' + $mod.ResolveField($tok).ToString() + ' #0x' + ('{0:X8}' -f $tok) } catch { $text = $op.Name + ' #0x' + ('{0:X8}' -f $tok) }
    }
    elseif ($op.OperandType -eq 'InlineType') {
      $tok = [BitConverter]::ToInt32($il, $pos); $pos += 4
      try { $text = $op.Name + ' ' + $mod.ResolveType($tok).ToString() + ' #0x' + ('{0:X8}' -f $tok) } catch { $text = $op.Name + ' #0x' + ('{0:X8}' -f $tok) }
    }
    elseif ($op.OperandType -eq 'InlineBrTarget') { $t = [BitConverter]::ToInt32($il, $pos); $pos += 4; $text = $op.Name + ' IL_' + ('{0:X4}' -f ($pos + $t)) }
    elseif ($op.OperandType -eq 'ShortInlineBrTarget') { $t = [sbyte]$il[$pos]; $pos += 1; $text = $op.Name + ' IL_' + ('{0:X4}' -f ($pos + $t)) }
    elseif ($op.OperandType -eq 'InlineI') { $t = [BitConverter]::ToInt32($il, $pos); $pos += 4; $text = $op.Name + ' ' + $t }
    elseif ($op.OperandType -eq 'ShortInlineI') { $t = [sbyte]$il[$pos]; $pos += 1; $text = $op.Name + ' ' + $t }
    elseif ($op.OperandType -eq 'InlineVar') { $t = [BitConverter]::ToUInt16($il, $pos); $pos += 2; $text = $op.Name + ' V' + $t }
    elseif ($op.OperandType -eq 'ShortInlineVar') { $t = $il[$pos]; $pos += 1; $text = $op.Name + ' V' + $t }
    else { $pos += $size }
    [void]$res.Add([pscustomobject]@{ Off=$start; Len=($pos-$start); Text=$text })
  }
  return ,@($res, $il)
}

switch ($Mode) {
  'consts' {
    foreach ($t in Get-Types) {
      foreach ($f in $t.GetFields([Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly')) {
        if ($f.IsLiteral -and $Pattern -and ($f.Name -match $Pattern -or $t.FullName -match $Pattern)) {
          $v = $null; try { $v = $f.GetRawConstantValue() } catch { $v = '<err>' }
          '{0} :: {1} = {2}' -f $t.FullName, $f.Name, $v
        }
      }
    }
  }
  'types' { foreach ($t in Get-Types) { if (-not $Pattern -or $t.FullName -match $Pattern) { $t.FullName } } }
  'members' {
    foreach ($t in Get-Types) {
      if ($TypeName -and $t.FullName -notmatch $TypeName) { continue }
      '===== ' + $t.FullName
      $flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
      foreach ($f in $t.GetFields($flags)) {
        $v = ''; if ($f.IsLiteral) { try { $v = ' = ' + $f.GetRawConstantValue() } catch { } }
        '  FIELD {0}{1}{2} : {3}{4}' -f ($(if($f.IsStatic){'static '}else{''})), ($(if($f.IsLiteral){'const '}elseif($f.IsInitOnly){'readonly '}else{''})), $f.Name, $f.FieldType.Name, $v
      }
      foreach ($m in $t.GetMethods($flags)) { '  METH  ' + $m.Name }
    }
  }
  { $_ -eq 'il' -or $_ -eq 'hex' } {
    $withHex = ($Mode -eq 'hex')
    $sb = New-Object System.Text.StringBuilder
    foreach ($t in Get-Types) {
      if ($TypeName -and $t.FullName -notmatch $TypeName) { continue }
      $flags = [Reflection.BindingFlags]'Public,NonPublic,Static,Instance,DeclaredOnly'
      $methods = @()
      try { $methods += $t.GetMethods($flags) } catch {}
      try { $methods += $t.GetConstructors($flags) } catch {}
      foreach ($m in $methods) {
        if ($MethodName -and $m.Name -notmatch $MethodName) { continue }
        $r = Disassemble $m
        if ($null -eq $r) { continue }
        $ins = $r[0]; $il = $r[1]
        [void]$sb.AppendLine('===== ' + $t.FullName + '::' + $m.Name + '  ilSize=' + $il.Length)
        foreach ($i in $ins) {
          if ($withHex) {
            $hex = (($i.Off..($i.Off+$i.Len-1)) | ForEach-Object { '{0:X2}' -f $il[$_] }) -join ' '
            [void]$sb.AppendLine(('  IL_{0:X4}: {1,-32} {2}' -f $i.Off, $hex, $i.Text))
          } else {
            [void]$sb.AppendLine(('  IL_{0:X4}: {1}' -f $i.Off, $i.Text))
          }
        }
      }
    }
    if ($OutFile) { [IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false))); 'wrote ' + $OutFile + ' chars=' + $sb.Length } else { $sb.ToString() }
  }
  default { 'unknown mode: ' + $Mode }
}
