# 解析 Cobertura 覆盖率报告并执行质量门禁
#
# 默认只统计 NewLife.Presto 主程序集，源生成文件已在 coverlet.runsettings 中排除。
# 用法：
#   pwsh -File tools/check-coverage.ps1
#   pwsh -File tools/check-coverage.ps1 -Path TestResults -LineThreshold 100 -BranchThreshold 95
param(
    [String]$Path = (Join-Path (Split-Path -Parent $PSScriptRoot) "TestResults"),
    [Double]$LineThreshold = 100,
    [Double]$BranchThreshold = 95
)

$ErrorActionPreference = "Stop"

$files = if (Test-Path -LiteralPath $Path -PathType Leaf) {
    @(Get-Item -LiteralPath $Path)
}
else {
    @(Get-ChildItem -LiteralPath $Path -Recurse -Filter "coverage.cobertura.xml" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending)
}
if ($files.Count -eq 0) { throw "未找到覆盖率报告：$Path" }

$file = $files[0]
[xml]$xml = Get-Content -LiteralPath $file.FullName -Raw
$coverage = $xml.coverage
if ($null -eq $coverage) { throw "覆盖率报告格式无效：$($file.FullName)" }

$lineValid = [Int32]$coverage.'lines-valid'
$lineCovered = [Int32]$coverage.'lines-covered'
$branchValid = [Int32]$coverage.'branches-valid'
$branchCovered = [Int32]$coverage.'branches-covered'
if ($lineValid -le 0) { throw "覆盖率报告没有有效行：$($file.FullName)" }

$lineRate = if ($lineValid -gt 0) { $lineCovered * 100.0 / $lineValid } else { 100.0 }
$branchRate = if ($branchValid -gt 0) { $branchCovered * 100.0 / $branchValid } else { 100.0 }

Write-Host ("覆盖率报告：{0}" -f $file.FullName)
Write-Host ("行覆盖：{0:N2}%（{1}/{2}）" -f $lineRate, $lineCovered, $lineValid)
Write-Host ("分支覆盖：{0:N2}%（{1}/{2}）" -f $branchRate, $branchCovered, $branchValid)

if ($lineRate + 0.0001 -lt $LineThreshold) {
    Write-Host ("::error::行覆盖率 {0:N2}% 低于门禁 {1:N2}%" -f $lineRate, $LineThreshold)
    throw "行覆盖率不达标"
}
if ($branchRate + 0.0001 -lt $BranchThreshold) {
    Write-Host ("::error::分支覆盖率 {0:N2}% 低于门禁 {1:N2}%" -f $branchRate, $BranchThreshold)
    throw "分支覆盖率不达标"
}

Write-Host "覆盖率门禁通过" -ForegroundColor Green
