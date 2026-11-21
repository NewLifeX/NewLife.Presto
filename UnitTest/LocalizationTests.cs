using System.Globalization;
using System.Reflection;
using NewLife;
using NewLife.Presto;

namespace UnitTest;

/// <summary>运行时多语言测试。覆盖 33 种语言、占位符与英文兜底</summary>
[Collection("Localization")]
public class LocalizationTests
{
    /// <summary>支持的语言清单。英文为源文与兜底，其余为译文</summary>
    public static readonly String[] Languages =
    [
        "ar", "bn", "cs", "da", "de", "el", "en", "es", "fa", "fi", "fr", "he", "hi", "hu", "id", "it",
        "ja", "ko", "ms", "nb", "nl", "pl", "pt", "ro", "ru", "sr", "sv", "th", "tr", "uk", "vi", "zh-Hans", "zh-Hant",
    ];

    /// <summary>驱动消息清单。键为英文源文</summary>
    private static readonly String[] Messages =
    [
        "AccessToken is required when Auth=JWT.",
        "Command timeout cannot be negative.",
        "Command type {0} is not supported; use CommandType.Text.",
        "Connection timeout must be greater than zero.",
        "Custom header '{0}' is managed by the driver and cannot be overridden.",
        "Host cannot be empty.",
        "Invalid custom header format: {0}. Expected name:value.",
        "Invalid session property format: {0}. Expected key=value.",
        "Max retries cannot be negative.",
        "Multiple statements are not supported.",
        "NewLife Presto Driver",
        "Port must be between 1 and 65535.",
        "Pure C# Presto/Trino ADO.NET driver with zero third-party dependencies, HTTP REST v1 protocol, full async/await and XCode ORM integration. MIT license. Supports net45~net11.",
        "Query failed: {0}",
        "Retry interval cannot be negative.",
        "Statement submission failed with HTTP {0}.",
        "The driver does not support auto-increment columns; use a business key (Guid/snowflake) or set AllowInsertIdentity.",
        "The driver does not support transactions.",
        "Unsupported authentication mode: {0}.",
        "Unsupported protocol: {0}. Use Presto or Trino.",
        "Failed to parse the server response: {0}",
        "The server returned an empty response.",
        "Request to '{0}' failed with HTTP {1}.",
        "Schema collection name cannot be empty.",
        "Schema collection '{0}' is not supported.",
        "Catalog is required for the '{0}' schema collection.",
        "Parameter '{0}' was not found.",
        "Parameter name cannot be empty.",
        "Only input parameters are supported.",
        "Special floating-point values (NaN and Infinity) are not supported as parameters.",
        "The parameter value is outside the supported interval range.",
        "The connection must be a PrestoConnection.",
        "The connection is not open.",
        "CommandText must not be empty.",
        "The connection string cannot be changed while the connection is open.",
        "The connection is already open.",
        "Database name cannot be empty.",
        "Column '{0}' was not found.",
        "The value is NULL.",
        "Reader is closed.",
        "Column index {0} is out of range.",
        "No current row is available.",
        "Cannot convert column '{0}' of type '{1}' to {2}. Value: {3}",
        "Connection timeout of {0} seconds expired.",
        "Command timeout of {0} seconds expired.",
        "Command behavior {0} is not supported.",
        "Parameter must be of type PrestoParameter.",
        "TrustServerCertificate is enabled; server certificate validation is disabled.",
    ];

    static LocalizationTests() => GC.KeepAlive(typeof(PrestoDriver));

    /// <summary>每种语言都有译文</summary>
    [Fact(DisplayName = "每种语言都有译文")]
    public void AllLanguagesTranslate()
    {
        var saved = Localization.Culture;
        try
        {
            foreach (var name in Languages)
            {
                Localization.Culture = CultureInfo.GetCultureInfo(name);

                var message = Localization.Translate("Host cannot be empty.");
                Assert.False(message.IsNullOrEmpty(), name);

                var formatted = Localization.Translate("Port must be between 1 and 65535.");
                Assert.Contains("1", formatted);
                Assert.Contains("65535", formatted);
            }
        }
        finally
        {
            Localization.Culture = saved;
        }
    }

    /// <summary>译文不得与英文原文相同（英文除外）</summary>
    [Fact(DisplayName = "译文不得与英文原文相同")]
    public void TranslationsDifferFromSource()
    {
        var saved = Localization.Culture;
        try
        {
            Localization.Culture = CultureInfo.GetCultureInfo("en");
            var source = Localization.Translate("The driver does not support transactions.");
            foreach (var name in Languages)
            {
                if (name == "en") continue;

                Localization.Culture = CultureInfo.GetCultureInfo(name);
                var message = Localization.Translate("The driver does not support transactions.");
                Assert.NotEqual(source, message);
            }
        }
        finally
        {
            Localization.Culture = saved;
        }
    }

