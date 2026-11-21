# NewLife.Presto — pilote ADO.NET pour Presto/Trino

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto** est un pilote ADO.NET en C# pur pour PrestoDB et Trino, développé par l’équipe NewLife. Il utilise le protocole client HTTP REST v1 officiel, ne dépend que de `NewLife.Core` avec **aucune dépendance tierce**, embarque **33 langues d’exécution**, cible `net45` à `net11` et est **compatible NativeAOT** à partir de `net8.0`. PrestoDB et Trino partagent un seul paquet ; `Protocol=Trino` (ou `TrinoHeaders=true`) change les en-têtes de requête.

> **État du projet** : implémenté et testé — pilote ADO.NET de base plus fournisseurs EF Core et EF6 en lecture seule, livrés en trois packages NuGet par le même pipeline de publication. 503 tests unitaires du cœur, tests des fournisseurs EF et intégration réelle Trino/PrestoDB passent ; le Spooling couvre les segments JSON non compressés et bascule sinon sur le protocole direct. Voir la [liste des fonctions](Doc/功能清单.md).

**Sommaire**: [Démarrage rapide](#démarrage-rapide) · [Pourquoi NewLife.Presto](#pourquoi-newlifepresto) · [Fonctions principales](#fonctions-principales) · [Prise en charge des plateformes](#prise-en-charge-des-plateformes) · [Localisation](#localisation) · [Documentation](#documentation) · [Contribution](#contribution) · [Carte de l'écosystème](#carte-de-lécosystème) · [Équipe](#équipe) · [Licence](#licence)

---

## Démarrage rapide

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

## Pourquoi NewLife.Presto

- **Un seul paquet pour PrestoDB et Trino** : même implémentation du protocole, en-têtes `X-Presto-*` / `X-Trino-*` choisis par configuration.
- **XCode d’abord** : fabrique, alias de chaîne de connexion, paramètres `@`, métadonnées et limites de capacités conçus pour le modèle Presto de XCode.
- **Aucune dépendance tierce** : uniquement `NewLife.Core` ; JSON, HTTP et journalisation viennent de la BCL ou de la pile NewLife.
- **Toutes les frameworks et plateformes** : `net45` ~ `net11`, Windows / Linux / macOS, x64 / arm64, plus LoongArch64 et musl en builds d’observation.
- **33 langues d’exécution** : exceptions, journaux et métadonnées du pilote localisés, l’anglais servant de source et de repli.
- **Compatible NativeAOT** : aucune réflexion d’exécution dans les codecs de protocole ou de types ; test AOT sur chaque plateforme prise en charge.

## Fonctions principales

<details>
<summary><b>Connexion et protocole</b> (6 éléments)</summary>

| Fonction | Apport |
|----------|--------|
| Alias de chaîne de connexion | `Host`/`Server`/`Data Source`, `Catalog`/`Database`, `Schema`/`Owner` et syntaxes XCode |
| En-têtes PrestoDB / Trino | `Protocol=Presto` par défaut ; `Protocol=Trino` ou `TrinoHeaders=true` pour Trino 351+ |
| Contexte de session | Catalog, schéma, utilisateur, source, fuseau horaire, propriétés de session et en-têtes personnalisés |
| Authentification | Basic, JWT/Bearer et en-têtes de passerelle personnalisés, secrets absents des journaux |
| TLS et proxys | Validation stricte des certificats par défaut ; dérogation explicite pour les AC privées |
| Transport HTTP partagé | Réutilisation de `HttpClient` par point de terminaison, avec proxy, compression et délais |

</details>

<details>
<summary><b>Requêtes et types</b> (6 éléments)</summary>

| Fonction | Apport |
|----------|--------|
| Cycle de vie des instructions | `POST /v1/statement`, interrogation de `nextUri` et détection de fin |
| États compatibles ascendants | Les états sont des chaînes, les nouveaux états serveur ne cassent pas l’analyse |
| Nouvelle tentative transitoire | 429/502/503/504 avec `Retry-After` ou backoff exponentiel |
| Paramètres `@name` | Substitution côté client ignorant chaînes, identificateurs et commentaires |
| Fidélité des types | Booléen, entier, decimal, date/heure, timestamp with time zone, UUID, IP et binaire |
| Types complexes | `array`/`map`/`row`/`json` renvoyés en chaînes brutes en v1 |

</details>

<details>
<summary><b>Intégration et diagnostics</b> (6 éléments)</summary>

| Fonction | Apport |
|----------|--------|
| `GetSchema` | `MetaDataCollections`, `Tables`, `Columns`, `DataTypes` |
| Intégration XCode | Bascule vers le pilote par défaut avec repli sur l’ancien ; aucune promesse de transaction, DDL ou auto-incrément |
| Localisation | 33 langues d’exécution, générées à la compilation et compatibles AOT |
| Diagnostics | ID de requête, code d’erreur, SQLState et durée conservés dans les exceptions et journaux |
| OpenTelemetry | `ActivitySource("NewLife.Presto")` sur les frameworks modernes |
| Empaquetage | Nom fort, SourceLink, paquet de symboles et README.nuget.md |

</details>

## Prise en charge des plateformes

| Domaine | Pris en charge |
|---------|----------------|
| Systèmes d’exploitation | Windows, Linux (glibc et musl), macOS |
| Architectures | x64, arm64 ; LoongArch64 et riscv64 via des builds d’observation |
| Frameworks cibles | net45, net461, netstandard2.0, netstandard2.1, net6.0, net8.0, net10.0, net11.0 |
| NativeAOT | net8.0+ sans avertissement d’analyse de trim/AOT ; test bloquant sur win-x64, linux-x64, linux-arm64 |
| Matrice CI | Windows, Linux x64, Linux arm64, conteneurs musl, macOS et Windows arm64 (observation) |

## Localisation

| Portée | Couverture |
|--------|------------|
| Messages d’exécution | 33 langues dont l’anglais ; les traductions manquantes reviennent à l’anglais |
| README | 11 langues générées depuis `readme-languages.json` |
| Métadonnées | Le titre et la description de l’assembly sont des sources anglaises, traduits à l’affichage |

> Les clés de traduction sont le texte source anglais. Après l’ajout d’un message dans le code, ajoutez la même clé dans chaque fichier de langue ; `tools/check-locales.ps1 -RequireTranslated` vérifie les clés, les espaces réservés et la complétude en CI.

## Documentation

- [需求文档](Doc/需求文档.md) — exigences et périmètre
- [功能清单](Doc/功能清单.md) — état implémentation/test/commentaires
- [架构设计](Doc/架构设计.md) — composants, flux de protocole, mappage des types, décisions
- [竞品分析](Doc/竞品分析.md) — projets de référence, matrice de fonctions, écarts

## Contribution

- Build : `dotnet build NewLife.Presto.slnx -c Release`
- Tests : `dotnet test UnitTest/UnitTest.csproj -c Release`
- Localisation : `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- Respectez les conventions C# NewLife et ajoutez chaque message d’exécution aux tables de langue.
- Ne modifiez pas `ChangeLog.md` ni les notes de version du paquet hors du processus de release.

## Carte de l'écosystème

NewLife est un écosystème en couches de projets open source versionnés indépendamment. La carte ci-dessous est reprise à l'identique par tous les dépôts NewLife ; chaque projet renvoie à son dépôt et à son paquet NuGet principal.

| Projet | Rôle | Téléchargements |
| --- | --- | --- |
| **Fondation** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Journalisation, configuration, cache, réseau, sérialisation, ordonnancement et traçage APM. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Intergiciel de données : tables uniques à l'échelle de dizaines de milliards de lignes, partitionnement automatique, séparation lecture/écriture, plus de 10 bases de données. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | Bibliothèque réseau hautes performances : 1,77 M pps / 14,5 Gbps sur une seule machine (TCP 1KB, 2026-10), 4 M de connexions TCP persistantes. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Communication protocolaire et framework RPC pour applications client/serveur et appareils IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Installe des applications .NET comme démons du système d'exploitation : services Windows et systemd Linux. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | Modèles de projet `dotnet new` pour la console, le web, l'API, les services, les données et l'IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Données et messagerie** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Client Redis avec une latence en microsecondes, des millions d'opérations/s et des files de messages intégrées. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Client RocketMQ entièrement managé pour Apache RocketMQ et Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | Client et serveur MQTT, y compris la connectivité Alibaba Cloud IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Base de données hybride en C# : embarquée ou en réseau, relationnelle, séries temporelles, file de messages et KV réunis. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | Pilote MySql et couche d'assistance construits sur la pile de données NewLife. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, IA et distribué** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Framework rapide de plateforme d'administration : permissions, SSO, serveur OAuth et échafaudage CRUD. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Plateforme de services distribués : gestion des nœuds, APM, centre de configuration, registre et centre de publication. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Plateforme de tâches distribuées et d'ordonnancement de données en temps réel avec partitionnement, reprises et backfill. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Client unifié pour 46 fournisseurs de LLM, plus ChatAI, une application web de chat prête à l'emploi. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Industriel et IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | Bibliothèque standard IoT définissant les contrats de protocole et les abstractions d'appareils. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Implémentation Modbus TCP/RTU/ASCII au-dessus de NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Protocole pour automate Siemens au-dessus de NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Protocole pour automate Omron au-dessus de NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Protocole pour automate Mitsubishi Melsec au-dessus de NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | Client OPC pour l'accès aux données industrielles. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | Pile de protocoles BACnet pour l'automatisation des bâtiments. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Plateforme IoT open source légère avec gestion des passerelles et des appareils. | — |
| **Télématique embarquée** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | Protocole de positionnement de véhicules JT/T 808 : plus de 70 corps de message, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | Protocole plateforme à plateforme JT/T 809 pour les autorités de transport. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | Vidéo embarquée JT/T 1078 : analyse RTP, remuxage FLV/fMP4 et diffusion en direct. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Composants métier** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Un seul paquet pour Excel, Word, PPT, PDF et plus de 15 formats de documents. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Audio multiplateforme : codecs, E/S d'appareils, DSP, conteneurs et streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Codecs vidéo, formats de conteneurs, traitement des images et streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Interface unifiée pour Baidu, AMap, Tencent et Tianditu avec géocodage et conversion de coordonnées. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | Géolocalisation IPv4 avec une base de données hors ligne mappée en mémoire et une API en ligne en secours. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Outils et produits** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Boîte à outils du développeur : réseau, port série, cryptographie, regex, Modbus et MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Système de gestion de code source Git auto-hébergé. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Installe automatiquement le runtime .NET correspondant lorsqu'il est lancé depuis un hôte plus ancien. | — |

> Les badges indiquent le paquet principal de chaque projet ; les paquets de thèmes, d'extensions et satellites sont répertoriés sur leurs pages NuGet. Les plus de 30 paquets non listés ici (FTP, LoRa, extensions Office, pilotes de protocoles, etc.) sont disponibles dans l'[organisation NewLifeX](https://github.com/NewLifeX) et sur [newlifex.com](https://newlifex.com).

## Équipe

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

Fondée en 2002, l'équipe NewLife œuvre depuis plus de deux décennies dans l'écosystème .NET et IoT, maintient près de 100 projets open source (dont 34 sont essentiels à cette carte) et fournit des solutions matérielles et logicielles intégrées aux secteurs de l'énergie, de la logistique, de la santé, du contrôle industriel et d'Internet. Notre devise : « l'apprentissage ne connaît pas l'ancienneté — celui qui maîtrise devient maître ».

Les composants open source ont été téléchargés <!--nuget-total:start-->6.48M<!--nuget-total:end--> fois depuis NuGet (à l'échelle de l'écosystème, en date de 2026-10).

Site web : https://newlifex.com · Organisation open source : https://github.com/NewLifeX

<details>
<summary>Compte officiel WeChat : 智能大石头 (scannez pour suivre)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## Licence

[MIT](LICENSE)
