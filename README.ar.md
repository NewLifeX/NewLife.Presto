# NewLife.Presto — برنامج تشغيل ADO.NET لـ Presto/Trino

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
🌐 [English](README.md) | [简体中文](README.zh-CN.md) | [繁體中文](README.zh-TW.md) | [Español](README.es.md) | [Deutsch](README.de.md) | [日本語](README.ja.md) | [Français](README.fr.md) | [Português](README.pt.md) | [Русский](README.ru.md) | [한국어](README.ko.md)
<!-- lang:end -->

**NewLife.Presto** هو برنامج تشغيل ADO.NET نقي بلغة C# لـ PrestoDB وTrino من فريق NewLife. يستخدم بروتوكول عميل HTTP REST v1 الرسمي، ويعتمد على `NewLife.Core` فقط مع **صفر تبعيات خارجية**، ويضم **33 لغة وقت تشغيل**، ويدعم `net45` حتى `net11`، و**جاهز لـ NativeAOT** على `net8.0+`. يشترك PrestoDB وTrino في حزمة واحدة؛ ويبدّل `Protocol=Trino` (أو `TrinoHeaders=true`) رؤوس الطلبات.

> **حالة المشروع**: مرحلة التصميم والهيكل. المكونات غير المتصلة بالشبكة (سلسلة الاتصال، رؤوس البروتوكول، تعيين الأنواع، الترجمة، اختبار AOT) منفذة ومختبرة؛ ويُتابع تنفيذ الشبكة والاستعلامات في [قائمة الوظائف](Doc/功能清单.md).

