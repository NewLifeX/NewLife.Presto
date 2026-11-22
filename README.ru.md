# NewLife.Presto — драйвер ADO.NET для Presto/Trino

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [한국어](README.ko.md) | [العربية](README.ar.md)
<!-- lang:end -->

**NewLife.Presto** — это чистый C#-драйвер ADO.NET для PrestoDB и Trino от команды NewLife. Он использует официальный клиентский протокол HTTP REST v1, зависит только от `NewLife.Core` и имеет **ноль сторонних зависимостей**, включает **33 языка выполнения**, поддерживает `net45`–`net11` и **готов к NativeAOT** на `net8.0+`. PrestoDB и Trino используют один пакет; `Protocol=Trino` (или `TrinoHeaders=true`) переключает заголовки запросов.

> **Состояние проекта**: реализовано и протестировано — основной драйвер ADO.NET и провайдеры EF Core/EF6 только для чтения, поставляемые тремя пакетами NuGet одним конвейером публикации. Проходят 561 модульных теста ядра, тесты провайдеров EF и интеграция с реальными Trino/PrestoDB; Spooling поддерживает несжатые JSON-сегменты, иначе автоматически используется прямой протокол. См. [список функций](Doc/功能清单.md).

**Содержание**: [Быстрый старт](#быстрый-старт) · [Почему NewLife.Presto](#почему-newlifepresto) · [Сравнение и бенчмарки](#сравнение-и-бенчмарки) · [Основные возможности](#основные-возможности) · [Поддержка платформ](#поддержка-платформ) · [Локализация](#локализация) · [Документация](#документация) · [Участие](#участие) · [Карта экосистемы](#карта-экосистемы) · [Команда](#команда) · [Лицензия](#лицензия)

---

## Быстрый старт

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

## Почему NewLife.Presto

- **Один пакет для PrestoDB и Trino**: одна реализация протокола, заголовки `X-Presto-*` / `X-Trino-*` выбираются настройкой.
- **XCode в приоритете**: фабрика, псевдонимы строки подключения, параметры `@`, метаданные и границы возможностей спроектированы под модель Presto в XCode.
- **Ноль сторонних зависимостей**: только `NewLife.Core`; JSON, HTTP и журналирование — из BCL или стека NewLife.
- **Все платформы и целевые платформы .NET**: `net45` ~ `net11`, Windows / Linux / macOS, x64 / arm64, а также LoongArch64 и musl в наблюдательных сборках.
- **33 языка выполнения**: исключения, журналы и метаданные драйвера локализованы; английский — источник и резерв.
- **Готов к NativeAOT**: без отражения во время выполнения в протоколе и кодеках типов; AOT-проверка на каждой поддерживаемой платформе.

## Сравнение и бенчмарки

В одном и том же раунде мы сравнили этот драйвер с другими ADO.NET-драйверами Presto/Trino для .NET и с чистым HTTP-протоколом v1: один процесс, одинаковый SQL, медиана трёх прогонов, на Trino 476 и PrestoDB 0.300.

| Аспект | Результат |
|--------|-----------|
| Задержка запросов | Наименьшая или равная в группе ADO во всех сценариях запросов — от подключения с первым запросом до сканирования 15 000 строк и широкой таблицы из 16 столбцов, с наибольшим отрывом на сканировании |
| Управляемые аллокации | Наименьшие во всех сценариях и ниже чистого HTTP-протокола: 9%–52% от самого быстрого конкурента |
| 8 параллельных подключений | На уровне чистого HTTP-протокола или чуть ниже; чуть ниже самого быстрого конкурента на Trino и чуть выше на PrestoDB |
| Условия | Локальный loopback, открытый текст, без аутентификации; порядок стабилен между раундами, абсолютные значения колеблются на 20%–80% |

Это наши собственные измерения в одной среде. Полные таблицы, методика и воспроизведение — в [性能测试](Doc/性能测试.md); версии пакетов и матрица возможностей — в [竞品分析](Doc/竞品分析.md).

**Компромиссы и ограничения**

- Транзакции, DDL, столбцы identity и хранимые процедуры не входят в область драйвера; провайдеры EF Core и EF6 работают только на чтение и явно отклоняют запись и миграции.
- Spooling поддерживает только несжатые JSON-сегменты; остальные кодировки переключаются на прямой протокол.
- Потоковый разбор UTF-8 доступен только с `net6.0+`; `net45`, `net461` и `netstandard2.x` используют путь через словарь с большими аллокациями.
- Пока не в бенчмарке: пропускная способность после публикации AOT, ячейки больше 64 КБ и реальные шлюзы JWT/OAuth2.
- Назначение — ADO.NET и провайдеры EF только для чтения; API запросов в стиле SDK не предоставляется.

## Основные возможности

<details>
<summary><b>Подключение и протокол</b> (6 пунктов)</summary>

| Возможность | Что даёт |
|-------------|----------|
| Псевдонимы строки подключения | `Host`/`Server`/`Data Source`, `Catalog`/`Database`, `Schema`/`Owner` и синтаксис XCode |
| Заголовки PrestoDB / Trino | По умолчанию `Protocol=Presto`; для Trino 351+ — `Protocol=Trino` или `TrinoHeaders=true` |
| Контекст сеанса | Catalog, схема, пользователь, источник, часовой пояс, свойства сеанса и пользовательские заголовки |
| Аутентификация | Basic, JWT/Bearer, учётные данные клиента OAuth2 и пользовательские заголовки шлюза; секреты не попадают в журналы |
| TLS и прокси | Строгая проверка сертификатов по умолчанию; явное ослабление для частных CA |
| Общий HTTP-транспорт | Повторное использование `HttpClient` для каждой конечной точки, прокси, сжатие и тайм-ауты |

</details>

<details>
<summary><b>Запросы и типы</b> (8 пунктов)</summary>

| Возможность | Что даёт |
|-------------|----------|
| Жизненный цикл инструкции | `POST /v1/statement`, опрос `nextUri` и определение завершения |
| Прямая совместимость состояний | Состояния хранятся строками, новые состояния сервера не ломают разбор |
| Повтор при временных ошибках | 429/502/503/504 с `Retry-After` или экспоненциальной задержкой |
| Параметры `@name` | Клиентская подстановка с пропуском строк, идентификаторов и комментариев |
| Точность типов | Boolean, целые, decimal, дата/время, timestamp with time zone, UUID, IP и двоичные |
| Сложные типы | `array`/`map`/`row` по умолчанию возвращаются исходными строками; `ComplexTypes=Structured` возвращает `object[]` и словари |
| `DbBatch` (.NET 6+) | Последовательные пакетные команды, суммарные затронутые строки и отмена, распространяемая на весь пакет |
| Spooling (Trino 466+) | `Spooling=true` запрашивает несжатые JSON-сегменты, скачивает их из объектного хранилища и подтверждает |

</details>

<details>
<summary><b>Интеграция и диагностика</b> (6 пунктов)</summary>

| Возможность | Что даёт |
|-------------|----------|
| `GetSchema` | `MetaDataCollections`, `Tables`, `Columns`, `DataTypes` |
| Интеграция с XCode | Переключение на драйвер по умолчанию с откатом на прежний; без обещаний транзакций, DDL и автоинкремента |
| Локализация | 33 языка выполнения, генерируются при компиляции и работают в AOT |
| Диагностика | Идентификатор запроса, код ошибки, SQLState и длительность сохраняются в исключениях и журналах; диагностический API запросов и узлов только для чтения |
| OpenTelemetry | `ActivitySource("NewLife.Presto")` в современных платформах |
| Упаковка | Строгое имя, SourceLink, пакет символов и README.nuget.md |

</details>

## Поддержка платформ

| Область | Поддержка |
|---------|-----------|
| Операционные системы | Windows, Linux (glibc и musl), macOS |
| Архитектуры | x64, arm64; LoongArch64 и riscv64 — в наблюдательных сборках |
| Целевые платформы | net45, net461, netstandard2.0, netstandard2.1, net6.0, net8.0, net10.0, net11.0 |
| NativeAOT | net8.0+ без предупреждений анализатора trim/AOT; блокирующая проверка на win-x64, linux-x64, linux-arm64 |
| Матрица CI | Windows, Linux x64, Linux arm64, контейнеры musl, macOS и Windows arm64 (наблюдение) |

## Локализация

| Область | Охват |
|---------|-------|
| Сообщения выполнения | 33 языка включая английский; отсутствующие переводы откатываются на английский |
| README | 11 языков, генерируются из `readme-languages.json` |
| Метаданные | Заголовок и описание сборки — английский исходный текст, переводятся при отображении |

> Ключи перевода — это английский исходный текст. После добавления сообщения в код добавьте тот же ключ во все языковые файлы; `tools/check-locales.ps1 -RequireTranslated` проверяет ключи, заполнители и полноту в CI.

## Документация

- [需求文档](Doc/需求文档.md) — требования и область
- [功能清单](Doc/功能清单.md) — статус реализации/тестов/комментариев
- [架构设计](Doc/架构设计.md) — компоненты, поток протокола, сопоставление типов, решения
- [竞品分析](Doc/竞品分析.md) — эталонные проекты, матрица возможностей, пробелы
- [性能测试](Doc/性能测试.md) — бенчмарк одного раунда с ADO-конкурентами и чистым HTTP-протоколом

## Участие

- Сборка: `dotnet build NewLife.Presto.slnx -c Release`
- Тесты: `dotnet test UnitTest/UnitTest.csproj -c Release`
- Локализация: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- Соблюдайте соглашения C# NewLife и добавляйте каждое сообщение выполнения в языковые таблицы.
- Не изменяйте `ChangeLog.md` и примечания к выпуску пакета вне процесса релиза.

## Карта экосистемы

NewLife — это многоуровневая экосистема независимо версионируемых open-source проектов. Карта ниже дословно используется во всех репозиториях NewLife; каждый проект ссылается на свой репозиторий и свой основной пакет NuGet.

| Проект | Что делает | Загрузки |
| --- | --- | --- |
| **Базовый** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | Логирование, конфигурация, кэширование, сеть, сериализация, планирование и APM-трассировка. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | Промежуточное ПО данных: отдельные таблицы масштаба десятков миллиардов строк, автоматическое секционирование, разделение чтения и записи, более 10 баз данных. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | Высокопроизводительная сетевая библиотека: 1,77M pps / 14,5 Гбит/с на машину (TCP 1KB, 2026-10), 4M долгоживущих TCP-соединений. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | Протокольная связь и RPC-фреймворк для клиент-серверных приложений и IoT-устройств. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | Устанавливает .NET-приложения как демоны ОС: службы Windows и systemd в Linux. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | Шаблоны проектов `dotnet new` для консоли, web, API, службы, данных и IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **Данные и обмен сообщениями** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | Клиент Redis с микросекундной задержкой, миллионами операций в секунду и встроенными очередями сообщений. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | Полностью управляемый клиент RocketMQ для Apache RocketMQ и Alibaba Cloud Message Queue. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | Клиент и сервер MQTT, включая подключение к Alibaba Cloud IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | Гибридная база данных на C#: встроенная или сетевая, реляционная, временных рядов, очередь сообщений и KV в одном. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | Драйвер MySql и вспомогательный слой на основе стека данных NewLife. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web, AI и распределённые системы** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | Фреймворк быстрой разработки админ-платформ: права, SSO, сервер OAuth и CRUD-скаффолдинг. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | Распределённая сервисная платформа: управление узлами, APM, центр конфигурации, реестр и центр публикации. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | Распределённая платформа задач и планирования данных в реальном времени с секционированием, повторами и дозаполнением. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | Единый клиент для 46 поставщиков LLM плюс ChatAI — готовое веб-приложение для чата. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **Промышленность и IoT** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | Стандартная библиотека IoT, определяющая контракты протоколов и абстракции устройств. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | Реализация Modbus TCP/RTU/ASCII поверх NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | Протокол Siemens PLC поверх NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | Протокол Omron PLC поверх NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | Протокол Mitsubishi Melsec PLC поверх NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | Клиент OPC для доступа к промышленным данным. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | Стек протокола автоматизации зданий BACnet. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | Лёгкая open-source платформа IoT с управлением шлюзами и устройствами. | — |
| **Телематика транспорта** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | Протокол позиционирования транспорта JT/T 808: более 70 типов сообщений, 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | Межплатформенный протокол JT/T 809 для органов управления транспортом. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | Автомобильное видео JT/T 1078: разбор RTP, ремультиплексирование в FLV/fMP4 и прямая трансляция. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **Прикладные компоненты** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | Один пакет для Excel, Word, PPT, PDF и более 15 форматов документов. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | Кроссплатформенное аудио: кодеки, ввод-вывод устройств, DSP, контейнеры и потоковая передача. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | Видеокодеки, форматы контейнеров, обработка кадров и потоковая передача. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | Единый интерфейс к Baidu, AMap, Tencent и Tianditu с геокодированием и преобразованием координат. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | Геолокация IPv4 с офлайн-базой, отображаемой в память, и онлайн-API как резервом. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **Инструменты и продукты** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | Инструменты разработчика: сеть, последовательный порт, шифрование, регулярные выражения, Modbus и MQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | Самостоятельно размещаемая система управления исходным кодом Git. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | Автоматически устанавливает подходящую среду выполнения .NET при запуске на старом хосте. | — |

> Бейджи показывают основной пакет каждого проекта; пакеты тем, расширений и сателлитов перечислены на соответствующих страницах NuGet. Более 30 пакетов, не перечисленных здесь (FTP, LoRa, расширения Office, драйверы протоколов и другие), доступны в [организации NewLifeX](https://github.com/NewLifeX) и на [newlifex.com](https://newlifex.com).

## Команда

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

Команда NewLife основана в 2002 году и более двух десятилетий работает в экосистеме .NET и IoT, поддерживает около 100 проектов с открытым кодом (34 из них — основные) и предлагает интегрированные программно-аппаратные решения для энергетики, логистики, здравоохранения, промышленной автоматизации и интернет-отрасли. Мы следуем девизу «в учении нет старших — мастерство приносит уважение».

Open-source компоненты скачали <!--nuget-total:start-->6.48M<!--nuget-total:end--> раз с NuGet (по всей экосистеме, по состоянию на 2026-10).

Сайт: https://newlifex.com · Организация open source: https://github.com/NewLifeX

<details>
<summary>Официальный аккаунт WeChat: 智能大石头 (отсканируйте, чтобы подписаться)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## Лицензия

[MIT](LICENSE)
