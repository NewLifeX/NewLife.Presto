# NewLife.Presto — Presto/Trino ADO.NET-Treiber

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto** ist ein reiner C#-ADO.NET-Treiber für PrestoDB und Trino vom NewLife-Team. Er nutzt das offizielle HTTP-REST-v1-Clientprotokoll, hängt nur von `NewLife.Core` ab und hat **keine Abhängigkeiten von Drittanbietern**. Er enthält **33 Laufzeitsprachen**, unterstützt `net45` bis `net11` und ist ab `net8.0` **NativeAOT-fähig**. PrestoDB und Trino teilen sich ein Paket; `Protocol=Trino` (oder `TrinoHeaders=true`) schaltet die Anforderungsheader um.

> **Projektstatus**: Entwurfs- und Gerüstphase. Die Offline-Bausteine (Verbindungszeichenfolge, Protokollheader, Typzuordnung, Lokalisierung, AOT-Smoke) sind implementiert und getestet. Netzwerk und Abfrageausführung werden in der [Funktionsliste](Doc/功能清单.md) verfolgt.

**Inhalt**: [Schnellstart](#schnellstart) · [Warum NewLife.Presto](#warum-newlifepresto) · [Hauptfunktionen](#hauptfunktionen) · [Plattformunterstützung](#plattformunterstützung) · [Lokalisierung](#lokalisierung) · [Dokumentation](#dokumentation) · [Mitwirken](#mitwirken) · [Ökosystem-Karte](#ökosystem-karte) · [Team](#team) · [Lizenz](#lizenz)

---

## Schnellstart

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

## Warum NewLife.Presto

- **Ein Paket für PrestoDB und Trino**: dieselbe Protokollimplementierung, `X-Presto-*` / `X-Trino-*`-Header per Konfiguration.
- **XCode zuerst**: Factory, Verbindungszeichenfolgen-Aliase, `@`-Parameter, Metadaten und Fähigkeitsgrenzen sind auf XCodes Presto-Modell zugeschnitten.
- **Keine Drittanbieter-Abhängigkeiten**: nur `NewLife.Core`; JSON, HTTP und Protokollierung stammen aus der BCL oder dem NewLife-Stack.
- **Alle Frameworks und Plattformen**: `net45` ~ `net11`, Windows / Linux / macOS, x64 / arm64, LoongArch64 und musl als beobachtete Builds.
- **33 Laufzeitsprachen**: Ausnahmen, Protokolle und Treibermetadaten sind lokalisiert; Englisch ist Quelle und Fallback.
- **NativeAOT-fähig**: keine Laufzeitreflexion in Protokoll- oder Typcodecs; AOT-Smoke auf jeder unterstützten Plattform.

## Hauptfunktionen

<details>
<summary><b>Verbindung und Protokoll</b> (6 Elemente)</summary>

| Funktion | Nutzen |
|----------|--------|
| Aliase für Verbindungszeichenfolgen | `Host`/`Server`/`Data Source`, `Catalog`/`Database`, `Schema`/`Owner` und XCode-Schreibweisen |
| PrestoDB-/Trino-Header | Standard `Protocol=Presto`; für Trino 351+ `Protocol=Trino` oder `TrinoHeaders=true` |
| Sitzungskontext | Catalog, Schema, Benutzer, Quelle, Zeitzone, Sitzungseigenschaften und benutzerdefinierte Header |
| Authentifizierung | Basic, JWT/Bearer und benutzerdefinierte Gateway-Header; Geheimnisse bleiben aus Protokollen |
| TLS und Proxys | Standardmäßig strenge Zertifikatsprüfung; private CAs nur bei ausdrücklicher Freigabe |
| Gemeinsamer HTTP-Transport | `HttpClient` wird je Endpunkt wiederverwendet, mit Proxy, Komprimierung und Timeouts |

</details>

<details>
<summary><b>Abfrage und Typen</b> (6 Elemente)</summary>

| Funktion | Nutzen |
|----------|--------|
| Anweisungslebenszyklus | `POST /v1/statement`, `nextUri`-Abfrage und Abschlusserkennung |
| Vorwärtskompatible Zustände | Zustände sind Zeichenfolgen, neue Serverzustände brechen die Analyse nicht |
| Wiederholung bei temporären Fehlern | 429/502/503/504 mit `Retry-After` oder exponentiellem Backoff |
| `@name`-Parameter | Clientseitige Ersetzung, die Zeichenfolgen, Bezeichner und Kommentare überspringt |
| Typentreue | Boolean, Ganzzahl, Decimal, Datum/Uhrzeit, timestamp with time zone, UUID, IP und Binär |
| Komplexe Typen | `array`/`map`/`row`/`json` werden in v1 als Rohzeichenfolgen zurückgegeben |

</details>

<details>
<summary><b>Integration und Diagnose</b> (6 Elemente)</summary>

| Funktion | Nutzen |
|----------|--------|
| `GetSchema` | `MetaDataCollections`, `Tables`, `Columns`, `DataTypes` |
| XCode-Integration | Standardtreiberwechsel mit Fallback auf den alten Treiber; keine Transaktions-, DDL- oder Auto-Increment-Zusagen |
| Lokalisierung | 33 Laufzeitsprachen, zur Kompilierzeit erzeugt und AOT-sicher |
| Diagnose | Abfrage-ID, Fehlercode, SQLState und Dauer bleiben in Ausnahmen und Protokollen erhalten |
| OpenTelemetry | `ActivitySource("NewLife.Presto")` in modernen Frameworks |
| Paketierung | Starke Namen, SourceLink, Symbolpaket und README.nuget.md |

</details>

## Plattformunterstützung

| Bereich | Unterstützt |
|---------|-------------|
| Betriebssysteme | Windows, Linux (glibc und musl), macOS |
| Architekturen | x64, arm64; LoongArch64 und riscv64 über beobachtete Builds |
| Ziel Frameworks | net45, net461, netstandard2.0, netstandard2.1, net6.0, net8.0, net10.0, net11.0 |
| NativeAOT | net8.0+ ohne Trimm-/AOT-Analyzerwarnungen; Pflicht-Smoke auf win-x64, linux-x64, linux-arm64 |
| CI-Matrix | Windows, Linux x64, Linux arm64, musl-Container, macOS und Windows arm64 (beobachtet) |

## Lokalisierung

| Umfang | Abdeckung |
|--------|-----------|
| Laufzeitmeldungen | 33 Sprachen einschließlich Englisch; fehlende Übersetzungen fallen auf Englisch zurück |
| README | 11 Sprachen, erzeugt aus `readme-languages.json` |
| Metadaten | Assemblytitel und -beschreibung sind englische Quelltexte und werden bei der Anzeige übersetzt |

> Übersetzungsschlüssel sind der englische Quelltext. Nach dem Hinzufügen einer Meldung im Code muss derselbe Schlüssel in allen Sprachdateien ergänzt werden; `tools/check-locales.ps1 -RequireTranslated` prüft Schlüssel, Platzhalter und Vollständigkeit in CI.

## Dokumentation

- [需求文档](Doc/需求文档.md) — Anforderungen und Umfang
- [功能清单](Doc/功能清单.md) — Implementierungs-/Test-/Kommentarstatus
- [架构设计](Doc/架构设计.md) — Komponenten, Protokollablauf, Typzuordnung, Entscheidungen
- [竞品分析](Doc/竞品分析.md) — Referenzprojekte, Funktionsmatrix, Lücken

## Mitwirken

- Build: `dotnet build NewLife.Presto.slnx -c Release`
- Test: `dotnet test UnitTest/UnitTest.csproj -c Release`
- Lokalisierung: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- NewLife-C#-Konventionen befolgen und jede Laufzeitmeldung in die Sprachdateien aufnehmen.
- `ChangeLog.md` und Paket-Release-Notes außerhalb des Release-Prozesses nicht ändern.

## Ökosystem-Karte

NewLife ist ein geschichtetes Ökosystem unabhängig versionierter Open-Source-Projekte. Die folgende Karte wird wortgleich in allen NewLife-Repositories verwendet; jedes Projekt verlinkt auf sein Repository und sein primäres NuGet-Paket.

| Projekt | Funktion | Downloads |
| --- | --- | --- |
| **Grundlage** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Protokollierung, Konfiguration, Caching, Netzwerk, Serialisierung, Zeitplanung und APM-Tracing. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Daten-Middleware: einzelne Tabellen in der Größenordnung von zig Milliarden Zeilen, automatisches Sharding, Lese-/Schreibtrennung, über 10 Datenbanken. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | Hochperformante Netzwerkbibliothek: 1,77 Mio. pps / 14,5 Gbps auf einer Maschine (TCP 1KB, 2026-10), 4 Mio. langlebige TCP-Verbindungen. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Protokollkommunikation und RPC-Framework für Client/Server-Anwendungen und IoT-Geräte. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Installiert .NET-Anwendungen als Betriebssystem-Dienste: Windows-Dienste und Linux-systemd. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | `dotnet new`-Projektvorlagen für Konsole, Web, API, Dienst, Daten und IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Daten & Messaging** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Redis-Client mit Mikrosekunden-Latenz, Millionen Operationen pro Sekunde und integrierten Nachrichtenwarteschlangen. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Vollständig verwalteter RocketMQ-Client für Apache RocketMQ und Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | MQTT-Client und -Server, einschließlich Alibaba Cloud IoT-Anbindung. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Hybriddatenbank in C#: eingebettet oder im Netzwerk, relational, Zeitreihen, Nachrichtenwarteschlange und KV in einem. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | MySql-Treiber und Hilfsschicht auf Basis des NewLife-Datenstacks. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, KI & verteilt** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Schnelles Framework für Administrationsplattformen: Berechtigungen, SSO, OAuth-Server und CRUD-Gerüst. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Verteilte Dienstplattform: Knotenverwaltung, APM, Konfigurationszentrum, Registry und Release-Center. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Plattform für verteilte Aufgaben und Echtzeit-Datenplanung mit Sharding, Wiederholungen und Backfill. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Einheitlicher Client für 46 LLM-Anbieter plus ChatAI, eine sofort einsatzbereite Web-Chat-Anwendung. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Industrie & IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | IoT-Standardbibliothek mit Protokollverträgen und Geräteabstraktionen. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Modbus-TCP/RTU/ASCII-Implementierung auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Siemens-SPS-Protokoll auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Omron-SPS-Protokoll auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Mitsubishi-Melsec-SPS-Protokoll auf Basis von NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | OPC-Client für industriellen Datenzugriff. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | BACnet-Protokollstack für die Gebäudeautomation. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Leichtgewichtige Open-Source-IoT-Plattform mit Gateway- und Geräteverwaltung. | — |
| **Fahrzeugtelematik** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | JT/T-808-Fahrzeugortungsprotokoll: über 70 Nachrichtenkörper, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | JT/T-809-Plattform-zu-Plattform-Protokoll für Verkehrsbehörden. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | JT/T-1078-Fahrzeugvideo: RTP-Parsing, FLV/fMP4-Remuxing und Live-Streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Domänenkomponenten** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Ein Paket für Excel, Word, PPT, PDF und über 15 Dokumentformate. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Plattformübergreifendes Audio: Codecs, Geräte-I/O, DSP, Container und Streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Videocodecs, Containerformate, Frame-Verarbeitung und Streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Einheitliche Schnittstelle für Baidu, AMap, Tencent und Tianditu mit Geokodierung und Koordinatenumrechnung. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | IPv4-Geolokalisierung mit einer offline speichergemappten Datenbank und einer Online-API als Fallback. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Werkzeuge & Produkte** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Entwickler-Toolbox: Netzwerk, serielle Schnittstelle, Krypto, Regex, Modbus und MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Selbst gehostetes Git-Quellcode-Verwaltungssystem. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Installiert automatisch die passende .NET-Laufzeit, wenn es von einem älteren Host gestartet wird. | — |

> Die Badges zeigen das primäre Paket jedes Projekts; Theme-, Erweiterungs- und Satellitenpakete sind auf den jeweiligen NuGet-Seiten aufgeführt. Die hier nicht aufgeführten über 30 Pakete (FTP, LoRa, Office-Erweiterungen, Protokolltreiber und mehr) sind in der [NewLifeX-Organisation](https://github.com/NewLifeX) und auf [newlifex.com](https://newlifex.com) verfügbar.

## Team

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

Das NewLife-Team wurde 2002 gegründet und arbeitet seit mehr als zwei Jahrzehnten am .NET- und IoT-Ökosystem. Es betreut nahezu 100 Open-Source-Projekte (34 davon sind Kern dieses Ökosystems) und liefert integrierte Hard- und Softwarelösungen für die Bereiche Energie, Logistik, Gesundheitswesen, Industrieautomation und Internet. Unser Leitspruch lautet „Lernen kennt kein Dienstalter – wer es beherrscht, wird zum Lehrmeister“.

Die Open-Source-Komponenten wurden <!--nuget-total:start-->6.48M<!--nuget-total:end--> Mal von NuGet heruntergeladen (ökosystemweit, Stand 2026-10).

Website: https://newlifex.com · Open-Source-Organisation: https://github.com/NewLifeX

<details>
<summary>WeChat-Offizieller Account: 智能大石头 (zum Folgen scannen)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## Lizenz

[MIT](LICENSE)
