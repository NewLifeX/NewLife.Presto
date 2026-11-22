# NewLife.Presto — controlador ADO.NET para Presto/Trino

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto** es un controlador ADO.NET en C# puro para PrestoDB y Trino del equipo NewLife. Usa el protocolo cliente HTTP REST v1 oficial, depende solo de `NewLife.Core` con **cero dependencias de terceros**, incluye **33 idiomas de ejecución**, es compatible con `net45` a `net11` y está **listo para NativeAOT** en `net8.0+`. PrestoDB y Trino comparten un paquete; `Protocol=Trino` (o `TrinoHeaders=true`) cambia los encabezados de solicitud.

> **Estado del proyecto**: implementado y probado — controlador ADO.NET principal más proveedores EF Core y EF6 de solo lectura, entregados como tres paquetes NuGet por el mismo flujo de publicación. Pasan 561 pruebas unitarias del núcleo, las de los proveedores EF y la integración real con Trino/PrestoDB; Spooling cubre segmentos JSON sin comprimir y, en otro caso, vuelve al protocolo directo. Ver la [lista de funciones](Doc/功能清单.md).

**Contenido**: [Inicio rápido](#inicio-rápido) · [Por qué NewLife.Presto](#por-qué-newlifepresto) · [Comparación y benchmarks](#comparación-y-benchmarks) · [Funciones principales](#funciones-principales) · [Compatibilidad de plataformas](#compatibilidad-de-plataformas) · [Localización](#localización) · [Documentación](#documentación) · [Contribuir](#contribuir) · [Mapa del ecosistema](#mapa-del-ecosistema) · [Equipo](#equipo) · [Licencia](#licencia)

---

## Inicio rápido

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

## Por qué NewLife.Presto

- **Un paquete para PrestoDB y Trino**: la misma implementación de protocolo, con encabezados `X-Presto-*` / `X-Trino-*` seleccionados por configuración.
- **XCode primero**: fábrica, alias de cadena de conexión, parámetros `@`, metadatos y límites de capacidad diseñados para el modelo Presto de XCode.
- **Cero dependencias de terceros**: solo `NewLife.Core`; JSON, HTTP y registro provienen de la BCL o de la pila NewLife.
- **Todas las plataformas y frameworks**: `net45` ~ `net11`, Windows / Linux / macOS, x64 / arm64, más LoongArch64 y musl como compilaciones de observación.
- **33 idiomas de ejecución**: excepciones, registros y metadatos del controlador localizados, con inglés como origen y respaldo.
- **Listo para NativeAOT**: sin reflexión en tiempo de ejecución en los códecs de protocolo o tipos; prueba AOT en cada plataforma compatible.

## Comparación y benchmarks

En la misma ronda de pruebas comparamos este controlador con los demás controladores .NET ADO.NET de Presto/Trino y con una línea base HTTP v1 sin abstracción: un proceso, el mismo SQL, mediana de tres ejecuciones, sobre Trino 476 y PrestoDB 0.300.

| Aspecto | Resultado |
|---------|-----------|
| Latencia de consulta | La más baja o empatada del grupo ADO en todos los escenarios de consulta, desde la conexión y la primera consulta hasta el escaneo de 15.000 filas y la tabla ancha de 16 columnas, con la mayor ventaja en los escaneos |
| Asignación administrada | La más baja en todos los escenarios y por debajo de la línea base HTTP: 9%–52% del competidor más rápido |
| Concurrencia de 8 vías | Al nivel o algo por debajo de la línea base HTTP; algo por debajo del competidor más rápido en Trino y algo por encima en PrestoDB |
| Entorno | Bucle local, texto plano, sin autenticación; el orden se mantiene entre rondas y los valores absolutos varían un 20%–80% |

Son mediciones propias de un solo entorno. Las tablas completas, el método y la reproducción están en [性能测试](Doc/性能测试.md); las versiones de paquetes y la matriz de capacidades, en [竞品分析](Doc/竞品分析.md).

**Compromisos y límites**

- Las transacciones, el DDL, las columnas de identidad y los procedimientos almacenados quedan fuera del alcance del controlador; los proveedores EF Core y EF6 son de solo lectura y rechazan escrituras y migraciones.
- El spooling solo cubre segmentos JSON sin comprimir; otras codificaciones vuelven al protocolo directo.
- El análisis de flujo UTF-8 solo existe en `net6.0+`; `net45`, `net461` y `netstandard2.x` usan la ruta de diccionario y asignan más.
- Aún fuera de la prueba: rendimiento tras publicar con AOT, celdas de más de 64 KB y pasarelas JWT/OAuth2 reales.
- El alcance es ADO.NET y proveedores EF de solo lectura; no hay una API de consulta estilo SDK.

## Funciones principales

<details>
<summary><b>Conexión y protocolo</b> (6 elementos)</summary>

| Función | Valor |
|---------|-------|
| Alias de cadena de conexión | `Host`/`Server`/`Data Source`, `Catalog`/`Database`, `Schema`/`Owner` y sintaxis de XCode |
| Encabezados PrestoDB / Trino | `Protocol=Presto` por defecto; `Protocol=Trino` o `TrinoHeaders=true` para Trino 351+ |
| Contexto de sesión | Catalog, esquema, usuario, origen, zona horaria, propiedades de sesión y encabezados personalizados |
| Autenticación | Basic, JWT/Bearer, credenciales de cliente OAuth2 y encabezados de puerta de enlace personalizados, sin secretos en los registros |
| TLS y proxies | Validación estricta de certificados por defecto; excepción explícita para CA privadas |
| Transporte HTTP compartido | Reutiliza `HttpClient` por punto de conexión, con proxy, compresión y tiempos de espera |

</details>

<details>
<summary><b>Consultas y tipos</b> (8 elementos)</summary>

| Función | Valor |
|---------|-------|
| Ciclo de vida de la instrucción | `POST /v1/statement`, sondeo de `nextUri` y detección de finalización |
| Estados compatibles hacia adelante | Los estados son cadenas, los nuevos estados del servidor no rompen el análisis |
| Reintento transitorio | 429/502/503/504 con `Retry-After` o retroceso exponencial |
| Parámetros `@name` | Sustitución en el cliente que omite cadenas, identificadores y comentarios |
| Fidelidad de tipos | Booleano, entero, decimal, fecha/hora, timestamp with time zone, UUID, IP y binario |
| Tipos complejos | `array`/`map`/`row` devuelven cadenas sin formato por defecto; `ComplexTypes=Structured` devuelve `object[]` y diccionarios |
| `DbBatch` (.NET 6+) | Comandos por lotes secuenciales, filas afectadas sumadas y cancelación propagada al lote completo |
| Spooling (Trino 466+) | `Spooling=true` solicita segmentos JSON sin comprimir, los descarga del almacenamiento de objetos y los confirma |

</details>

<details>
<summary><b>Integración y diagnóstico</b> (6 elementos)</summary>

| Función | Valor |
|---------|-------|
| `GetSchema` | `MetaDataCollections`, `Tables`, `Columns`, `DataTypes` |
| Integración con XCode | Cambio al controlador predeterminado con respaldo al anterior; sin promesas de transacción, DDL o autoincremento |
| Localización | 33 idiomas de ejecución, generados en tiempo de compilación y compatibles con AOT |
| Diagnóstico | ID de consulta, código de error, SQLState y duración conservados en excepciones y registros; API de diagnóstico de consultas y nodos de solo lectura |
| OpenTelemetry | `ActivitySource("NewLife.Presto")` en frameworks modernos |
| Empaquetado | Nombre fuerte, SourceLink, paquete de símbolos y README.nuget.md |

</details>

## Compatibilidad de plataformas

| Área | Compatible |
|------|------------|
| Sistemas operativos | Windows, Linux (glibc y musl), macOS |
| Arquitecturas | x64, arm64; LoongArch64 y riscv64 mediante compilaciones de observación |
| Frameworks de destino | net45, net461, netstandard2.0, netstandard2.1, net6.0, net8.0, net10.0, net11.0 |
| NativeAOT | net8.0+ sin advertencias del analizador de recorte/AOT; prueba bloqueante en win-x64, linux-x64, linux-arm64 |
| Matriz CI | Windows, Linux x64, Linux arm64, contenedores musl, macOS y Windows arm64 (observación) |

## Localización

| Ámbito | Cobertura |
|--------|-----------|
| Mensajes de ejecución | 33 idiomas incluido inglés; las traducciones ausentes vuelven al inglés |
| README | 11 idiomas generados desde `readme-languages.json` |
| Metadatos | El título y la descripción del ensamblado son texto origen en inglés y se traducen al mostrarse |

> Las claves de traducción son el texto origen en inglés. Tras añadir un mensaje en el código, añada la misma clave en cada archivo de idioma; `tools/check-locales.ps1 -RequireTranslated` valida claves, marcadores y completitud en CI.

## Documentación

- [需求文档](Doc/需求文档.md) — requisitos y alcance
- [功能清单](Doc/功能清单.md) — estado de implementación/pruebas/comentarios
- [架构设计](Doc/架构设计.md) — componentes, flujo del protocolo, asignación de tipos, decisiones
- [竞品分析](Doc/竞品分析.md) — proyectos de referencia, matriz de funciones, brechas
- [性能测试](Doc/性能测试.md) — benchmark de la misma ronda frente a competidores ADO y una línea base HTTP

## Contribuir

- Compilar: `dotnet build NewLife.Presto.slnx -c Release`
- Probar: `dotnet test UnitTest/UnitTest.csproj -c Release`
- Localización: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- Siga las convenciones C# de NewLife y añada cada mensaje de ejecución a las tablas de idioma.
- No modifique `ChangeLog.md` ni las notas de versión del paquete fuera del proceso de publicación.

## Mapa del ecosistema

NewLife es un ecosistema por capas de proyectos de código abierto con versionado independiente. El mapa siguiente se comparte literalmente en todos los repositorios de NewLife; cada proyecto enlaza a su repositorio y a su paquete principal de NuGet.

| Proyecto | Qué hace | Descargas |
| --- | --- | --- |
| **Fundación** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Registro de logs, configuración, caché, red, serialización, programación de tareas y seguimiento APM. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Middleware de datos: tablas únicas a escala de decenas de miles de millones, particionado automático, separación lectura/escritura y más de 10 bases de datos. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | Biblioteca de red de alto rendimiento: 1,77 M pps / 14,5 Gbps en una máquina (TCP 1KB, 2026-10), 4 M conexiones TCP persistentes. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Framework de comunicación por protocolo y RPC para aplicaciones cliente/servidor y dispositivos IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Instala aplicaciones .NET como demonios del sistema operativo: servicios de Windows y systemd de Linux. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | Plantillas de proyecto `dotnet new` para consola, web, API, servicio, datos e IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Datos y mensajería** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Cliente Redis con latencia de microsegundos, millones de operaciones por segundo y colas de mensajes integradas. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Cliente RocketMQ totalmente administrado para Apache RocketMQ y Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | Cliente y servidor MQTT, incluida la conectividad con Alibaba Cloud IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Base de datos híbrida en C#: embebida o en red, relacional, series temporales, cola de mensajes y KV en una sola. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | Controlador y capa de ayuda de MySql construidos sobre la pila de datos de NewLife. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, IA y distribuido** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Framework rápido para plataformas de administración: permisos, SSO, servidor OAuth y andamiaje CRUD. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Plataforma distribuida de servicios: gestión de nodos, APM, centro de configuración, registro y centro de despliegues. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Plataforma de tareas distribuidas y programación de datos en tiempo real con particionado, reintentos y relleno histórico. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Cliente unificado para 46 proveedores de LLM más ChatAI, una aplicación web de chat lista para usar. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Industrial e IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | Biblioteca estándar de IoT que define contratos de protocolo y abstracciones de dispositivos. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Implementación de Modbus TCP/RTU/ASCII sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Protocolo para PLC Siemens sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Protocolo para PLC Omron sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Protocolo para PLC Mitsubishi Melsec sobre NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | Cliente OPC para el acceso a datos industriales. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | Pila de protocolo BACnet para la automatización de edificios. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Plataforma IoT de código abierto y ligera con gestión de pasarelas y dispositivos. | — |
| **Telemática vehicular** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | Protocolo de posicionamiento vehicular JT/T 808: más de 70 cuerpos de mensaje, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | Protocolo plataforma a plataforma JT/T 809 para autoridades de transporte. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | Vídeo a bordo JT/T 1078: análisis RTP, remultiplexado FLV/fMP4 y transmisión en directo. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Componentes de dominio** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Un solo paquete para Excel, Word, PPT, PDF y más de 15 formatos de documento. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Audio multiplataforma: códecs, E/S de dispositivos, DSP, contenedores y streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Códecs de vídeo, formatos de contenedor, procesamiento de fotogramas y streaming. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Interfaz unificada sobre Baidu, AMap, Tencent y Tianditu con geocodificación y conversión de coordenadas. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | Geolocalización IPv4 con una base de datos offline mapeada en memoria y una API en línea de respaldo. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Herramientas y productos** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Caja de herramientas del desarrollador: red, puerto serie, criptografía, expresiones regulares, Modbus y MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Sistema de gestión de código fuente Git autoalojado. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Instala automáticamente el runtime de .NET correspondiente cuando se ejecuta desde un host más antiguo. | — |

> Las insignias muestran el paquete principal de cada proyecto; los paquetes de temas, extensiones y satélites figuran en sus páginas de NuGet. Los más de 30 paquetes no listados aquí (FTP, LoRa, extensiones de Office, controladores de protocolo, etc.) están disponibles en la [organización NewLifeX](https://github.com/NewLifeX) y en [newlifex.com](https://newlifex.com).

## Equipo

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

NewLife se fundó en 2002 y lleva más de dos décadas dedicado al ecosistema .NET e IoT, mantiene cerca de 100 proyectos de código abierto (34 de ellos principales en este mapa) y ofrece soluciones integradas de software y hardware para los sectores de energía, logística, salud, control industrial e Internet. Nos guía el lema «en el aprendizaje no hay antigüedad: quien domina es maestro».

Los componentes de código abierto acumulan <!--nuget-total:start-->6.48M<!--nuget-total:end--> descargas en NuGet (ecosistema completo, a fecha de 2026-10).

Sitio web: https://newlifex.com · Organización de código abierto: https://github.com/NewLifeX

<details>
<summary>Cuenta oficial de WeChat: 智能大石头 (escanea para seguir)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## Licencia

[MIT](LICENSE)
