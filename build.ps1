# 一键编译：把 dsh-usage.cs 编成单文件 exe（需要 Windows 自带的 .NET Framework 4.x）
# 用法：右键“使用 PowerShell 运行”，或在 PowerShell 里执行 .\build.ps1
param(
    [string]$Out
)

$root = $PSScriptRoot
if (-not $Out) { $Out = Join-Path $root 'DSH记账.exe' }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '没找到 csc.exe，请先安装 .NET Framework 4.x' }

$src  = Join-Path $root 'dsh-usage.cs'
$icon = Join-Path $root 'dsh.ico'
$libs = $root

$dlls = @(
    'ZstdSharp',
    'System.Memory',
    'System.Buffers',
    'System.Numerics.Vectors',
    'System.Runtime.CompilerServices.Unsafe',
    'System.Threading.Tasks.Extensions'
)

$refs = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Windows.Forms.dll',
    '/reference:System.Drawing.dll',
    '/reference:System.Xml.dll'
)
foreach ($d in $dlls) { $refs += ('/reference:' + (Join-Path $libs ($d + '.dll'))) }

# 6 个依赖库作为内嵌资源打进 exe，运行时释放到临时目录再加载 —— 保证分发只有一个文件
$res = @()
foreach ($d in $dlls) { $res += ('/resource:' + (Join-Path $libs ($d + '.dll')) + ',' + $d) }

$cargs = @(
    '/nologo',
    '/target:winexe',
    '/optimize+',
    ('/out:' + $Out),
    ('/win32icon:' + $icon),
    $src
) + $refs + $res

& $csc @cargs
if ($LASTEXITCODE -ne 0) { throw "编译失败（exit $LASTEXITCODE）" }
Write-Host ('编译完成：' + $Out + '  (' + [int]((Get-Item $Out).Length / 1024) + ' KB)')

