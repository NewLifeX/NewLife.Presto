#!/usr/bin/env bash
# 在 Linux（含 WSL）上跑项目全量测试：构建 → 单元 + 真库集成 → 控制台冒烟 → AOT 冒烟。
# 覆盖率报告写入仓库 TestResults，供 Windows 侧 tools/check-coverage.ps1 校验。
#
# 用法：
#   bash tools/test-linux.sh
#   bash tools/test-linux.sh --skip-aot
#   TRINO_TEST_CONNSTR='...' PRESTO_TEST_CONNSTR='...' bash tools/test-linux.sh
#
# 环境要求：.NET SDK 10+；NativeAOT 需要 clang 与 zlib1g-dev。
set -euo pipefail

SKIP_AOT=0
for arg in "$@"; do
    case "$arg" in
        --skip-aot) SKIP_AOT=1 ;;
        *) echo "未知参数：$arg" >&2; exit 2 ;;
    esac
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# 在临时目录构建，避免 bin/obj 与 Windows 侧产物混用
tar -C "$ROOT" --exclude=./.git --exclude=./.vs --exclude='*/bin' --exclude='*/obj' \
    --exclude='*/Bin' --exclude=./TestResults -cf - . | tar -xf - -C "$WORK"
cd "$WORK"

if ! dotnet --list-sdks | grep -q '^11\.'; then
    echo "未检测到 .NET 11 SDK，只构建到 net10.0"
    sed -i 's/net10.0;net11.0/net10.0/' NewLife.Presto/NewLife.Presto.csproj
fi

echo "== 构建 =="
dotnet build NewLife.Presto.slnx -c Release --nologo

echo "== 单元测试 + 真库集成 =="
dotnet test UnitTest/UnitTest.csproj -c Release --no-build --nologo \
    --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory TestResults

echo "== 控制台冒烟（假服务端） =="
dotnet run --project Test/Test.csproj -c Release --no-build --framework net10.0

for name in TRINO_TEST_CONNSTR PRESTO_TEST_CONNSTR; do
    if [ -n "${!name:-}" ]; then
        echo "== 控制台冒烟（$name） =="
        dotnet run --project Test/Test.csproj -c Release --no-build --framework net10.0 -- --connstr "${!name}"
    else
        echo "跳过真库冒烟：未设置 $name"
    fi
done

echo "== AOT 冒烟（JIT 基线） =="
dotnet run -c Release --project AotSmoke/AotSmoke.csproj --no-build -- --no-db

if [ "$SKIP_AOT" -eq 0 ]; then
    case "$(uname -m)" in
        aarch64|arm64) RID=linux-arm64 ;;
        *) RID=linux-x64 ;;
    esac
    echo "== NativeAOT 发布并运行（$RID） =="
    rm -rf out
    dotnet publish AotSmoke/AotSmoke.csproj -c Release -r "$RID" -o out 2>&1 | tee publish.log
    if grep -E 'warning IL[0-9]{4}' publish.log | grep .; then
        echo "NativeAOT 发布存在 IL 告警" >&2
        exit 1
    fi
    ./out/AotSmoke --no-db
    for name in TRINO_TEST_CONNSTR PRESTO_TEST_CONNSTR; do
        if [ -n "${!name:-}" ]; then
            echo "== AOT 冒烟（原生 $RID / $name） =="
            ./out/AotSmoke --connstr "${!name}"
        fi
    done
fi

mkdir -p "$ROOT/TestResults"
cp TestResults/*/coverage.cobertura.xml "$ROOT/TestResults/linux-coverage.cobertura.xml"
echo
echo "覆盖率报告：TestResults/linux-coverage.cobertura.xml"
echo "在 Windows 侧校验：pwsh -File tools/check-coverage.ps1"
echo "全部测试通过"