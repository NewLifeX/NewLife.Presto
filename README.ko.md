# NewLife.Presto — Presto/Trino ADO.NET 드라이버

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto**는 NewLife 팀이 만든 PrestoDB / Trino용 순수 C# ADO.NET 드라이버입니다. 공식 HTTP REST v1 클라이언트 프로토콜을 사용하며 `NewLife.Core`에만 의존하는 **타사 종속성 제로** 드라이버입니다. **33개 런타임 언어**를 내장하고 `net45`부터 `net11`까지 지원하며 `net8.0+`에서 **NativeAOT를 지원**합니다. PrestoDB와 Trino는 하나의 패키지를 공유하며 `Protocol=Trino`(또는 `TrinoHeaders=true`)로 요청 헤더를 전환합니다.

> **프로젝트 상태**: 설계 및 스캐폴딩 단계입니다. 오프라인 구성 요소(연결 문자열, 프로토콜 헤더, 형식 매핑, 다국어, AOT 스모크)는 구현 및 테스트되었습니다. 네트워크와 쿼리 실행 진행 상황은 [기능 목록](Doc/功能清单.md)을 참조하세요.

**목차**: [빠른 시작](#빠른-시작) · [NewLife.Presto를 선택하는 이유](#newlifepresto를-선택하는-이유) · [주요 기능](#주요-기능) · [플랫폼 지원](#플랫폼-지원) · [다국어](#다국어) · [문서](#문서) · [기여](#기여) · [생태계 지도](#생태계-지도) · [팀](#팀) · [라이선스](#라이선스)

---

## 빠른 시작

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

---

## NewLife.Presto를 선택하는 이유

- **PrestoDB와 Trino를 하나의 패키지로**: 동일한 프로토콜 구현을 공유하고 설정으로 `X-Presto-*` / `X-Trino-*` 헤더를 선택합니다.
- **XCode 우선**: 팩터리, 연결 문자열 별칭, `@` 매개 변수, 메타데이터, 기능 경계를 XCode의 Presto 모델에 맞춰 설계했습니다.
- **타사 종속성 제로**: `NewLife.Core`만 참조하며 JSON, HTTP, 로깅은 BCL 또는 NewLife 스택을 사용합니다.
- **모든 프레임워크와 플랫폼**: `net45` ~ `net11`, Windows / Linux / macOS, x64 / arm64, LoongArch64와 musl은 관찰용 빌드로 지원합니다.
- **33개 런타임 언어**: 예외, 로그, 드라이버 메타데이터를 지역화하며 영어가 원문과 대체 언어입니다.
- **NativeAOT 지원**: 프로토콜과 형식 코덱에서 런타임 리플렉션을 사용하지 않으며 지원 플랫폼에서 AOT 스모크를 실행합니다.

## 주요 기능

<details>
<summary><b>연결과 프로토콜</b> (6개)</summary>

| 기능 | 제공 가치 |
|------|----------|
| 연결 문자열 별칭 | `Host`/`Server`/`Data Source`, `Catalog`/`Database`, `Schema`/`Owner`, XCode 형식 지원 |
| PrestoDB / Trino 헤더 | 기본 `Protocol=Presto`; Trino 351+는 `Protocol=Trino` 또는 `TrinoHeaders=true` |
| 세션 컨텍스트 | Catalog, Schema, 사용자, 소스, 표준 시간대, 세션 속성, 사용자 지정 헤더 |
| 인증 | Basic, JWT/Bearer, 게이트웨이 사용자 지정 헤더. 비밀 정보는 로그에 남기지 않음 |
| TLS와 프록시 | 기본적으로 인증서를 엄격히 검증하며 사설 CA는 명시적으로 완화 |
| 공유 HTTP 전송 | 엔드포인트별 `HttpClient` 재사용, 프록시, 압축, 시간 제한 지원 |

</details>

<details>
<summary><b>쿼리와 형식</b> (6개)</summary>

| 기능 | 제공 가치 |
|------|----------|
| 문 수명 주기 | `POST /v1/statement`, `nextUri` 폴링, 완료 판정 |
| 전방 호환 상태 | 상태를 문자열로 보관하여 서버의 새 상태도 구문 분석이 깨지지 않음 |
| 일시 오류 재시도 | 429/502/503/504를 `Retry-After` 또는 지수 백오프로 재시도 |
| `@name` 매개 변수 | 문자열, 식별자, 주석을 건너뛰는 클라이언트 측 대체 |
| 형식 정확성 | 부울, 정수, decimal, 날짜/시간, timestamp with time zone, UUID, IP, 이진 |
| 복합 형식 | v1에서는 `array`/`map`/`row`/`json`을 원시 문자열로 반환 |

</details>

<details>
<summary><b>통합과 진단</b> (6개)</summary>

| 기능 | 제공 가치 |
|------|----------|
| `GetSchema` | `MetaDataCollections`, `Tables`, `Columns`, `DataTypes` |
| XCode 통합 | 기본 드라이버 전환과 이전 드라이버 대체. 트랜잭션, DDL, 자동 증가는 지원하지 않음 |
| 다국어 | 33개 런타임 언어. 컴파일 시 생성되며 AOT에서도 사용 가능 |
| 진단 | 예외와 로그에 쿼리 ID, 오류 코드, SQLState, 소요 시간 유지 |
| OpenTelemetry | 최신 프레임워크에서 `ActivitySource("NewLife.Presto")` 제공 |
| 패키징 | 강력한 이름, SourceLink, 기호 패키지, README.nuget.md |

</details>

## 플랫폼 지원

| 영역 | 지원 범위 |
|------|----------|
| 운영 체제 | Windows, Linux(glibc와 musl), macOS |
| 아키텍처 | x64, arm64. LoongArch64와 riscv64는 관찰용 빌드로 지원 |
| 대상 프레임워크 | net45, net461, netstandard2.0, netstandard2.1, net6.0, net8.0, net10.0, net11.0 |
| NativeAOT | net8.0+에서 트리밍/AOT 분석기 경고 0. win-x64, linux-x64, linux-arm64는 필수 스모크 |
| CI 매트릭스 | Windows, Linux x64, Linux arm64, musl 컨테이너, macOS, Windows arm64(관찰용) |

## 다국어

| 범위 | 지원 |
|------|------|
| 런타임 메시지 | 영어를 포함한 33개 언어. 번역이 없으면 영어로 대체 |
| README | `readme-languages.json`에서 11개 언어 생성 |
| 메타데이터 | 어셈블리 제목과 설명은 영어 원문이며 표시 시 번역 |

> 번역 키는 영어 원문입니다. 코드에 메시지를 추가한 뒤 모든 언어 파일에 같은 키를 추가하세요. `tools/check-locales.ps1 -RequireTranslated`가 키, 자리 표시자, 번역 완전성을 CI에서 검사합니다.

## 문서

- [需求文档](Doc/需求文档.md) — 요구 사항과 범위
- [功能清单](Doc/功能清单.md) — 구현/테스트/주석 상태
- [架构设计](Doc/架构设计.md) — 구성 요소, 프로토콜 흐름, 형식 매핑, 설계 결정
- [竞品分析](Doc/竞品分析.md) — 참조 구현, 기능 매트릭스, 격차

## 기여

- 빌드: `dotnet build NewLife.Presto.slnx -c Release`
- 테스트: `dotnet test UnitTest/UnitTest.csproj -c Release`
- 다국어: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- NewLife C# 규칙을 따르고 모든 런타임 메시지를 로캘 테이블에 추가하세요.
- 릴리스 절차 외에는 `ChangeLog.md`와 패키지 릴리스 정보를 수정하지 마세요.

## 생태계 지도

NewLife는 각자 독립적으로 버전이 매겨지는 오픈 소스 프로젝트들의 계층적 생태계입니다. 아래 지도는 모든 NewLife 저장소에서 동일하게 공유되며, 각 프로젝트는 해당 저장소와 대표 NuGet 패키지로 연결됩니다.

| 프로젝트 | 역할 | 다운로드 |
| --- | --- | --- |
| **기반** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | 로깅, 구성, 캐싱, 네트워킹, 직렬화, 스케줄링, APM 추적. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | 데이터 미들웨어: 수백억 규모 단일 테이블, 자동 샤딩, 읽기/쓰기 분리, 10여 종 데이터베이스. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | 고성능 네트워크 라이브러리: 단일 머신 1.77M pps / 14.5 Gbps(TCP 1KB, 2026-10), 4M 장수명 TCP 연결. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | 클라이언트/서버 애플리케이션과 IoT 기기를 위한 프로토콜 통신 및 RPC 프레임워크. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | .NET 애플리케이션을 운영체제 데몬으로 설치: Windows 서비스와 Linux systemd. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | 콘솔, 웹, API, 서비스, 데이터, IoT 프로젝트용 `dotnet new` 프로젝트 템플릿. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **데이터 및 메시징** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | 마이크로초 지연, 초당 수백만 건 처리, 메시지 큐 내장 Redis 클라이언트. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Apache RocketMQ와 알리바바 클라우드 메시지 큐를 위한 순수 관리형 RocketMQ 클라이언트. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT 클라이언트와 서버, 알리바바 클라우드 IoT 연결 포함. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | C#로 만든 하이브리드 데이터베이스: 임베디드 또는 네트워크, 관계형, 시계열, 메시지 큐, KV를 하나로. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | NewLife 데이터 스택 기반의 MySql 드라이버와 헬퍼 계층. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **웹, AI 및 분산** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | 빠른 관리 플랫폼 프레임워크: 권한, SSO, OAuth 서버, CRUD 스캐폴딩. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | 분산 서비스 플랫폼: 노드 관리, APM, 구성 센터, 레지스트리, 릴리스 센터. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | 샤딩, 재시도, 백필을 갖춘 분산 작업 및 실시간 데이터 스케줄링 플랫폼. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | 46개 LLM 공급자를 위한 통합 클라이언트와 즉시 실행 가능한 웹 채팅 애플리케이션 ChatAI. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **산업 및 IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | 프로토콜 계약과 기기 추상화를 정의하는 IoT 표준 라이브러리. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | NewLife.IoT 위에 구현한 Modbus TCP/RTU/ASCII. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | NewLife.IoT 위에 구현한 지멘스 PLC 프로토콜. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | NewLife.IoT 위에 구현한 오므론 PLC 프로토콜. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | NewLife.IoT 위에 구현한 미쓰비시 Melsec PLC 프로토콜. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | 산업 데이터 액세스를 위한 OPC 클라이언트. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet 빌딩 자동화 프로토콜 스택. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | 게이트웨이와 기기 관리를 갖춘 경량 오픈 소스 IoT 플랫폼. | — |
| **차량 텔레매틱스** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 차량 위치 프로토콜: 70여 종 메시지 본문, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | 운수 관리 부문을 위한 JT/T 809 플랫폼 간 프로토콜. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 차량 내 영상: RTP 파싱, FLV/fMP4 리먹싱, 라이브 스트리밍. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **도메인 컴포넌트** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Excel, Word, PPT, PDF 및 15가지 이상 문서 형식을 하나의 패키지로. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | 크로스 플랫폼 오디오: 코덱, 기기 I/O, DSP, 컨테이너, 스트리밍. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | 비디오 코덱, 컨테이너 형식, 프레임 처리, 스트리밍. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | 바이두, AMap, 텐센트, 티안디투를 지오코딩과 좌표 변환으로 통합한 인터페이스. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | 오프라인 메모리 매핑 데이터베이스와 온라인 API 폴백을 갖춘 IPv4 지역 정보. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **도구 및 제품** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | 개발자 도구 상자: 네트워크, 시리얼 포트, 암호화, 정규식, Modbus, MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | 셀프 호스팅 Git 소스 코드 관리 시스템. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | 구버전 호스트에서 실행될 때 일치하는 .NET 런타임을 자동으로 설치. | — |

> 배지는 각 프로젝트의 대표 패키지를 보여 줍니다. 테마, 확장, 부속 패키지는 각 NuGet 페이지에 나열되어 있습니다.여기에 나열되지 않은 30여 개 패키지(FTP, LoRa, Office 확장, 프로토콜 드라이버 등)는 [NewLifeX 조직](https://github.com/NewLifeX)과 [newlifex.com](https://newlifex.com)에서 확인할 수 있습니다.

## 팀

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

NewLife 팀은 2002년에 창립되어 20년 넘게 .NET과 IoT 생태계에 매진해 왔으며, 약 100개의 오픈 소스 프로젝트(그중 34개가 이 지도의 핵심)를 유지하고, 전력, 물류, 의료, 산업 제어, 인터넷 산업에 소프트웨어와 하드웨어 통합 솔루션을 제공합니다. 우리는 "배움에 선후는 없고, 통달함이 존경을 얻는다"는 신조를 따릅니다.

오픈 소스 컴포넌트는 NuGet에서 <!--nuget-total:start-->6.48M<!--nuget-total:end-->회 다운로드되었습니다(생태계 전체 기준, 2026-10).

웹사이트: https://newlifex.com · 오픈 소스 조직: https://github.com/NewLifeX

<details>
<summary>위챗 공식 계정: 智能大石头 (스캔하여 팔로우)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## 라이선스

[MIT](LICENSE)
