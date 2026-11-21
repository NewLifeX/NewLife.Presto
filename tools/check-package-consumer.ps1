# NuGet 消费端冒烟：在临时工程引用本地 nupkg，验证包内容、连接串、协议头、异常与本地化
#
# 用法：
#   pwsh -File tools/check-package-consumer.ps1
#   pwsh -File tools/check-package-consumer.ps1 -PackageDir out -PackageVersion 1.0.2026.1120
#   pwsh -File tools/check-package-consumer.ps1 -Keep
param(
    [String]$Root = (Split-Path -Parent $PSScriptRoot),
    [String]$PackageDir,
    [String]$PackageVersion,
    [Switch]$Keep
)

$ErrorActionPreference = "Stop"

$work = Join-Path $Root ".tmp\package-consumer"
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

if (!$PackageDir) {
    $PackageDir = Join-Path $work "packages"
    New-Item -ItemType Directory -Path $PackageDir | Out-Null
    foreach ($project in @("NewLife.Presto\NewLife.Presto.csproj", "NewLife.Presto.EntityFrameworkCore\NewLife.Presto.EntityFrameworkCore.csproj", "NewLife.Presto.EntityFramework\NewLife.Presto.EntityFramework.csproj")) {
        Write-Host "打包 $project ..."
        & dotnet pack (Join-Path $Root $project) -c Release -o $PackageDir --nologo
        if ($LASTEXITCODE -ne 0) { throw "dotnet pack 失败：$project" }
    }
}

$nupkg = Get-ChildItem -LiteralPath $PackageDir -Filter "NewLife.Presto.*.nupkg" -File |
    Where-Object { $_.BaseName -match '^NewLife\.Presto\.\d' } |
    Sort-Object Name | Select-Object -Last 1
if (!$nupkg) { throw "未找到 NewLife.Presto nupkg：$PackageDir" }
$PackageDir = (Resolve-Path -LiteralPath $PackageDir).Path

$pkgVersion = if ($PackageVersion) { $PackageVersion } else { $nupkg.BaseName.Substring("NewLife.Presto.".Length) }
Write-Host "包文件：$($nupkg.FullName)"
Write-Host "包版本：$pkgVersion"

# 校验包内关键文件，避免发布缺少 README、图标或目标程序集
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($package in Get-ChildItem -LiteralPath $PackageDir -Filter "NewLife.Presto*.nupkg" -File) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entries = $zip.Entries | ForEach-Object FullName
        foreach ($required in @("README.nuget.md", "leaf.png")) {
            if ($entries -notcontains $required) { throw "$($package.Name) 缺少 $required" }
        }
        $assembly = $package.BaseName -replace '\.\d+(\.[0-9A-Za-z\-]+)*$', ''
        if (!($entries | Where-Object { $_ -like "lib/*/$assembly.dll" })) { throw "$($package.Name) 缺少 lib/*/$assembly.dll" }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host "包内容校验通过：$($package.Name)"
}

$consumer = Join-Path $work "Consumer"
& dotnet new console -n Consumer -o $consumer --framework net10.0 --force | Out-Null
if ($LASTEXITCODE -ne 0) { throw "创建消费工程失败" }

$nugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$PackageDir" />
    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@
[IO.File]::WriteAllText((Join-Path $consumer "NuGet.config"), $nugetConfig, (New-Object System.Text.UTF8Encoding($false)))

& dotnet add (Join-Path $consumer "Consumer.csproj") package NewLife.Presto --version $pkgVersion
if ($LASTEXITCODE -ne 0) { throw "添加本地包失败" }

& dotnet add (Join-Path $consumer "Consumer.csproj") package NewLife.Presto.EntityFrameworkCore --version $pkgVersion
if ($LASTEXITCODE -ne 0) { throw "添加 EF Core 提供程序包失败" }

$program = @'
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using NewLife;
using NewLife.Presto;
using NewLife.Presto.EntityFrameworkCore;

var builder = new PrestoConnectionStringBuilder("Server=presto.local;Port=8443;Database=hive;Owner=default;TrinoHeaders=true");
if (builder.Host != "presto.local") throw new Exception("Host alias failed");
if (builder.Port != 8443) throw new Exception("Port failed");
if (builder.Catalog != "hive" || builder.Schema != "default") throw new Exception("Catalog/Schema alias failed");
if (builder.Protocol != PrestoProtocol.Trino) throw new Exception("TrinoHeaders failed");
if (PrestoHeaders.User(builder.Protocol) != "X-Trino-User") throw new Exception("Header prefix failed");
if (PrestoException.SubmitFailed(503).HttpStatusCode != 503) throw new Exception("Exception status failed");

var assembly = typeof(PrestoConnectionStringBuilder).Assembly;
var title = assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title ?? "";
if (String.IsNullOrWhiteSpace(title)) throw new Exception("AssemblyTitle is empty");

// EF Core 提供程序：构建模型即可验证包引用、服务注册与类型映射，不需要连接数据库
var options = new DbContextOptionsBuilder<SmokeContext>()
    .UsePresto("Host=presto.local;Port=8080;Catalog=hive;Schema=default;User=test")
    .Options;
using (var context = new SmokeContext(options))
{
    var count = context.Model.GetEntityTypes().Count();
    if (count == 0) throw new Exception("EF Core model has no entity types");
}

Localization.Culture = CultureInfo.GetCultureInfo("zh-Hans");
var zh = Localization.Translate("Host cannot be empty.");
if (zh == "Host cannot be empty.") throw new Exception("zh-Hans translation missing");
var zhTitle = Localization.Translate(title);
if (zhTitle == title) throw new Exception("zh-Hans assembly title translation missing");

Localization.Culture = CultureInfo.GetCultureInfo("ja");
var ja = Localization.Translate("Host cannot be empty.");
if (ja == "Host cannot be empty.") throw new Exception("ja translation missing");

Localization.Culture = CultureInfo.GetCultureInfo("sw-KE");
var fallback = Localization.Translate("Host cannot be empty.");
if (fallback != "Host cannot be empty.") throw new Exception("English fallback broken");

Console.WriteLine("OK: " + zh + " | " + ja + " | " + zhTitle);

public sealed class SmokeContext : DbContext
{
    public SmokeContext(DbContextOptions<SmokeContext> options) : base(options) { }

    public DbSet<SmokeItem> Items => Set<SmokeItem>();
}

public sealed class SmokeItem
{
    public Int32 Id { get; set; }

    public String? Name { get; set; }
}
'@
[IO.File]::WriteAllText((Join-Path $consumer "Program.cs"), $program, (New-Object System.Text.UTF8Encoding($false)))

Push-Location $consumer
try {
    & dotnet run -c Release
    if ($LASTEXITCODE -ne 0) { throw "消费端运行失败" }
}
finally {
    Pop-Location
}

if (!$Keep) {
    Remove-Item -LiteralPath $work -Recurse -Force
    Write-Host "已清理临时工程（-Keep 可保留）"
}
Write-Host "NuGet 消费端冒烟通过" -ForegroundColor Green
