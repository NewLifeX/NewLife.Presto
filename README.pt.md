# NewLife.Presto — driver ADO.NET para Presto/Trino

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto** é um driver ADO.NET em C# puro para PrestoDB e Trino da equipe NewLife. Ele usa o protocolo cliente HTTP REST v1 oficial, depende apenas de `NewLife.Core` com **zero dependências de terceiros**, inclui **33 idiomas de execução**, oferece suporte de `net45` a `net11` e está **pronto para NativeAOT** em `net8.0+`. PrestoDB e Trino compartilham um único pacote; `Protocol=Trino` (ou `TrinoHeaders=true`) alterna os cabeçalhos de solicitação.

> **Estado do projeto**: fase de design e scaffolding. Os componentes offline (string de conexão, cabeçalhos de protocolo, mapeamento de tipos, localização, teste AOT) estão implementados e testados; a rede e a execução de consultas são acompanhadas na [lista de funções](Doc/功能清单.md).

**Conteúdo**: [Início rápido](#início-rápido) · [Por que NewLife.Presto](#por-que-newlifepresto) · [Principais recursos](#principais-recursos) · [Suporte de plataformas](#suporte-de-plataformas) · [Localização](#localização) · [Documentação](#documentação) · [Contribuição](#contribuição) · [Mapa do ecossistema](#mapa-do-ecossistema) · [Equipe](#equipe) · [Licença](#licença)

---

## Início rápido

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

## Por que NewLife.Presto

- **Um pacote para PrestoDB e Trino**: a mesma implementação de protocolo, com cabeçalhos `X-Presto-*` / `X-Trino-*` escolhidos por configuração.
- **XCode primeiro**: fábrica, aliases de string de conexão, parâmetros `@`, metadados e limites de capacidade projetados para o modelo Presto do XCode.
- **Zero dependências de terceiros**: apenas `NewLife.Core`; JSON, HTTP e logs vêm da BCL ou da pilha NewLife.
- **Todos os frameworks e plataformas**: `net45` ~ `net11`, Windows / Linux / macOS, x64 / arm64, além de LoongArch64 e musl em builds de observação.
- **33 idiomas de execução**: exceções, logs e metadados do driver localizados, com inglês como origem e fallback.
- **Pronto para NativeAOT**: sem reflexão em tempo de execução nos codecs de protocolo ou tipos; teste AOT em cada plataforma suportada.

## Principais recursos

<details>
<summary><b>Conexão e protocolo</b> (6 itens)</summary>

| Recurso | O que oferece |
|---------|---------------|
| Aliases de string de conexão | `Host`/`Server`/`Data Source`, `Catalog`/`Database`, `Schema`/`Owner` e sintaxe XCode |
| Cabeçalhos PrestoDB / Trino | `Protocol=Presto` por padrão; `Protocol=Trino` ou `TrinoHeaders=true` para Trino 351+ |
| Contexto de sessão | Catalog, schema, usuário, origem, fuso horário, propriedades de sessão e cabeçalhos personalizados |
| Autenticação | Basic, JWT/Bearer e cabeçalhos de gateway personalizados, sem segredos nos logs |
| TLS e proxies | Validação estrita de certificados por padrão; exceção explícita para CAs privadas |
| Transporte HTTP compartilhado | Reutiliza `HttpClient` por endpoint, com proxy, compactação e tempos limite |

</details>

<details>
<summary><b>Consultas e tipos</b> (6 itens)</summary>

| Recurso | O que oferece |
|---------|---------------|
| Ciclo de vida da instrução | `POST /v1/statement`, polling de `nextUri` e detecção de conclusão |
| Estados compatíveis com versões futuras | Estados são strings; novos estados do servidor não quebram a análise |
| Repetição transitória | 429/502/503/504 com `Retry-After` ou backoff exponencial |
| Parâmetros `@name` | Substituição no cliente que ignora strings, identificadores e comentários |
| Fidelidade de tipos | Booleano, inteiro, decimal, data/hora, timestamp with time zone, UUID, IP e binário |
| Tipos complexos | `array`/`map`/`row`/`json` retornam como strings brutas na v1 |

</details>

<details>
<summary><b>Integração e diagnóstico</b> (6 itens)</summary>

| Recurso | O que oferece |
|---------|---------------|
| `GetSchema` | `MetaDataCollections`, `Tables`, `Columns`, `DataTypes` |
| Integração com XCode | Troca para o driver padrão com fallback para o anterior; sem promessas de transação, DDL ou auto-incremento |
| Localização | 33 idiomas de execução, gerados em tempo de compilação e compatíveis com AOT |
| Diagnóstico | ID da consulta, código de erro, SQLState e duração preservados em exceções e logs |
| OpenTelemetry | `ActivitySource("NewLife.Presto")` em frameworks modernos |
| Empacotamento | Nome forte, SourceLink, pacote de símbolos e README.nuget.md |

</details>

## Suporte de plataformas

| Área | Suportado |
|------|-----------|
| Sistemas operacionais | Windows, Linux (glibc e musl), macOS |
| Arquiteturas | x64, arm64; LoongArch64 e riscv64 por builds de observação |
| Frameworks de destino | net45, net461, netstandard2.0, netstandard2.1, net6.0, net8.0, net10.0, net11.0 |
| NativeAOT | net8.0+ sem avisos do analisador de trim/AOT; teste bloqueante em win-x64, linux-x64, linux-arm64 |
| Matriz CI | Windows, Linux x64, Linux arm64, contêineres musl, macOS e Windows arm64 (observação) |

## Localização

| Escopo | Cobertura |
|--------|-----------|
| Mensagens de execução | 33 idiomas incluindo inglês; traduções ausentes retornam ao inglês |
| README | 11 idiomas gerados a partir de `readme-languages.json` |
| Metadados | O título e a descrição do assembly são texto de origem em inglês e traduzidos na exibição |

> As chaves de tradução são o texto de origem em inglês. Após adicionar uma mensagem no código, adicione a mesma chave em cada arquivo de idioma; `tools/check-locales.ps1 -RequireTranslated` valida chaves, marcadores e completude na CI.

## Documentação

- [需求文档](Doc/需求文档.md) — requisitos e escopo
- [功能清单](Doc/功能清单.md) — status de implementação/testes/comentários
- [架构设计](Doc/架构设计.md) — componentes, fluxo do protocolo, mapeamento de tipos, decisões
- [竞品分析](Doc/竞品分析.md) — projetos de referência, matriz de recursos, lacunas

## Contribuição

- Build: `dotnet build NewLife.Presto.slnx -c Release`
- Testes: `dotnet test UnitTest/UnitTest.csproj -c Release`
- Localização: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- Siga as convenções C# da NewLife e mantenha cada mensagem de execução nas tabelas de idioma.
- Não altere `ChangeLog.md` nem as notas de versão do pacote fora do processo de release.

## Mapa do ecossistema

NewLife é um ecossistema em camadas de projetos de código aberto com versionamento independente. O mapa abaixo é compartilhado literalmente por todos os repositórios NewLife; cada projeto aponta para o seu repositório e o seu pacote principal no NuGet.

| Projeto | O que faz | Downloads |
| --- | --- | --- |
| **Fundação** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Logs, configuração, cache, rede, serialização, agendamento e rastreamento APM. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Middleware de dados: tabelas únicas na escala de dezenas de bilhões de linhas, particionamento automático, separação de leitura/escrita, mais de 10 bancos de dados. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | Biblioteca de rede de alto desempenho: 1,77 M pps / 14,5 Gbps em uma única máquina (TCP 1KB, 2026-10), 4 M conexões TCP persistentes. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Framework de comunicação por protocolo e RPC para aplicações cliente/servidor e dispositivos IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Instala aplicações .NET como daemons do sistema operacional: serviços do Windows e systemd do Linux. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | Templates de projeto `dotnet new` para console, web, API, serviço, dados e IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Dados e mensageria** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Cliente Redis com latência de microssegundos, milhões de operações por segundo e filas de mensagens integradas. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Cliente RocketMQ totalmente gerenciado para Apache RocketMQ e Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | Cliente e servidor MQTT, incluindo conectividade com o Alibaba Cloud IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Banco de dados híbrido em C#: embarcado ou em rede, relacional, séries temporais, fila de mensagens e KV em um só. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | Driver MySql e camada de apoio construídos sobre a stack de dados NewLife. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, IA e distribuído** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Framework rápido para plataformas administrativas: permissões, SSO, servidor OAuth e scaffolding de CRUD. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Plataforma distribuída de serviços: gestão de nós, APM, central de configuração, registro e central de distribuição. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Plataforma distribuída de tarefas e agendamento de dados em tempo real, com particionamento, novas tentativas e preenchimento retroativo. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Cliente unificado para 46 provedores de LLM, além do ChatAI, uma aplicação web de chat pronta para uso. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Industrial e IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | Biblioteca padrão de IoT que define contratos de protocolo e abstrações de dispositivos. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Implementação de Modbus TCP/RTU/ASCII sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Protocolo de PLC Siemens sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Protocolo de PLC Omron sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Protocolo de PLC Mitsubishi Melsec sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | Cliente OPC para acesso a dados industriais. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | Stack do protocolo BACnet para automação predial. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Plataforma IoT de código aberto e leve, com gestão de gateways e dispositivos. | — |
| **Telemática veicular** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | Protocolo de posicionamento veicular JT/T 808: mais de 70 corpos de mensagem, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | Protocolo plataforma-a-plataforma JT/T 809 para autoridades de transporte. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | Vídeo embarcado JT/T 1078: análise RTP, remux FLV/fMP4 e transmissão ao vivo. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Componentes de domínio** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Um único pacote para Excel, Word, PPT, PDF e mais de 15 formatos de documento. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Áudio multiplataforma: codecs, E/S de dispositivos, DSP, contêineres e streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Codecs de vídeo, formatos de contêiner, processamento de quadros e streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Interface unificada sobre Baidu, AMap, Tencent e Tianditu, com geocodificação e conversão de coordenadas. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | Geolocalização IPv4 com base de dados offline mapeada em memória e uma API online como alternativa. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Ferramentas e produtos** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Caixa de ferramentas do desenvolvedor: rede, porta serial, criptografia, expressões regulares, Modbus e MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Sistema de gestão de código-fonte Git auto-hospedado. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Instala automaticamente o runtime .NET correspondente quando iniciado a partir de um host mais antigo. | — |

> Os badges mostram o pacote principal de cada projeto; pacotes de temas, extensões e satélites estão listados nas suas páginas do NuGet. Os mais de 30 pacotes não listados aqui (FTP, LoRa, extensões do Office, drivers de protocolo e outros) estão disponíveis na [organização NewLifeX](https://github.com/NewLifeX) e em [newlifex.com](https://newlifex.com).

## Equipe

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

Fundada em 2002, a equipe NewLife dedica-se há mais de duas décadas ao ecossistema .NET e IoT, mantém cerca de 100 projetos de código aberto (34 deles centrais neste mapa) e entrega soluções integradas de software e hardware para os setores de energia, logística, saúde, controle industrial e internet. Seguimos o lema "no aprendizado não há antiguidade — o domínio conquista o respeito".

Os componentes de código aberto somam <!--nuget-total:start-->6.48M<!--nuget-total:end--> downloads no NuGet (ecossistema completo, em 2026-10).

Site: https://newlifex.com · Organização de código aberto: https://github.com/NewLifeX

<details>
<summary>Conta oficial do WeChat: 智能大石头 (escaneie para seguir)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## Licença

[MIT](LICENSE)
