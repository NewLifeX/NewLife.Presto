# NewLife.Presto — Presto/Trino ADO.NET 驅動程式

[![AOT](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/aot.yml/badge.svg?label=AOT)](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/aot.yml)
![GitHub License](https://img.shields.io/github/license/NewLifeX/NewLife.Presto?logo=github)
![NuGet Downloads](https://img.shields.io/nuget/dt/NewLife.Presto?logo=nuget)
![NuGet](https://img.shields.io/nuget/v/NewLife.Presto?logo=nuget)
![NuGet prerelease](https://img.shields.io/nuget/vpre/NewLife.Presto?label=dev%20nuget&logo=nuget)
[![test](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/test.yml/badge.svg?branch=master)](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/test.yml)
[![locales](https://img.shields.io/badge/locales-33-2ea44f?logo=dotnet)](Doc/多语言本地化.md)
![platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![arch](https://img.shields.io/badge/arch-x64%20%7C%20arm64%20%7C%20loongarch64%20%7C%20riscv64-orange)
[![contributors](https://img.shields.io/github/contributors/NewLifeX/NewLife.Presto?logo=github)](https://github.com/NewLifeX/NewLife.Presto/graphs/contributors)

<!-- lang:start -->
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto** 是新生命開發團隊出品的純 C# PrestoDB / Trino ADO.NET 驅動程式。它使用官方 HTTP REST v1 用戶端通訊協定，僅依賴 `NewLife.Core`，**零第三方相依**，內建 **33 種執行階段語言**，目標框架涵蓋 `net45` ~ `net11`，`net8.0+` **支援 NativeAOT**。PrestoDB 與 Trino 共用一個套件，透過 `Protocol=Trino`（或 `TrinoHeaders=true`）切換要求標頭。

> **專案狀態**：已實作並通過測試——核心 ADO.NET 驅動 + 唯讀的 EF Core 與 EF6 Provider，建置為三個 NuGet 套件並由同一發佈流程打包推送。484 項核心單元測試、EF Provider 測試與真實 Trino/PrestoDB 整合全部通過；僅 Spooling 暫緩。見[功能清單](Doc/功能清单.md)。

**目錄**: [快速開始](#快速開始) · [為什麼選擇 NewLife.Presto](#為什麼選擇-newlifepresto) · [主要功能](#主要功能) · [平台支援](#平台支援) · [多語言](#多語言) · [文件](#文件) · [參與貢獻](#參與貢獻) · [生態版圖](#生態版圖) · [新生命開發團隊](#新生命開發團隊) · [授權](#授權)

---

## 快速開始

```bash
dotnet build NewLife.Presto.slnx -c Release
dotnet test UnitTest/UnitTest.csproj -c Release
dotnet run --project AotSmoke/AotSmoke.csproj -c Release -- --no-db
```

```text
Host=localhost;Port=8080;Catalog=hive;Schema=default;User=test;Password=xxx
```

```csharp
using NewLife.Presto;

using var conn = new PrestoConnection(
    "Host=localhost;Port=8080;Catalog=hive;Schema=default;User=test;Password=xxx");
await conn.OpenAsync();

using var cmd = conn.CreateCommand();
cmd.CommandText = "select count(*) from tpch.tiny.nation where nationkey > @minKey";
cmd.Parameters.Add(new PrestoParameter("@minKey", 0));

var count = await cmd.ExecuteScalarAsync();
```
```csharp
// Entity Framework Core (read-only)
var options = new DbContextOptionsBuilder<MyDbContext>()
    .UsePresto("Host=localhost;Port=8080;Catalog=hive;Schema=default;User=test")
    .Options;

// Entity Framework 6 (read-only)
DbConfiguration.SetConfiguration(new PrestoEFConfiguration());
```

---

## 為什麼選擇 NewLife.Presto

- **一個套件同時支援 PrestoDB 與 Trino**：共用同一套通訊協定實作，透過設定選擇 `X-Presto-*` / `X-Trino-*` 標頭。
- **XCode 優先**：工廠、連接字串別名、`@` 參數、中繼資料與能力邊界圍繞 XCode 的 Presto 模型設計。
- **零第三方相依**：只依賴 `NewLife.Core`；JSON、HTTP 與記錄來自 BCL 或 NewLife 技術棧。
- **全框架、全平台**：`net45` ~ `net11`，Windows / Linux / macOS，x64 / arm64，並提供 LoongArch64 與 musl 觀察期建置。
- **33 種執行階段語言**：例外、記錄與驅動程式中繼資料均已本地化，英文為源文與退回語言。
- **NativeAOT 就緒**：通訊協定與型別編解碼不使用執行階段反射；各支援平台均執行 NativeAOT 冒煙。

## 主要功能

<details>
<summary><b>連線與通訊協定</b>（6 項）</summary>

| 功能點 | 一句話特點 |
|--------|------------|
| 連接字串別名 | `Host`/`Server`/`Data Source`、`Catalog`/`Database`、`Schema`/`Owner` 與 XCode 寫法相容 |
| PrestoDB / Trino 雙標頭 | 預設 `Protocol=Presto`；Trino 351+ 用 `Protocol=Trino` 或 `TrinoHeaders=true` |
| 工作階段內容 | Catalog、Schema、使用者、來源、時區、工作階段屬性與自訂標頭 |
| 驗證 | Basic、JWT/Bearer 與閘道自訂標頭，敏感性資訊不寫入記錄 |
| TLS 與代理 | 預設嚴格驗證憑證；私有 CA 需明確放寬 |
| 共用 HTTP 傳輸 | 依端點重複使用 `HttpClient`，支援代理、壓縮與逾時 |

</details>

<details>
<summary><b>查詢與型別</b>（6 項）</summary>

| 功能點 | 一句話特點 |
|--------|------------|
| 語句生命週期 | `POST /v1/statement`、`nextUri` 輪詢與完成判定 |
| 前向相容狀態 | 狀態以字串保存，伺服器新增狀態不會破壞解析 |
| 暫時性重試 | 429/502/503/504，依 `Retry-After` 或指數退避 |
| `@name` 參數 | 用戶端取代，跳過字串、識別碼與註解 |
| 型別保真 | 布林、整數、decimal、日期時間、timestamp with time zone、UUID、IP 與二進位 |
| 複雜型別 | 首版將 `array`/`map`/`row`/`json` 傳回原始字串 |

</details>

<details>
<summary><b>整合與診斷</b>（6 項）</summary>

| 功能點 | 一句話特點 |
|--------|------------|
| `GetSchema` | `MetaDataCollections`、`Tables`、`Columns`、`DataTypes` |
| XCode 整合 | 預設驅動切換並保留舊驅動退回；不聲稱支援交易、DDL 或自增 |
| 多語言 | 33 種執行階段語言，編譯期產生，AOT 可用 |
| 診斷 | 例外與記錄保留查詢 ID、錯誤碼、SQLState 與耗時 |
| OpenTelemetry | 現代框架提供 `ActivitySource("NewLife.Presto")` |
| 封裝 | 強式命名、SourceLink、符號套件與 README.nuget.md |

</details>

## 平台支援

| 維度 | 支援範圍 |
|------|----------|
| 作業系統 | Windows、Linux（glibc 與 musl）、macOS |
| 指令集 | x64、arm64；LoongArch64 與 riscv64 由觀察期建置涵蓋 |
| 目標框架 | net45、net461、netstandard2.0、netstandard2.1、net6.0、net8.0、net10.0、net11.0 |
| NativeAOT | net8.0+ 裁剪/AOT 分析器零警告；win-x64、linux-x64、linux-arm64 為阻斷冒煙 |
| CI 矩陣 | Windows、Linux x64、Linux arm64、musl 容器、macOS 與 Windows arm64（觀察期） |

## 多語言

| 範圍 | 涵蓋 |
|------|------|
| 執行階段訊息 | 33 種語言（含英文）；缺少譯文時退回英文 |
| README | 由 `readme-languages.json` 產生 11 種語言 |
| 中繼資料 | 組件標題與描述使用英文源文，顯示時翻譯 |

> 詞表鍵就是英文源文。程式碼新增訊息後，需在每個語言檔補齊同名鍵；`tools/check-locales.ps1 -RequireTranslated` 會在 CI 中驗證鍵集合、佔位符與翻譯完整度。

## 文件

- [需求文档](Doc/需求文档.md) — 目標與範圍
- [功能清单](Doc/功能清单.md) — 實作/測試/註解三維狀態
- [架构设计](Doc/架构设计.md) — 元件、通訊協定流程、型別對應與設計決策
- [竞品分析](Doc/竞品分析.md) — 參考實作、功能矩陣與差距

## 參與貢獻

- 建置：`dotnet build NewLife.Presto.slnx -c Release`
- 測試：`dotnet test UnitTest/UnitTest.csproj -c Release`
- 多語言：`pwsh -File tools/check-locales.ps1 -RequireTranslated`
- 遵循 NewLife C# 慣例，執行階段訊息必須進入詞表。
- 非發版流程不得修改 `ChangeLog.md` 與套件發行說明。

## 生態版圖

NewLife 生態系按相依分層，各專案獨立版本、獨立發行。下面的版圖在所有 NewLife 儲存庫中逐字一致；每個專案連結到程式碼儲存庫與主 NuGet 套件。

| 專案 | 一句話定位 | 下載量 |
| --- | --- | --- |
| **基礎底座** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | 日誌、設定、快取、網路、序列化、排程與 APM 追蹤。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | 資料中介層：單表百億級、自動分表、讀寫分離，支援 10 多種資料庫。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | 高效能網路庫：單機 177 萬 pps、14.5 Gbps（TCP 1KB，2026-10 實測），400 萬 Tcp 長連線。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | 協定通訊與 RPC 框架，面向 CS 應用程式與物聯網裝置。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | 將 .NET 應用程式安裝為作業系統常駐服務：Windows 服務與 Linux systemd。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | `dotnet new` 專案範本：主控台、Web、API、服務、資料與物聯網。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **資料儲存與訊息** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Redis 用戶端：微秒級延遲、百萬級輸送量，內建訊息佇列。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | 純受控 RocketMQ 用戶端，支援 Apache RocketMQ 與阿里雲訊息佇列。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT 用戶端與服務端，含阿里雲物聯網接入。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | C# 混合式資料庫：內嵌或網路雙模，關係型、時序、訊息佇列與 KV 於一體。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | 建構於 NewLife 資料堆疊之上的 MySql 驅動與輔助層。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web、AI 與分散式** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | 快速後台開發框架：權限、SSO、OAuth 服務端與 CRUD 樣板。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | 分散式服務平台：節點管理、APM、設定中心、註冊中心與發佈中心。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | 分散式任務與即時資料排程平台，支援分片、重試與回溯補算。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | 統一接入 46 家大模型服務商，附帶開箱即用的 ChatAI Web 對話應用。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **工業與物聯網** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | 物聯網標準庫，定義通訊協定契約與裝置抽象。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | 建構於 NewLife.IoT 之上的 Modbus TCP/RTU/ASCII 實作。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | 建構於 NewLife.IoT 之上的西門子 PLC 協定。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | 建構於 NewLife.IoT 之上的歐姆龍 PLC 協定。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | 建構於 NewLife.IoT 之上的三菱 Melsec PLC 協定。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | 工業資料存取的 OPC 用戶端。 | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet 樓宇自動化協定堆疊。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | 開源輕量級物聯網平台，含閘道與裝置管理。 | — |
| **車聯網** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 車輛定位協定：70+ 訊息體，涵蓋 2011/2013/2019 三個版本。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | JT/T 809 部標平台互聯協定，面向運輸管理部門。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 車載視訊：RTP 解析、FLV/fMP4 轉封裝與直播推送。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **領域元件** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | 一個套件涵蓋 Excel、Word、PPT、PDF 等 15+ 文件格式。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | 跨平台音訊：編解碼、裝置 IO、DSP、容器與串流處理。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | 視訊編解碼、封裝格式、影格處理與串流。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | 百度／高德／騰訊／天地圖統一介面，支援地理編碼與座標轉換。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | IPv4 歸屬地：離線記憶體映射資料庫 + 線上 API 雙來源。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **工具與產品** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | 開發者工具箱：網路、序列埠、加解密、正規表示式、Modbus 與 MQTT。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | 自架 Git 原始碼管理系統。 | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | 從較舊的宿主啟動時，自動安裝相符的 .NET 執行階段。 | — |

> 徽章取各專案主套件；主題套件、擴充套件與衛星套件見對應 NuGet 頁面。未在表中列出的 30 多個套件（FTP、LoRa、Office 擴充、各類協定驅動等）見 [NewLifeX 組織](https://github.com/NewLifeX) 與 [newlifex.com](https://newlifex.com)。

## 新生命開發團隊

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

新生命團隊成立於 2002 年，二十餘年專注 .NET 與物聯網技術生態，主導開源專案近百個（生態版圖收錄 34 個主幹專案），為電力、物流、醫療、工控、網際網路等行業提供軟硬體一體化解決方案；以「學無先後達者為師」為信條。

開源元件在 NuGet 累計下載 <!--nuget-total:start-->6.48M<!--nuget-total:end--> 次（全生態口徑，截至 2026-10）。

網站：https://newlifex.com · 開源組織：https://github.com/NewLifeX

<details>
<summary>微信公眾號：智能大石头（掃碼關注）</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## 授權

[MIT](LICENSE)