**المحتويات**: [البدء السريع](#البدء-السريع) · [لماذا NewLife.Presto](#لماذا-newlifepresto) · [الميزات الرئيسية](#الميزات-الرئيسية) · [دعم المنصات](#دعم-المنصات) · [الترجمة](#الترجمة) · [التوثيق](#التوثيق) · [المساهمة](#المساهمة) · [خريطة المنظومة](#خريطة-المنظومة) · [الفريق](#الفريق) · [الترخيص](#الترخيص)

---

## البدء السريع

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

## لماذا NewLife.Presto

- **حزمة واحدة لـ PrestoDB وTrino**: التنفيذ نفسه للبروتوكول، مع اختيار رؤوس `X-Presto-*` / `X-Trino-*` عبر الإعدادات.
- **XCode أولاً**: المصنع وأسماء سلسلة الاتصال المستعارة ومعلمات `@` والبيانات الوصفية وحدود الإمكانات مصممة وفق نموذج Presto في XCode.
- **صفر تبعيات خارجية**: يعتمد على `NewLife.Core` فقط؛ تأتي JSON وHTTP والتسجيل من BCL أو من حزمة NewLife.
- **جميع الأطر والمنصات**: `net45` ~ `net11`، وWindows / Linux / macOS، وx64 / arm64، مع LoongArch64 وmusl في عمليات بناء للمراقبة.
- **33 لغة وقت تشغيل**: الاستثناءات والسجلات وبيانات تعريف برنامج التشغيل مترجمة، والإنجليزية هي المصدر والاحتياطي.
- **جاهز لـ NativeAOT**: لا انعكاس وقت تشغيل في بروتوكولات أو مرمزات الأنواع؛ ويجري اختبار AOT على كل منصة مدعومة.

## الميزات الرئيسية

<details>
<summary><b>الاتصال والبروتوكول</b> (6 عناصر)</summary>

| الميزة | القيمة |
|--------|--------|
| أسماء سلسلة الاتصال المستعارة | `Host`/`Server`/`Data Source` و`Catalog`/`Database` و`Schema`/`Owner` وصياغة XCode |
| رؤوس PrestoDB / Trino | `Protocol=Presto` افتراضيًا؛ ولـ Trino 351+ استخدم `Protocol=Trino` أو `TrinoHeaders=true` |
| سياق الجلسة | Catalog والمخطط والمستخدم والمصدر والمنطقة الزمنية وخصائص الجلسة والرؤوس المخصصة |
| المصادقة | Basic وJWT/Bearer ورؤوس بوابة مخصصة، مع إبقاء الأسرار خارج السجلات |
| TLS والوكلاء | تحقق صارم من الشهادات افتراضيًا؛ وتخفيف صريح لشهادات CA الخاصة |
| نقل HTTP مشترك | إعادة استخدام `HttpClient` لكل نقطة نهاية، مع الوكيل والضغط والمهلات |

</details>

<details>
<summary><b>الاستعلامات والأنواع</b> (6 عناصر)</summary>

| الميزة | القيمة |
|--------|--------|
| دورة حياة العبارة | `POST /v1/statement` واستطلاع `nextUri` واكتشاف الاكتمال |
| حالات متوافقة مستقبلاً | الحالات سلاسل نصية، والحالات الجديدة من الخادم لا تكسر التحليل |
| إعادة المحاولة العابرة | 429/502/503/504 مع `Retry-After` أو تراجع أسي |
| معلمات `@name` | استبدال في العميل يتجاوز السلاسل والمعرفات والتعليقات |
| دقة الأنواع | Boolean والأعداد الصحيحة وdecimal والتاريخ/الوقت وtimestamp with time zone وUUID وIP والثنائي |
| الأنواع المركبة | تُرجع `array`/`map`/`row`/`json` كنصوص خام في الإصدار الأول |

</details>

<details>
<summary><b>التكامل والتشخيص</b> (6 عناصر)</summary>

| الميزة | القيمة |
|--------|--------|
| `GetSchema` | `MetaDataCollections` و`Tables` و`Columns` و`DataTypes` |
| تكامل XCode | تبديل برنامج التشغيل الافتراضي مع الرجوع إلى السابق؛ دون ادعاء دعم المعاملات أو DDL أو الزيادة التلقائية |
| الترجمة | 33 لغة وقت تشغيل تُنشأ وقت الترجمة وتعمل مع AOT |
| التشخيص | معرّف الاستعلام ورمز الخطأ وSQLState والمدة محفوظة في الاستثناءات والسجلات |
| OpenTelemetry | `ActivitySource("NewLife.Presto")` في الأطر الحديثة |
| التغليف | اسم قوي وSourceLink وحزمة رموز وREADME.nuget.md |

</details>

## دعم المنصات

| المجال | المدعوم |
|--------|---------|
| أنظمة التشغيل | Windows وLinux (glibc وmusl) وmacOS |
| المعماريات | x64 وarm64؛ وLoongArch64 وriscv64 عبر عمليات بناء للمراقبة |
| أطر الهدف | net45 وnet461 وnetstandard2.0 وnetstandard2.1 وnet6.0 وnet8.0 وnet10.0 وnet11.0 |
| NativeAOT | net8.0+ دون تحذيرات محلل trim/AOT؛ اختبار مانع على win-x64 وlinux-x64 وlinux-arm64 |
| مصفوفة CI | Windows وLinux x64 وLinux arm64 وحاويات musl وmacOS وWindows arm64 (مراقبة) |

## الترجمة

| النطاق | التغطية |
|--------|---------|
| رسائل وقت التشغيل | 33 لغة بما فيها الإنجليزية؛ الترجمات المفقودة تعود إلى الإنجليزية |
| README | 11 لغة تُنشأ من `readme-languages.json` |
| البيانات الوصفية | عنوان التجميع ووصفه نصان مصدرهما الإنجليزية ويُترجمان عند العرض |

> مفاتيح الترجمة هي النص المصدر بالإنجليزية. بعد إضافة رسالة في الشيفرة، أضف المفتاح نفسه في كل ملف لغة؛ ويتحقق `tools/check-locales.ps1 -RequireTranslated` من المفاتيح والعناصر النائبة والاكتمال في CI.

## التوثيق

- [需求文档](Doc/需求文档.md) — المتطلبات والنطاق
- [功能清单](Doc/功能清单.md) — حالة التنفيذ/الاختبار/التعليقات
- [架构设计](Doc/架构设计.md) — المكونات وتدفق البروتوكول وتعيين الأنواع والقرارات
- [竞品分析](Doc/竞品分析.md) — المشاريع المرجعية ومصفوفة الميزات والفجوات

## المساهمة

- البناء: `dotnet build NewLife.Presto.slnx -c Release`
- الاختبار: `dotnet test UnitTest/UnitTest.csproj -c Release`
- الترجمة: `pwsh -File tools/check-locales.ps1 -RequireTranslated`
- اتبع اصطلاحات C# في NewLife وأضف كل رسالة وقت تشغيل إلى جداول اللغات.
- لا تعدّل `ChangeLog.md` أو ملاحظات إصدار الحزمة خارج عملية الإصدار.

## خريطة المنظومة

NewLife منظومة متعدّدة الطبقات من مشاريع مفتوحة المصدر تُصدَر بإصدارات مستقلة. الخريطة أدناه مشتركة حرفيًا بين جميع مستودعات NewLife؛ وكل مشروع يرتبط بمستودعه وبحزمته الرئيسية على NuGet.

| المشروع | الوظيفة | التنزيلات |
| --- | --- | --- |
| **الأساس** | | |
| [NewLife.Core](https://github.com/NewLifeX/X) | التسجيل والتهيئة والتخزين المؤقت والشبكات والتسلسل والجدولة وتتبّع أداء APM. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Core?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Core) |
| [NewLife.XCode](https://github.com/NewLifeX/NewLife.XCode) | وسيط بيانات: جداول واحدة بمستوى عشرات المليارات من الصفوف، وتقسيم تلقائي للجداول، وفصل القراءة عن الكتابة، وأكثر من 10 قواعد بيانات. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCode?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCode) |
| [NewLife.Net](https://github.com/NewLifeX/NewLife.Net) | مكتبة شبكات عالية الأداء: 1.77 مليون pps / 14.5 Gbps على جهاز واحد (TCP 1KB، 2026-10)، و4 ملايين اتصال TCP طويل الأمد. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Net?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Net) |
| [NewLife.Remoting](https://github.com/NewLifeX/NewLife.Remoting) | إطار اتصال بروتوكولي وRPC لتطبيقات العميل/الخادم وأجهزة إنترنت الأشياء. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Remoting?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Remoting) |
| [NewLife.Agent](https://github.com/NewLifeX/NewLife.Agent) | يثبّت تطبيقات .NET كخدمات حارسة لنظام التشغيل: خدمات Windows وsystemd في Linux. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Agent?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Agent) |
| [NewLife.Templates](https://github.com/NewLifeX/NewLife.Zero) | قوالب مشاريع `dotnet new` للطرفية وweb وAPI والخدمات والبيانات وإنترنت الأشياء. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Templates?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Templates) |
| **البيانات والمراسلة** | | |
| [NewLife.Redis](https://github.com/NewLifeX/NewLife.Redis) | عميل Redis بزمن استجابة بالميكروثانية، وملايين العمليات في الثانية، وقوائم رسائل مدمجة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Redis?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Redis) |
| [NewLife.RocketMQ](https://github.com/NewLifeX/NewLife.RocketMQ) | عميل RocketMQ مُدار بالكامل لـ Apache RocketMQ وخدمة رسائل Alibaba Cloud. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.RocketMQ?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.RocketMQ) |
| [NewLife.MQTT](https://github.com/NewLifeX/NewLife.MQTT) | عميل وخادم MQTT، بما في ذلك الاتصال بـ Alibaba Cloud IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MQTT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MQTT) |
| [NewLife.NovaDb](https://github.com/NewLifeX/NewLife.NovaDb) | قاعدة بيانات هجينة بلغة C#: مدمجة أو شبكية، وعلائقية، وسلاسل زمنية، وقائمة رسائل، وKV في واحد. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.NovaDb?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.NovaDb) |
| [NewLife.MySql](https://github.com/NewLifeX/NewLife.MySql) | برنامج تشغيل MySql وطبقة مساعدة مبنية على مكدّس بيانات NewLife. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.MySql?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.MySql) |
| **Web والذكاء الاصطناعي والأنظمة الموزّعة** | | |
| [NewLife.Cube](https://github.com/NewLifeX/NewLife.Cube) | إطار منصة إدارة سريعة: الصلاحيات، والدخول الموحّد SSO، وخادم OAuth، وتوليد واجهات CRUD. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Cube?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Cube) |
| [Stardust](https://github.com/NewLifeX/Stardust) | منصة خدمات موزّعة: إدارة العقد، وAPM، ومركز الإعدادات، ومركز التسجيل، ومركز النشر. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Stardust?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Stardust) |
| [AntJob](https://github.com/NewLifeX/AntJob) | منصة موزّعة للمهام وجدولة البيانات الفورية، مع التقسيم وإعادة المحاولة وإعادة التعبئة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AntJob?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AntJob) |
| [NewLife.AI](https://github.com/NewLifeX/NewLife.AI) | عميل موحّد لـ 46 مزوّد LLM بالإضافة إلى ChatAI، تطبيق ويب جاهز للمحادثة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.AI?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.AI) |
| **الصناعة وإنترنت الأشياء** | | |
| [NewLife.IoT](https://github.com/NewLifeX/NewLife.IoT) | مكتبة معايير إنترنت الأشياء التي تعرّف عقود البروتوكولات وتجريدات الأجهزة. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IoT?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IoT) |
| [NewLife.Modbus](https://github.com/NewLifeX/NewLife.Modbus) | تنفيذ Modbus TCP/RTU/ASCII فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Modbus?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Modbus) |
| [NewLife.Siemens](https://github.com/NewLifeX/NewLife.Siemens) | بروتوكول Siemens PLC فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Siemens?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Siemens) |
| [NewLife.Omron](https://github.com/NewLifeX/NewLife.Omron) | بروتوكول Omron PLC فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Omron?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Omron) |
| [NewLife.Melsec](https://github.com/NewLifeX/NewLife.Melsec) | بروتوكول Mitsubishi Melsec PLC فوق NewLife.IoT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Melsec?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Melsec) |
| [NewLife.OPC](https://github.com/NewLifeX/NewLife.OPC) | عميل OPC للوصول إلى البيانات الصناعية. | — |
| [NewLife.BACnet](https://github.com/NewLifeX/NewLife.BACnet) | مكدّس بروتوكول BACnet لأتمتة المباني. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.BACnet?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.BACnet) |
| [ZeroIoT](https://github.com/NewLifeX/ZeroIoT) | منصة إنترنت أشياء مفتوحة المصدر وخفيفة، مع إدارة البوابات والأجهزة. | — |
| **تليماتيكا المركبات** | | |
| [NewLife.JT808](https://github.com/NewLifeX/NewLife.JT808) | بروتوكول تحديد مواقع المركبات JT/T 808: أكثر من 70 جسم رسالة، 2011/2013/2019. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT808?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT808) |
| [NewLife.JT809](https://github.com/NewLifeX/NewLife.JT809) | بروتوكول JT/T 809 بين المنصات لجهات إدارة النقل. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT809?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT809) |
| [NewLife.JT1078](https://github.com/NewLifeX/NewLife.JT1078) | فيديو داخل المركبة JT/T 1078: تحليل RTP وإعادة التجميع إلى FLV/fMP4 والبث المباشر. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.JT1078?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.JT1078) |
| **مكوّنات المجال** | | |
| [NewLife.Office](https://github.com/NewLifeX/NewLife.Office) | حزمة واحدة لـ Excel وWord وPPT وPDF وأكثر من 15 صيغة مستندات. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Office?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Office) |
| [NewLife.Audio](https://github.com/NewLifeX/NewLife.Audio) | صوت عبر المنصات: مرمّزات، وإدخال/إخراج الأجهزة، وDSP، وحاويات، وبث. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Audio?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Audio) |
| [NewLife.Video](https://github.com/NewLifeX/NewLife.Video) | مرمّزات الفيديو وصيغ الحاويات ومعالجة الإطارات والبث. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Video?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Video) |
| [NewLife.Map](https://github.com/NewLifeX/NewLife.Map) | واجهة موحّدة فوق Baidu وAMap وTencent وTianditu مع الترميز الجغرافي وتحويل الإحداثيات. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.Map?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.Map) |
| [NewLife.IP](https://github.com/NewLifeX/NewLife.IP) | تحديد الموقع الجغرافي لعناوين IPv4 بقاعدة بيانات غير متصلة مُعيّنة في الذاكرة، وواجهة API عبر الإنترنت كبديل. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.IP?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.IP) |
| **الأدوات والمنتجات** | | |
| [CrazyCoder](https://github.com/NewLifeX/XCoder) | صندوق أدوات المطوّر: الشبكات والمنفذ التسلسلي والتشفير والتعبيرات النمطية وModbus وMQTT. | [![NuGet](https://img.shields.io/nuget/dt/NewLife.XCoder?style=flat-square&logo=nuget&label=downloads)](https://www.nuget.org/packages/NewLife.XCoder) |
| [GitCandy](https://github.com/NewLifeX/GitCandy) | نظام استضافة ذاتية لإدارة الشيفرة المصدرية Git. | — |
| [LuckyClover](https://github.com/NewLifeX/LuckyClover) | يثبّت بيئة تشغيل .NET المطابقة تلقائيًا عند التشغيل من مضيف أقدم. | — |

> تعرض الشارات الحزمة الرئيسية لكل مشروع؛ أما حزم السمات والإضافات والحزم التابعة فمذكورة في صفحاتها على NuGet. والحزم التي تتجاوز 30 حزمة وغير المذكورة هنا (FTP وLoRa وإضافات Office وبرامج تشغيل البروتوكولات وغيرها) متاحة في [منظمة NewLifeX](https://github.com/NewLifeX) وعلى [newlifex.com](https://newlifex.com).

## الفريق

<img src="https://newlifex.com/logo.png" width="48" align="right" alt="NewLife" />

تأسّست NewLife في عام 2002، وأمضت أكثر من عقدين في منظومة .NET وإنترنت الأشياء، وتدير ما يقارب 100 مشروع مفتوح المصدر (34 منها مشاريع رئيسية في هذه الخريطة)، وتقدّم حلولًا متكاملة للبرمجيات والأجهزة لقطاعات الكهرباء والخدمات اللوجستية والرعاية الصحية والتحكّم الصناعي والإنترنت. ونلتزم بمبدأ "لا أسبقية في التعلّم — الإتقان يكسب الاحترام".

نُزّلت المكوّنات مفتوحة المصدر <!--nuget-total:start-->6.48M<!--nuget-total:end--> مرة من NuGet (على مستوى المنظومة بأكملها، حتى 2026-10).

الموقع: https://newlifex.com · المنظمة مفتوحة المصدر: https://github.com/NewLifeX

<details>
<summary>حساب WeChat الرسمي: 智能大石头 (امسح للاشتراك)</summary>

![智能大石头](https://newlifex.com/stone.jpg)

</details>

## الترخيص

[MIT](LICENSE)
