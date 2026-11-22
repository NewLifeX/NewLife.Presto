<!-- generated from README.md by tools/sync-readme-package.ps1; do not edit -->

# NewLife.Presto — Presto/Trino ADO.NET Driver for .NET

[![AOT](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/aot.yml/badge.svg?label=AOT)](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/aot.yml)
![GitHub License](https://img.shields.io/github/license/NewLifeX/NewLife.Presto?logo=github)
![NuGet Downloads](https://img.shields.io/nuget/dt/NewLife.Presto?logo=nuget)
![NuGet](https://img.shields.io/nuget/v/NewLife.Presto?logo=nuget)
![NuGet prerelease](https://img.shields.io/nuget/vpre/NewLife.Presto?label=dev%20nuget&logo=nuget)
[![test](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/test.yml/badge.svg?branch=master)](https://github.com/NewLifeX/NewLife.Presto/actions/workflows/test.yml)
[![locales](https://img.shields.io/badge/locales-33-2ea44f?logo=dotnet)](https://github.com/NewLifeX/NewLife.Presto/blob/master/Doc/多语言本地化.md)
![platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blue)
![arch](https://img.shields.io/badge/arch-x64%20%7C%20arm64%20%7C%20loongarch64%20%7C%20riscv64-orange)
[![contributors](https://img.shields.io/github/contributors/NewLifeX/NewLife.Presto?logo=github)](https://github.com/NewLifeX/NewLife.Presto/graphs/contributors)

<!-- lang:start -->
🌐 [简体中文](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.zh-CN.md) | [繁體中文](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.zh-TW.md) | [Español](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.es.md) | [Deutsch](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.de.md) | [日本語](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.ja.md) | [Français](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.fr.md) | [Português](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.pt.md) | [Русский](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.ru.md) | [한국어](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.ko.md) | [العربية](https://github.com/NewLifeX/NewLife.Presto/blob/master/README.ar.md)
<!-- lang:end -->

**NewLife.Presto** is a pure C# ADO.NET driver for PrestoDB and Trino from the NewLife team. It speaks the official HTTP REST v1 client protocol, depends only on `NewLife.Core` with **zero third-party dependencies**, ships **33 runtime languages**, targets `net45` through `net11`, and is **NativeAOT ready** on `net8.0+`. PrestoDB and Trino share one package; `Protocol=Trino` (or `TrinoHeaders=true`) switches the request headers. Trino 466+ spooling is supported with uncompressed JSON segments.

> **Project status**: implemented and tested — core ADO.NET driver plus read-only EF Core and EF6 providers, built as three NuGet packages shipped by one release pipeline. 561 core unit tests, EF provider tests and real Trino/PrestoDB integration pass; spooling covers uncompressed JSON segments and falls back to the direct protocol otherwise. See [Doc/功能清单.md](https://github.com/NewLifeX/NewLife.Presto/blob/master/Doc/功能清单.md).

**Contents**: [Quick Start](#quick-start) · [Why NewLife.Presto](#why-newlifepresto) · [Main Features](#main-features) · [Platform Support](#platform-support) · [Localization](#localization) · [Documentation](#documentation) · [Contributing](#contributing) · [Ecosystem Map](#ecosystem-map) · [Team](#team) · [License](#license)

---

## Quick Start

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
using Microsoft.EntityFrameworkCore;
using NewLife.Presto.EntityFrameworkCore;

var options = new DbContextOptionsBuilder<MyDbContext>()
    .UsePresto("Host=localhost;Port=8080;Catalog=hive;Schema=default;User=test")
    .Options;
```
```csharp
// Entity Framework 6 (read-only)
using System.Data.Entity;
using NewLife.Presto.EntityFramework;

DbConfiguration.SetConfiguration(new PrestoEFConfiguration());
```

---

## Why NewLife.Presto

- **One package for PrestoDB and Trino**: the same protocol implementation, with `X-Presto-*` / `X-Trino-*` headers selected by configuration.
- **XCode-first**: factory, connection string aliases, `@` parameters, metadata and capability boundaries are designed around XCode's Presto model.
- **Zero third-party dependencies**: only `NewLife.Core`; JSON, HTTP and logging come from the BCL or the NewLife stack.
- **Full framework and platform coverage**: `net45` ~ `net11`, Windows / Linux / macOS, x64 / arm64, plus observational LoongArch64 and musl builds.
- **33 runtime languages**: exceptions, logs and driver metadata are localized, with English as the source and fallback.
- **NativeAOT ready**: no runtime reflection in protocol or type codecs; NativeAOT smoke runs on every supported platform.

## Main Features

<details>
<summary><b>Connection & Protocol</b> (6 items)</summary>

| Feature | What it gives you |
|---------|-------------------|
| Connection string aliases | `Host`/`Server`/`Data Source`, `Catalog`/`Database`, `Schema`/`Owner`, and XCode-style keys |
| PrestoDB / Trino headers | `Protocol=Presto` by default; `Protocol=Trino` or `TrinoHeaders=true` for Trino 351+ |
| Session context | Catalog, schema, user, source, time zone, session properties and custom headers |
| Authentication | Basic, JWT/Bearer, OAuth2 client credentials and custom gateway headers, with secrets kept out of logs |
| TLS and proxies | Strict certificate validation by default; explicit trust switch for private CAs |
| Shared HTTP transport | Reuses `HttpClient` per endpoint, with proxy, compression and timeouts |

</details>

<details>
<summary><b>Query & Types</b> (8 items)</summary>

| Feature | What it gives you |
|---------|-------------------|
| Statement lifecycle | `POST /v1/statement`, `nextUri` polling and completion detection |
| Forward-compatible states | Query states are strings, so new server states do not break parsing |
| Transient retry | 429/502/503/504 retry with `Retry-After` or exponential backoff |
| `@name` parameters | Client-side substitution that skips strings, identifiers and comments |
| Type fidelity | Boolean, integer, decimal, date/time, timestamp with time zone, UUID, IP and binary |
| Complex types | `array`/`map`/`row` default to raw strings; `ComplexTypes=Structured` returns `object[]` and dictionaries |
| `DbBatch` (.NET 6+) | Sequential batch commands with summed affected rows and cancellation propagated to the whole batch |
| Spooling (Trino 466+) | `Spooling=true` requests uncompressed JSON segments, downloads them from object storage and acknowledges them |

</details>

<details>
<summary><b>Integration & Diagnostics</b> (6 items)</summary>

| Feature | What it gives you |
|---------|-------------------|
| `GetSchema` | `MetaDataCollections`, `Tables`, `Columns`, `DataTypes` |
| XCode integration | Default driver switch with legacy fallback; no transaction, DDL or auto-increment claims |
| Localization | 33 runtime languages, generated at compile time and AOT-safe |
| Diagnostics | Query id, error code, SQLState and timing preserved in exceptions and logs; read-only query/node diagnostics API |
| OpenTelemetry | `ActivitySource("NewLife.Presto")` on modern frameworks |
| Packaging | Strong naming, SourceLink, symbol package and README.nuget.md |

</details>

## Platform Support

| Area | Supported |
|------|-----------|
| Operating systems | Windows, Linux (glibc and musl), macOS |
| Architectures | x64, arm64; LoongArch64 and riscv64 covered by observational builds |
| Target frameworks | net45, net461, netstandard2.0, netstandard2.1, net6.0, net8.0, net10.0, net11.0 |
| NativeAOT | net8.0+ with zero trim/AOT analyzer warnings; blocking smoke on win-x64, linux-x64, linux-arm64 |
| CI matrix | Windows, Linux x64, Linux arm64, musl containers, macOS and Windows arm64 (observational) |

## Localization

| Scope | Coverage |
|-------|----------|
| Runtime messages | 33 languages including English; missing translations fall back to English |
| README | 11 languages generated from `readme-languages.json` |
| Metadata | Assembly title and description are English source strings and localized at display time |

> Translation keys are the English source text. Add a new message in code, then add the same key to every locale file; `tools/check-locales.ps1 -RequireTranslated` enforces key, placeholder and translation completeness in CI.

## Documentation

- [需求文档](https://github.com/NewLifeX/NewLife.Presto/blob/master/Doc/需求文档.md) — requirements and scope
- [功能清单](https://github.com/NewLifeX/NewLife.Presto/blob/master/Doc/功能清单.md) — implementation/test/documentation status
- [架构设计](https://github.com/NewLifeX/NewLife.Presto/blob/master/Doc/架构设计.md) — components, protocol flow, type mapping, decisions
- [竞品分析](https://github.com/NewLifeX/NewLife.Presto/blob/master/Doc/竞品分析.md) — reference projects, feature matrix, gaps
- [性能测试](https://github.com/NewLifeX/NewLife.Presto/blob/master/Doc/性能测试.md) — same-round benchmark against ADO competitors and a raw HTTP baseline

## Contributing

- Build: `dotnet build NewLife.Presto.slnx -c Release`
- Test: `dotnet test UnitTest/UnitTest.csproj -c Release`
- Localization: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- Follow NewLife C# conventions and keep every runtime message in the locale tables.
- Do not update `ChangeLog.md` or package release notes outside the release process.

## Ecosystem Map

NewLife is a layered ecosystem of independently versioned open-source projects. The map below is shared verbatim by every NewLife repository; each project links to its repository and its primary NuGet package.

| Project | What it does | Downloads |
| --- | --- | --- |
| **Foundation** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Logging, configuration, caching, networking, serialization, scheduling and APM tracing. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Data middleware: single tables at the tens-of-billions scale, automatic sharding, read-write splitting, 10+ databases. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | High-performance network library: 1.77M pps / 14.5 Gbps on a single machine (TCP 1KB, 2026-10), 4M long-lived TCP connections. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Protocol communication and RPC framework for client/server applications and IoT devices. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Installs .NET applications as OS daemons: Windows services and Linux systemd. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | `dotnet new` project templates for console, web, API, service, data and IoT projects. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Data & Messaging** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Redis client with microsecond latency, millions of ops/s and built-in message queues. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Pure managed RocketMQ client for Apache RocketMQ and Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT client and server, including Alibaba Cloud IoT connectivity. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Hybrid database in C#: embedded or networked, relational, time-series, message queue and KV in one. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | MySql driver and helper layer built on the NewLife data stack. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, AI & Distributed** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Rapid admin-platform framework: permissions, SSO, OAuth server and CRUD scaffolding. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Distributed service platform: node management, APM, configuration center, registry and release center. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Distributed task and real-time data scheduling platform with sharding, retries and backfill. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Unified client for 46 LLM providers plus ChatAI, a ready-to-run web chat application. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Industrial & IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | IoT standard library defining protocol contracts and device abstractions. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Modbus TCP/RTU/ASCII implementation on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Siemens PLC protocol on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Omron PLC protocol on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Mitsubishi Melsec PLC protocol on top of NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | OPC client for industrial data access. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet building-automation protocol stack. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Lightweight open-source IoT platform with gateway and device management. | — |
| **Vehicle Telematics** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 vehicle positioning protocol: 70+ message bodies, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | JT/T 809 platform-to-platform protocol for transport authorities. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 in-vehicle video: RTP parsing, FLV/fMP4 remuxing and live streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Domain Components** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | One package for Excel, Word, PPT, PDF and 15+ document formats. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Cross-platform audio: codecs, device I/O, DSP, containers and streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Video codecs, container formats, frame processing and streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Unified interface over Baidu, AMap, Tencent and Tianditu with geocoding and coordinate conversion. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | IPv4 geolocation with an offline memory-mapped database and an online API fallback. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Tools & Products** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Developer toolbox: network, serial port, crypto, regex, Modbus and MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Self-hosted Git source code management system. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Installs the matching .NET runtime automatically when launched from an older host. | — |

> Badges show the primary package of each project; theme, extension and satellite packages are listed on their NuGet pages. The 30+ packages not listed here (FTP, LoRa, Office extensions, protocol drivers and more) are available under the [NewLifeX organization](https://github.com/NewLifeX) and on [newlifex.com](https://newlifex.com).

## Team

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

Founded in 2002, the NewLife team has spent more than two decades on the .NET and IoT ecosystem, maintains nearly 100 open-source projects (34 of them core to this map), and delivers integrated software and hardware solutions for power, logistics, healthcare, industrial control and internet industries. We live by the motto "learning has no seniority — mastery earns respect".

Open-source components have been downloaded <!--nuget-total:start-->6.48M<!--nuget-total:end--> times from NuGet (ecosystem-wide, as of 2026-10).

Website: https://newlifex.com · Open source organization: https://github.com/NewLifeX

<details>
<summary>WeChat Official Account: 智能大石头 (scan to follow)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## License

[MIT](https://github.com/NewLifeX/NewLife.Presto/blob/master/LICENSE)
