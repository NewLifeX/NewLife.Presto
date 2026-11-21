# 全量测试：构建、单元 + 真库集成、覆盖率、控制台冒烟、AOT 冒烟、多语言与 README 门禁
#
# 用法：
#   pwsh -File tools/test-all.ps1
#   $env:TRINO_TEST_CONNSTR='Host=localhost;Port=8080;Catalog=tpch;Schema=tiny;User=test;Protocol=Trino'
#   $env:PRESTO_TEST_CONNSTR='Host=localhost;Port=8081;Catalog=tpch;Schema=tiny;User=test'
#   pwsh -File tools/test-all.ps1
#
# 说明：
#   - 未设置连接串环境变量时，真库集成测试自动跳过，其余测试照常执行。
#   - -SkipAot 跳过 NativeAOT 发布（缺少 MSVC / clang 工具链时使用）。
#   - -SkipLocalization 跳过词表与 README 门禁。
#   - -SkipPackage 跳过 NuGet 打包与消费端冒烟。
#   - Linux/WSL 环境请改用 tools/test-linux.sh（自动在临时目录构建，避免混用 Windows 产物）。
param(
    [Switch]$SkipAot,
    [Switch]$SkipLocalization,
    [Switch]$SkipPackage,
    [String]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

if ($PSVersionTable.PSVersion.Major -lt 6) { throw "请使用 PowerShell 7+（pwsh）运行本脚本。" }

$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root "TestResults"

function Invoke-Step([String]$Name, [ScriptBlock]$Action) {
    Write-Host "`n== $Name ==" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name 失败（exit $LASTEXITCODE）" }
}

Push-Location $root
try {
    Invoke-Step "构建" { dotnet build NewLife.Presto.slnx -c $Configuration --nologo -v q }

    if (Test-Path $results) { Remove-Item -Recurse -Force $results }

    Invoke-Step "单元测试 + 真库集成 + 覆盖率" {
        dotnet test UnitTest/UnitTest.csproj -c $Configuration --no-build --nologo `
            --collect:"XPlat Code Coverage" --settings coverlet.runsettings `
            --results-directory $results --logger "trx;LogFileName=unittest.trx"
    }

    Invoke-Step "覆盖率门禁" { pwsh -NoProfile -File (Join-Path $root "tools/check-coverage.ps1") -LineThreshold 100 -BranchThreshold 95 }

    Invoke-Step "控制台冒烟（假服务端）" { dotnet run --project Test/Test.csproj -c $Configuration --no-build }

    foreach ($name in @("TRINO_TEST_CONNSTR", "PRESTO_TEST_CONNSTR")) {
        $connstr = [Environment]::GetEnvironmentVariable($name)
        if ([String]::IsNullOrWhiteSpace($connstr)) {
            Write-Host "跳过真库冒烟：未设置 $name" -ForegroundColor Yellow
            continue
        }

        Invoke-Step "控制台冒烟（$name）" { dotnet run --project Test/Test.csproj -c $Configuration --no-build -- --connstr $connstr }
    }

    Invoke-Step "AOT 冒烟（JIT 基线）" { dotnet run -c $Configuration --project AotSmoke/AotSmoke.csproj --no-build -- --no-db }

    if (!$SkipAot) {
        $rid = if ($IsWindows) { "win-x64" } else { "linux-x64" }
        $out = Join-Path $root ".tmp-aot-out"
        $log = Join-Path $root ".tmp-aot-publish.log"
        if (Test-Path $out) { Remove-Item -Recurse -Force $out }

        Invoke-Step "NativeAOT 发布（$rid）" {
            dotnet publish AotSmoke/AotSmoke.csproj -c $Configuration -r $rid -o $out 2>&1 | Tee-Object -FilePath $log
        }

        $warnings = @(Select-String -Path $log -Pattern "warning IL[0-9]{4}")
        if ($warnings.Count -gt 0) {
            $warnings | ForEach-Object { Write-Host $_.Line -ForegroundColor Red }
            throw "NativeAOT 发布存在 $($warnings.Count) 条 IL 告警"
        }

        $binary = Join-Path $out $(if ($IsWindows) { "AotSmoke.exe" } else { "AotSmoke" })
        Invoke-Step "AOT 冒烟（原生 $rid）" { & $binary --no-db }

        foreach ($name in @("TRINO_TEST_CONNSTR", "PRESTO_TEST_CONNSTR")) {
            $connstr = [Environment]::GetEnvironmentVariable($name)
            if ([String]::IsNullOrWhiteSpace($connstr)) { continue }

            Invoke-Step "AOT 冒烟（原生 $rid / $name）" { & $binary --connstr $connstr }
        }

        Remove-Item -Recurse -Force $out
        Remove-Item -Force $log
    }

    if (!$SkipLocalization) {
        Invoke-Step "多语言与 README 门禁" { pwsh -NoProfile -File (Join-Path $root "tools/verify-localization.ps1") -RequireTranslated -SkipBuild -SkipTests }
    }

    if (!$SkipPackage) {
        Invoke-Step "NuGet 打包与消费端冒烟" { pwsh -NoProfile -File (Join-Path $root "tools/check-package-consumer.ps1") }
    }

    Write-Host "`n全部测试通过" -ForegroundColor Green
}
finally {
    Pop-Location
}
