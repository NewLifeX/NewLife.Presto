# NewLife.Presto — Presto/Trino ADO.NET ドライバー

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto** は、NewLife チームによる PrestoDB / Trino 用の純 C# ADO.NET ドライバーです。公式の HTTP REST v1 クライアント プロトコルを使用し、`NewLife.Core` のみに依存する **サードパーティ依存ゼロ** のドライバーです。**33 の実行時言語** を内蔵し、`net45` から `net11` までを対象とし、`net8.0+` で **NativeAOT に対応** します。PrestoDB と Trino は 1 つのパッケージを共用し、`Protocol=Trino`（または `TrinoHeaders=true`）で要求ヘッダーを切り替えます。

> **プロジェクトの状態**：設計とスキャフォールディングの段階です。オフラインの基盤（接続文字列、プロトコル ヘッダー、型マッピング、多言語、AOT スモーク）は実装・テスト済みです。ネットワークとクエリ実行の進捗は[機能リスト](Doc/功能清单.md)を参照してください。

**目次**: [クイック スタート](#クイック-スタート) · [NewLife.Presto を選ぶ理由](#newlifepresto-を選ぶ理由) · [主な機能](#主な機能) · [プラットフォーム対応](#プラットフォーム対応) · [多言語](#多言語) · [ドキュメント](#ドキュメント) · [コントリビューション](#コントリビューション) · [エコシステムマップ](#エコシステムマップ) · [チーム](#チーム) · [ライセンス](#ライセンス)

---

## クイック スタート

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

## NewLife.Presto を選ぶ理由

- **PrestoDB と Trino を 1 つのパッケージで**: 同じプロトコル実装を共用し、設定で `X-Presto-*` / `X-Trino-*` ヘッダーを選択します。
- **XCode ファースト**: ファクトリ、接続文字列の別名、`@` パラメーター、メタデータ、機能境界を XCode の Presto モデルに合わせて設計しています。
- **サードパーティ依存ゼロ**: 依存は `NewLife.Core` のみ。JSON、HTTP、ログは BCL または NewLife スタックを使用します。
- **全フレームワーク・全プラットフォーム**: `net45` ~ `net11`、Windows / Linux / macOS、x64 / arm64。LoongArch64 と musl は観測用ビルドで対応します。
- **33 の実行時言語**: 例外、ログ、ドライバー メタデータをローカライズし、英語を原文とフォールバックにします。
- **NativeAOT 対応**: プロトコルと型コーデックで実行時リフレクションを使用せず、対応プラットフォームで AOT スモークを実行します。

## 主な機能

<details>
<summary><b>接続とプロトコル</b>（6 項目）</summary>

| 機能 | 特長 |
|------|------|
| 接続文字列の別名 | `Host`/`Server`/`Data Source`、`Catalog`/`Database`、`Schema`/`Owner`、XCode 形式に対応 |
| PrestoDB / Trino ヘッダー | 既定は `Protocol=Presto`。Trino 351+ は `Protocol=Trino` または `TrinoHeaders=true` |
| セッション コンテキスト | Catalog、Schema、ユーザー、ソース、タイム ゾーン、セッション プロパティ、カスタム ヘッダー |
| 認証 | Basic、JWT/Bearer、ゲートウェイ用カスタム ヘッダー。秘密情報はログに出しません |
| TLS とプロキシ | 既定で証明書を厳格に検証。プライベート CA は明示的に緩和 |
| 共有 HTTP トランスポート | エンドポイントごとに `HttpClient` を再利用し、プロキシ、圧縮、タイムアウトに対応 |

</details>

<details>
<summary><b>クエリと型</b>（6 項目）</summary>

| 機能 | 特長 |
|------|------|
| ステートメント ライフサイクル | `POST /v1/statement`、`nextUri` ポーリング、完了判定 |
| 前方互換の状態 | 状態は文字列で保持し、サーバーの新状態でも解析が壊れません |
| 一時エラーの再試行 | 429/502/503/504 を `Retry-After` または指数バックオフで再試行 |
| `@name` パラメーター | 文字列、識別子、コメントをスキップするクライアント側置換 |
| 型の忠実性 | ブール、整数、decimal、日付時刻、timestamp with time zone、UUID、IP、バイナリ |
| 複合型 | v1 では `array`/`map`/`row`/`json` を生の文字列で返します |

</details>

<details>
<summary><b>統合と診断</b>（6 項目）</summary>

| 機能 | 特長 |
|------|------|
| `GetSchema` | `MetaDataCollections`、`Tables`、`Columns`、`DataTypes` |
| XCode 統合 | 既定ドライバーの切り替えと旧ドライバーへのフォールバック。トランザクション、DDL、自動増分は非対応と明示 |
| 多言語 | 33 の実行時言語。コンパイル時に生成され AOT でも利用可能 |
| 診断 | 例外とログにクエリ ID、エラー コード、SQLState、所要時間を保持 |
| OpenTelemetry | モダン フレームワークで `ActivitySource("NewLife.Presto")` を提供 |
| パッケージ | 厳密名、SourceLink、シンボル パッケージ、README.nuget.md |

</details>

## プラットフォーム対応

| 項目 | 対応範囲 |
|------|----------|
| OS | Windows、Linux（glibc と musl）、macOS |
| アーキテクチャ | x64、arm64。LoongArch64 と riscv64 は観測用ビルドで対応 |
| ターゲット フレームワーク | net45、net461、netstandard2.0、netstandard2.1、net6.0、net8.0、net10.0、net11.0 |
| NativeAOT | net8.0+ でトリム/AOT アナライザー警告ゼロ。win-x64、linux-x64、linux-arm64 は必須スモーク |
| CI マトリックス | Windows、Linux x64、Linux arm64、musl コンテナー、macOS、Windows arm64（観測用） |

## 多言語

| 範囲 | 対応 |
|------|------|
| 実行時メッセージ | 英語を含む 33 言語。未翻訳は英語にフォールバック |
| README | `readme-languages.json` から 11 言語を生成 |
| メタデータ | アセンブリのタイトルと説明は英語原文で、表示時に翻訳 |

> 翻訳キーは英語の原文です。コードにメッセージを追加したら、すべての言語ファイルに同じキーを追加してください。`tools/check-locales.ps1 -RequireTranslated` がキー、プレースホルダー、翻訳の完全性を CI で検証します。

## ドキュメント

- [需求文档](Doc/需求文档.md) — 要件と範囲
- [功能清单](Doc/功能清单.md) — 実装/テスト/コメントの状態
- [架构设计](Doc/架构设计.md) — コンポーネント、プロトコル フロー、型マッピング、設計判断
- [竞品分析](Doc/竞品分析.md) — 参考実装、機能マトリックス、差分

## コントリビューション

- ビルド: `dotnet build NewLife.Presto.slnx -c Release`
- テスト: `dotnet test UnitTest/UnitTest.csproj -c Release`
- 多言語: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- NewLife の C# 規約に従い、実行時メッセージは必ずロケール テーブルに追加してください。
- リリース工程以外では `ChangeLog.md` とパッケージ リリース ノートを変更しないでください。

## エコシステムマップ

NewLife は、それぞれ独立してバージョン管理されるオープンソースプロジェクトによる階層型エコシステムです。以下のマップはすべての NewLife リポジトリで同一の内容を掲載しています。各プロジェクトはそのリポジトリと主要な NuGet パッケージにリンクしています。

| プロジェクト | 役割 | ダウンロード数 |
| --- | --- | --- |
| **基盤** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | ログ、設定、キャッシュ、ネットワーク、シリアル化、スケジューリング、APM トレーシング。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | データミドルウェア：数百億行規模の単一テーブル、自動シャーディング、読み書き分離、10 以上のデータベース。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | ハイパフォーマンスネットワークライブラリ：単一マシンで 1.77M pps / 14.5 Gbps（TCP 1KB、2026-10）、400 万の長時間 TCP 接続。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | クライアント/サーバーアプリケーションと IoT デバイス向けのプロトコル通信および RPC フレームワーク。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | .NET アプリケーションを OS のデーモン（Windows サービス、Linux systemd）としてインストール。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | コンソール、Web、API、サービス、データ、IoT プロジェクト向けの `dotnet new` プロジェクトテンプレート。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **データとメッセージング** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | マイクロ秒のレイテンシ、毎秒数百万オペレーション、組み込みメッセージキューを備えた Redis クライアント。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Apache RocketMQ と Alibaba Cloud Message Queue 向けの純マネージド RocketMQ クライアント。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT クライアントとサーバー。Alibaba Cloud IoT への接続を含む。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | C# 製のハイブリッドデータベース：組み込みまたはネットワーク、リレーショナル、時系列、メッセージキュー、KV を 1 つに。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | NewLife データスタック上に構築された MySql ドライバーとヘルパー層。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web、AI、分散システム** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | 迅速な管理プラットフォームフレームワーク：権限、SSO、OAuth サーバー、CRUD スキャフォールディング。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | 分散サービスプラットフォーム：ノード管理、APM、設定センター、レジストリ、リリースセンター。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | シャーディング、リトライ、バックフィルを備えた分散タスクおよびリアルタイムデータスケジューリングプラットフォーム。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | 46 の LLM プロバイダー向け統合クライアントと、すぐに使える Web チャットアプリケーション ChatAI。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **産業と IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | プロトコル契約とデバイス抽象化を定義する IoT 標準ライブラリ。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | NewLife.IoT 上の Modbus TCP/RTU/ASCII 実装。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | NewLife.IoT 上の Siemens PLC プロトコル。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | NewLife.IoT 上の Omron PLC プロトコル。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | NewLife.IoT 上の三菱 Melsec PLC プロトコル。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | 産業データアクセス向けの OPC クライアント。 | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet ビルオートメーションプロトコルスタック。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | ゲートウェイとデバイス管理を備えた軽量オープンソース IoT プラットフォーム。 | — |
| **車載テレマティクス** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T 808 車両位置プロトコル：70 以上のメッセージボディ、2011/2013/2019。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | 交通管理部門向けの JT/T 809 プラットフォーム間プロトコル。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T 1078 車載ビデオ：RTP 解析、FLV/fMP4 リマックス、ライブストリーミング。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **ドメインコンポーネント** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Excel、Word、PPT、PDF など 15 以上のドキュメント形式を 1 つのパッケージで。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | クロスプラットフォーム音声：コーデック、デバイス I/O、DSP、コンテナ、ストリーミング。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | ビデオコーデック、コンテナ形式、フレーム処理、ストリーミング。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | 百度地図、高徳地図、騰訊地図、天地図を統合したインターフェース。ジオコーディングと座標変換を提供。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | オフラインのメモリマップドデータベースとオンライン API フォールバックを備えた IPv4 位置情報。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **ツールと製品** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | 開発者向けツールボックス：ネットワーク、シリアルポート、暗号、正規表現、Modbus、MQTT。 | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | セルフホスト型の Git ソースコード管理システム。 | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | 古いホストから起動された際に、対応する .NET ランタイムを自動的にインストール。 | — |

> バッジは各プロジェクトの主要パッケージを示しています。テーマ、拡張、サテライトパッケージは各 NuGet ページに掲載されています。ここに記載のない 30 以上のパッケージ（FTP、LoRa、Office 拡張、プロトコルドライバーなど）は [NewLifeX オーガナイゼーション](https://github.com/NewLifeX) および [newlifex.com](https://newlifex.com) で入手できます。

## チーム

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

NewLife チームは 2002 年に設立され、20 年以上にわたり .NET と IoT のエコシステムに取り組み、約 100 のオープンソースプロジェクト（うち 34 がこのマップの中核）を維持し、電力、物流、医療、産業制御、インターネットの各業界向けにソフトウェアとハードウェアを統合したソリューションを提供しています。私たちは「学問に先輩後輩はなく、達した者が尊ばれる」という言葉を信条としています。

オープンソースコンポーネントは NuGet から <!--nuget-total:start-->6.48M<!--nuget-total:end--> 回ダウンロードされています（エコシステム全体、2026-10 時点）。

ウェブサイト：https://newlifex.com · オープンソース組織：https://github.com/NewLifeX

<details>
<summary>WeChat 公式アカウント：智能大石头（スキャンしてフォロー）</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## ライセンス

[MIT](LICENSE)
