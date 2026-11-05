param([string]$Game = 'C:\Games\Alta\A Township Tale')
$ErrorActionPreference = 'Stop'

$mgd   = Join-Path $Game 'A Township Tale_Data\Managed'
$ml    = Join-Path $Game 'MelonLoader\net35'
$csc   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$here  = Split-Path -Parent $MyInvocation.MyCommand.Path
$out   = Join-Path $here 'build'
$dll   = Join-Path $out 'ArmorRestored.dll'
$pack  = Join-Path $here 'assets\armor.pack'
$check = Join-Path $here 'tools\check_style.py'

foreach ($need in @($mgd, $ml, $csc, $pack)) { if (-not (Test-Path $need)) { throw "missing $need" } }
$py = Get-Command py, python -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $py) { throw 'the style check needs Python 3 (py or python)' }

& $py.Source $check $here
if ($LASTEXITCODE -ne 0) { throw 'style check failed' }

New-Item -ItemType Directory -Force $out | Out-Null
$refs = @(Get-ChildItem (Join-Path $mgd '*.dll') | ForEach-Object { "/r:$($_.FullName)" })
$refs += "/r:$(Join-Path $ml 'MelonLoader.dll')"
$refs += "/r:$(Join-Path $ml '0Harmony.dll')"
$src = @(Get-ChildItem (Join-Path $here 'src\*.cs') | ForEach-Object { $_.FullName })

& $csc /target:library /nostdlib+ /noconfig /unsafe- /optimize+ /warn:4 /nologo "/out:$dll" $refs "/resource:$pack,ArmorRestored.armor.pack" $src
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }

$asm = [System.Reflection.Assembly]::ReflectionOnlyLoad([System.IO.File]::ReadAllBytes($dll))
if (@($asm.GetReferencedAssemblies() | ForEach-Object { $_.Name }) -contains 'TavernLib') { throw 'ArmorRestored must not reference TavernLib' }

& $py.Source $check $here $dll
if ($LASTEXITCODE -ne 0) { throw 'DLL check failed' }

$version = [regex]::Match((Get-Content -Raw (Join-Path $here 'src\Main.cs')), 'Version = "([^"]+)"').Groups[1].Value
if (-not $version) { throw 'no Version in src\Main.cs' }
$zip = Join-Path $out "ArmorRestored-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $dll, (Join-Path $here 'README.md') -DestinationPath $zip
Get-Item $dll, $zip | Select-Object Name, Length, LastWriteTime