    /// <summary>占位符按参数格式化</summary>
    [Fact(DisplayName = "占位符按参数格式化")]
    public void PlaceholdersAreFormatted()
    {
        var saved = Localization.Culture;
        try
        {
            Localization.Culture = CultureInfo.GetCultureInfo("zh-Hans");
            Assert.Equal("不支持命令类型 7；请使用 CommandType.Text。", Localization.Translate("Command type {0} is not supported; use CommandType.Text.", 7));

            Localization.Culture = CultureInfo.GetCultureInfo("ja");
            Assert.Equal("コマンドの種類 7 はサポートされていません。CommandType.Text を使用してください。", Localization.Translate("Command type {0} is not supported; use CommandType.Text.", 7));
        }
        finally
        {
            Localization.Culture = saved;
        }
    }

    /// <summary>每种语言覆盖全部消息</summary>
    [Fact(DisplayName = "每种语言覆盖全部消息")]
    public void AllMessagesTranslated()
    {
        var saved = Localization.Culture;
        try
        {
            Localization.Culture = CultureInfo.GetCultureInfo("en");
            var english = Messages.Select(e => Localization.Translate(e)).ToArray();

            foreach (var name in Languages)
            {
                Localization.Culture = CultureInfo.GetCultureInfo(name);
                for (var i = 0; i < Messages.Length; i++)
                {
                    var text = Localization.Translate(Messages[i]);
                    Assert.False(text.IsNullOrEmpty(), $"{name}: {Messages[i]}");
                    if (name != "en") Assert.NotEqual(english[i], text);
                }
            }
        }
        finally
        {
            Localization.Culture = saved;
        }
    }

    /// <summary>区域文化回退到对应语言词表</summary>
    [Theory(DisplayName = "区域文化回退到对应语言词表")]
    [InlineData("zh", "主机不能为空。")]
    [InlineData("zh-CN", "主机不能为空。")]
    [InlineData("zh-SG", "主机不能为空。")]
    [InlineData("zh-TW", "主機不能為空。")]
    [InlineData("zh-HK", "主機不能為空。")]
    [InlineData("zh-MO", "主機不能為空。")]
    [InlineData("ja-JP", "ホストを空にすることはできません。")]
    [InlineData("pt-PT", "O host não pode estar vazio.")]
    [InlineData("es-419", "El host no puede estar vacío.")]
    [InlineData("ru-BY", "Имя узла не может быть пустым.")]
    [InlineData("ar-EG", "لا يمكن أن يكون المضيف فارغًا.")]
    [InlineData("de-AT", "Der Host darf nicht leer sein.")]
    [InlineData("fr-CA", "L'hôte ne peut pas être vide.")]
    [InlineData("ko-KR", "호스트는 비워 둘 수 없습니다.")]
    public void RegionalCultureFallsBackToLanguage(String culture, String expected)
    {
        var savedCulture = Localization.Culture;
        var savedUI = CultureInfo.CurrentUICulture;
        try
        {
            Localization.Culture = null;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

            Assert.Equal(expected, Localization.Translate("Host cannot be empty."));
        }
        finally
        {
            CultureInfo.CurrentUICulture = savedUI;
            Localization.Culture = savedCulture;
        }
    }

    /// <summary>未知语言回退英文</summary>
    [Fact(DisplayName = "未知语言回退英文")]
    public void UnknownCultureFallsBackToEnglish()
    {
        var saved = Localization.Culture;
        try
        {
            Localization.Culture = CultureInfo.GetCultureInfo("sw-KE");
            Assert.Equal("Host cannot be empty.", Localization.Translate("Host cannot be empty."));
        }
        finally
        {
            Localization.Culture = saved;
        }
    }

    /// <summary>驱动标题与描述可翻译</summary>
    [Fact(DisplayName = "驱动标题与描述可翻译")]
    public void DriverMetadataIsLocalized()
    {
        var saved = Localization.Culture;
        try
        {
            Localization.Culture = CultureInfo.GetCultureInfo("zh-Hans");
            Assert.Equal("新生命 Presto 驱动", PrestoDriver.Title);
            Assert.Contains("纯 C#", PrestoDriver.Description);

            Localization.Culture = CultureInfo.GetCultureInfo("en");
            Assert.Equal("NewLife Presto Driver", PrestoDriver.Title);
        }
        finally
        {
            Localization.Culture = saved;
        }
    }

    /// <summary>程序集标题与说明使用英文源文</summary>
    [Fact(DisplayName = "程序集标题与说明使用英文源文")]
    public void AssemblyMetadataIsEnglish()
    {
        var asm = typeof(PrestoDriver).Assembly;
        var title = asm.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
        var description = asm.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;

        Assert.Equal("NewLife Presto Driver", title);
        Assert.False(description.IsNullOrEmpty());
        Assert.DoesNotContain("驱动", description);
    }
}
