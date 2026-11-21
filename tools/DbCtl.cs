// NewLife.Presto 本地数据库控制脚本（.NET 10+ 文件式应用）
//
// 用法：
//   dotnet run tools/DbCtl.cs -- status
//   dotnet run tools/DbCtl.cs -- start trino [--port 8080]
//   dotnet run tools/DbCtl.cs -- start presto [--port 8081]
//   dotnet run tools/DbCtl.cs -- stop trino
//   dotnet run tools/DbCtl.cs -- install trino
//   dotnet run tools/DbCtl.cs -- clean [--purge]
//
// 说明：
//   - Trino/PrestoDB 官方发行包带硬性 OS 检查，不能在 Windows 上原生运行；本脚本通过 WSL 运行，
//     安装目录为发行版内的 /opt/dbservers（其虚拟磁盘位于空余空间最多的磁盘）。
//   - 未检测到 WSL 发行版时，自动下载 Ubuntu base rootfs 并导入到空余空间最多的磁盘。
//   - 发行包与 JDK 缺失时自动联网下载（Maven 镜像 + Azul CDN），并解压到 /opt/dbservers。

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;

const String Root = "/opt/dbservers";
const String DefaultDistro = "Ubuntu-24.04";
const String UbuntuBaseUrl = "https://cdimage.ubuntu.com/ubuntu-base/releases/24.04/release/ubuntu-base-24.04.3-base-amd64.tar.gz";
const String TrinoUrl = "https://maven.aliyun.com/repository/public/io/trino/trino-server/476/trino-server-476.tar.gz";
const String PrestoUrl = "https://maven.aliyun.com/repository/public/com/facebook/presto/presto-server/0.300/presto-server-0.300.tar.gz";
const String Jdk25LinuxUrl = "https://cdn.azul.com/zulu/bin/zulu25.38.201-ca-jdk25.0.5.1-linux_x64.tar.gz";
const String Jdk17LinuxUrl = "https://cdn.azul.com/zulu/bin/zulu17.70.201-ca-jdk17.0.21.1-linux_x64.tar.gz";

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant();
var options = ParseOptions(args.Skip(1).ToArray());
var engine = options.Positional.FirstOrDefault()?.ToLowerInvariant();
var distro = options.Get("distro") ?? ResolveDistro();

try
{
    switch (command)
    {
        case "status":
            Status(distro);
            return 0;
        case "start":
            RequireEngine(engine);
            EnsureDistro(distro);
            EnsureKeepAlive(distro);
            EnsureInstalled(distro, engine!);
            StartEngine(distro, engine!, options.Port, options.Get("port") != null);
            return 0;
        case "stop":
            RequireEngine(engine);
            StopEngine(distro, engine!, options.Has("force"));
            StopKeepAliveIfIdle(distro);
            return 0;
        case "install":
            RequireEngine(engine);
            EnsureDistro(distro);
            EnsureInstalled(distro, engine!);
            Console.WriteLine($"{engine} 已安装：{Root}/{EngineDir(engine!)}");
            return 0;
        case "clean":
            EnsureDistro(distro);
            Clean(distro, options.Has("purge"));
            return 0;
        default:
            Console.Error.WriteLine($"未知命令：{command}");
            PrintHelp();
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"失败：{ex.Message}");
    return 1;
}

static void RequireEngine(String? engine)
{
    if (engine is not ("trino" or "presto"))
        throw new ArgumentException("Specify a database: trino or presto.");
}

static String EngineDir(String engine) => engine == "trino" ? "trino-476" : "presto-0.300";

static (Int32 Code, String Output) RunWsl(String distro, String script, Int32 timeoutSeconds = 1800)
{
    var psi = new ProcessStartInfo("wsl.exe")
    {
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };
    psi.ArgumentList.Add("-d");
    psi.ArgumentList.Add(distro);
    psi.ArgumentList.Add("-u");
    psi.ArgumentList.Add("root");
    psi.ArgumentList.Add("--");
    psi.ArgumentList.Add("bash");
    psi.ArgumentList.Add("-s");
    psi.Environment["WSL_UTF8"] = "1";

    using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start wsl.exe");
    // bash 不接受 CRLF，统一转换为 LF 后经 stdin 传入
    process.StandardInput.Write(script.Replace("\r\n", "\n"));
    process.StandardInput.Close();

    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(timeoutSeconds * 1000))
    {
        try { process.Kill(true); } catch { }
        throw new TimeoutException($"WSL 命令超时（{timeoutSeconds}s）");
    }

    var text = stdout.GetAwaiter().GetResult();
    var err = stderr.GetAwaiter().GetResult();
    return (process.ExitCode, String.IsNullOrWhiteSpace(err) ? text : text + Environment.NewLine + err);
}

static String RunHost(String fileName, IEnumerable<String> arguments, Int32 timeoutSeconds = 300)
{
    var psi = new ProcessStartInfo(fileName)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };
    foreach (var item in arguments) psi.ArgumentList.Add(item);
    psi.Environment["WSL_UTF8"] = "1";

    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"无法启动 {fileName}");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(timeoutSeconds * 1000))
    {
        try { process.Kill(true); } catch { }
        throw new TimeoutException($"{fileName} 超时");
    }

    return stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
}

static String[] ListDistros()
{
    var text = RunHost("wsl.exe", ["-l", "-q"]);
    return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
        .Select(e => e.Trim().Trim('\0'))
        .Where(e => e.Length > 0)
        .ToArray();
}

static String ResolveDistro()
{
    var distros = ListDistros();
    if (distros.Contains(DefaultDistro)) return DefaultDistro;
    if (distros.Length > 0) return distros[0];

    return "NewLifeDb";
}

static String BestDriveRoot()
{
    var drive = DriveInfo.GetDrives()
        .Where(e => e.DriveType == DriveType.Fixed && e.IsReady && e.AvailableFreeSpace > 0)
        .OrderByDescending(e => e.AvailableFreeSpace)
        .FirstOrDefault() ?? throw new InvalidOperationException("No available disk found");

    return drive.RootDirectory.FullName.TrimEnd('\\');
}

static void EnsureDistro(String distro)
{
    if (ListDistros().Contains(distro)) return;

    var root = Path.Combine(BestDriveRoot(), "WSL", distro);
    var tar = Path.Combine(root, "rootfs.tar.gz");
    Console.WriteLine($"未检测到 WSL 发行版 {distro}，准备导入到 {root}");

    Directory.CreateDirectory(root);
    if (!File.Exists(tar))
    {
        Console.WriteLine($"下载 Ubuntu base rootfs：{UbuntuBaseUrl}");
        Download(UbuntuBaseUrl, tar);
    }

    Console.WriteLine("Importing WSL distribution (about 1 minute for the first run)...");
    RunHost("wsl.exe", ["--import", distro, root, tar, "--version", "2"], 1800);
    var (code, output) = RunWsl(distro, "apt-get update -qq && apt-get install -y -qq curl ca-certificates >/dev/null && echo ok\n");
    if (code != 0) throw new InvalidOperationException("Distribution initialization failed: " + output);
}

static void Download(String url, String file)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
    response.EnsureSuccessStatusCode();
    using var input = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
    using var output = File.Create(file);
    input.CopyTo(output);
}

// ---------------- 安装 ----------------

static void EnsureInstalled(String distro, String engine)
{
    var script = BuildInstallScript(engine);
    var (code, output) = RunWsl(distro, script);
    if (code != 0) throw new InvalidOperationException($"{engine} 安装失败：{output.Trim()}");
}

static String BuildInstallScript(String engine)
{
    var dir = EngineDir(engine);
    var isTrino = engine == "trino";
    var jdk = isTrino ? "jdk25" : "jdk17";
    var jdkUrl = isTrino ? Jdk25LinuxUrl : Jdk17LinuxUrl;
    var serverUrl = isTrino ? TrinoUrl : PrestoUrl;
    var appClass = isTrino ? "io.trino.server.TrinoServer" : "com.facebook.presto.server.PrestoServer";
    var marker = isTrino ? $"{Root}/{dir}/lib/io.trino_trino-main-476.jar" : $"{Root}/{dir}/lib/presto-main-0.300.jar";

    var builder = new StringBuilder();
    builder.AppendLine("set -euo pipefail");
    builder.AppendLine($"root={Root}");
    builder.AppendLine("mkdir -p \"$root/_cache\" \"$root/logs\" \"$root/data\" \"$root/run\"");
    // JDK：优先用发行版自带的 Java 17（Presto），否则下载 Azul Zulu
    builder.AppendLine($"if [ ! -x \"$root/{jdk}/bin/java\" ] && [ -x /usr/lib/jvm/java-17-openjdk-amd64/bin/java ] && [ \"{jdk}\" = \"jdk17\" ]; then ln -sfn /usr/lib/jvm/java-17-openjdk-amd64 \"$root/jdk17\"; fi");
    builder.AppendLine($"if [ ! -x \"$root/{jdk}/bin/java\" ]; then");
    builder.AppendLine($"  echo '下载 {jdk} ...'");
    builder.AppendLine($"  curl -fSL --retry 3 -o \"$root/_cache/{jdk}.tar.gz\" '{jdkUrl}'");
    builder.AppendLine($"  mkdir -p \"$root/{jdk}\"");
    builder.AppendLine($"  tar -xzf \"$root/_cache/{jdk}.tar.gz\" -C \"$root/{jdk}\" --strip-components=1");
    builder.AppendLine($"  rm -f \"$root/_cache/{jdk}.tar.gz\"");
    builder.AppendLine("fi");
    // 服务端
    builder.AppendLine($"if [ ! -f '{marker}' ]; then");
    builder.AppendLine($"  echo '下载 {engine} ...'");
    builder.AppendLine($"  curl -fSL --retry 3 -o \"$root/_cache/{dir}.tar.gz\" '{serverUrl}'");
    builder.AppendLine($"  mkdir -p \"$root/{dir}\"");
    builder.AppendLine($"  tar -xzf \"$root/_cache/{dir}.tar.gz\" -C \"$root/{dir}\" --strip-components=1");
    builder.AppendLine($"  rm -f \"$root/_cache/{dir}.tar.gz\"");
    builder.AppendLine("fi");
    builder.AppendLine($"mkdir -p \"$root/{dir}/etc/catalog\"");
    builder.AppendLine($"cat > \"$root/{dir}/etc/node.properties\" <<'EOF'");
    builder.AppendLine("node.environment=test");
    builder.AppendLine($"node.id={engine}-dbctl-01");
    builder.AppendLine($"node.data-dir={Root}/data/{engine}");
    builder.AppendLine("EOF");
    builder.AppendLine($"cat > \"$root/{dir}/etc/log.properties\" <<'EOF'");
    builder.AppendLine(isTrino ? "io.trino=INFO" : "com.facebook.presto=INFO");
    builder.AppendLine("EOF");
    builder.AppendLine($"printf 'connector.name=tpch\\n' > \"$root/{dir}/etc/catalog/tpch.properties\"");
    builder.AppendLine($"printf 'connector.name=memory\\n' > \"$root/{dir}/etc/catalog/memory.properties\"");
    builder.AppendLine($"echo 'INSTALLED {appClass}'");
    return builder.ToString();
}

// ---------------- 启动与停止 ----------------

static void StartEngine(String distro, String engine, Int32 port, Boolean portExplicit)
{
    var dir = EngineDir(engine);
    var isTrino = engine == "trino";
    var jdk = isTrino ? "jdk25" : "jdk17";
    var appClass = isTrino ? "io.trino.server.TrinoServer" : "com.facebook.presto.server.PrestoServer";

    if (IsHttpReady(port))
    {
        Console.WriteLine($"{engine} 的端口 {port} 已有服务在运行。");
        PrintInfo(port);
        return;
    }

    if (IsPortOpen(port))
    {
        // 端口已被占用但还未就绪（可能是其它会话正在启动），等待而不是再起一个实例
        Console.WriteLine($"端口 {port} 已被占用，等待现有实例就绪...");
        if (WaitReady(port, 90))
        {
            PrintInfo(port);
            return;
        }

        throw new InvalidOperationException($"端口 {port} 被其它进程占用且未就绪；请先释放该端口或使用 --port 指定其它端口。");
    }

    var config = isTrino
        ? $"""
coordinator=true
node-scheduler.include-coordinator=true
http-server.http.port={port}
http-server.process-forwarded=true
discovery.uri=http://localhost:{port}
query.max-memory=2GB
query.max-memory-per-node=1GB
"""
        : $"""
coordinator=true
node-scheduler.include-coordinator=true
http-server.http.port={port}
discovery-server.enabled=true
discovery.uri=http://localhost:{port}
query.max-memory=2GB
query.max-memory-per-node=1GB
query.max-total-memory=3GB
query.max-total-memory-per-node=1GB
""";

    var jvmArgs = isTrino
        ? $"-server -Xmx3G -XX:+ExitOnOutOfMemoryError -Dfile.encoding=UTF-8 -Dnode.environment=test -Dnode.id={engine}-dbctl-01 -Dnode.data-dir={Root}/data/{engine} -Dlog.levels-file={Root}/{dir}/etc/log.properties -Dlog.output-file={Root}/logs/{engine}-server.log -Dlog.enable-console=false -Dconfig={Root}/{dir}/etc/config.properties"
        : $"-server -Xmx2G -XX:+ExitOnOutOfMemoryError -Dfile.encoding=UTF-8 -Djdk.attach.allowAttachSelf=true --add-opens=java.base/java.nio=ALL-UNNAMED --add-opens=java.base/java.lang=ALL-UNNAMED --add-opens=java.base/java.util=ALL-UNNAMED --add-opens=java.base/sun.nio.ch=ALL-UNNAMED -Dnode.environment=test -Dnode.id={engine}-dbctl-01 -Dnode.data-dir={Root}/data/{engine} -Dlog.levels-file={Root}/{dir}/etc/log.properties -Dlog.path={Root}/logs/{engine}-server.log -Dlog.enable-console=false -Dconfig={Root}/{dir}/etc/config.properties";

    var script = $"""
set -euo pipefail
root={Root}
dir={dir}
pidf="$root/run/{engine}.pid"
if [ -f "$pidf" ] && kill -0 "$(cat "$pidf")" 2>/dev/null; then echo "ALREADY $(cat "$pidf")"; exit 0; fi
cat > "$root/$dir/etc/config.properties" <<'EOF'
{config}
EOF
java="$root/{jdk}/bin/java"
if [ ! -x "$java" ]; then echo "MISSING_JAVA $java"; exit 3; fi
cd "$root/$dir"
nohup "$java" {jvmArgs} -cp "$root/$dir/lib/*" {appClass} >> "$root/logs/{engine}-console.log" 2>&1 &
echo $! > "$pidf"
sleep 1
if kill -0 "$(cat "$pidf")" 2>/dev/null; then echo "STARTED $(cat "$pidf")"; else echo "START_FAILED"; tail -n 40 "$root/logs/{engine}-console.log" || true; exit 4; fi
""";

    var (code, output) = RunWsl(distro, script, 120);
    if (code != 0) throw new InvalidOperationException($"启动 {engine} 失败：{output.Trim()}");

    Console.WriteLine($"正在等待 {engine} 就绪（端口 {port}）...");
    if (!WaitReady(port, 240))
    {
        var (_, log) = RunWsl(distro, $"tail -n 60 {Root}/logs/{engine}-console.log || true\n", 60);
        throw new InvalidOperationException($"{engine} 未在 240 秒内就绪：{log.Trim()}");
    }

    PrintInfo(port);
    Console.WriteLine();
    Console.WriteLine("Connection string example:");
    if (isTrino)
        Console.WriteLine($"Host=localhost;Port={port};Catalog=tpch;Schema=tiny;User=test;Protocol=Trino");
    else
        Console.WriteLine($"Host=localhost;Port={port};Catalog=tpch;Schema=tiny;User=test");
}

static void StopEngine(String distro, String engine, Boolean force)
{
    var appClass = engine == "trino" ? "io.trino.server.TrinoServer" : "com.facebook.presto.server.PrestoServer";
    var script = $"""
root={Root}
pidf="$root/run/{engine}.pid"
if [ -f "$pidf" ]; then
  pid=$(cat "$pidf")
  kill "$pid" 2>/dev/null || true
  for i in $(seq 1 40); do kill -0 "$pid" 2>/dev/null || break; sleep 1; done
  kill -9 "$pid" 2>/dev/null || true
  rm -f "$pidf"
  echo "STOPPED $pid"
  exit 0
fi
if [ "{BoolText(force)}" = "1" ]; then
  pkill -f '{appClass}' 2>/dev/null || true
  sleep 2
  echo "FORCED"
else
  echo "NOT_OURS（端口上的实例不是本工具启动的；如需强制结束请加 --force）"
fi
""";
    var (code, output) = RunWsl(distro, script, 120);
    if (code != 0) throw new InvalidOperationException($"停止 {engine} 失败：{output.Trim()}");
    Console.WriteLine($"{engine}：{output.Trim()}");
}

static String BoolText(Boolean value) => value ? "1" : "0";

static Boolean IsHttpReady(Int32 port)
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var text = client.GetStringAsync($"http://127.0.0.1:{port}/v1/info").GetAwaiter().GetResult();
        return text.Contains("\"starting\":false", StringComparison.Ordinal);
    }
    catch
    {
        return false;
    }
}

static Boolean IsPortOpen(Int32 port)
{
    try
    {
        using var client = new System.Net.Sockets.TcpClient();
        return client.ConnectAsync("127.0.0.1", port).Wait(1500) && client.Connected;
    }
    catch
    {
        return false;
    }
}

static Boolean WaitReady(Int32 port, Int32 timeoutSeconds)
{
    var end = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (DateTime.UtcNow < end)
    {
        if (IsHttpReady(port)) return true;
        Thread.Sleep(2000);
    }

    return false;
}

static void PrintInfo(Int32 port)
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        var text = client.GetStringAsync($"http://127.0.0.1:{port}/v1/info").GetAwaiter().GetResult();
        Console.WriteLine($"已就绪：http://localhost:{port}  {text}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"查询 /v1/info 失败：{ex.Message}");
    }
}

// ---------------- 状态与清理 ----------------

static void Status(String distro)
{
    Console.WriteLine($"WSL 发行版：{distro}");
    Console.WriteLine($"安装目录：{Root}（位于发行版虚拟磁盘，当前磁盘剩余 {FreeText()}）");

    var script = $"""
root={Root}
for e in trino presto; do
  pidf="$root/run/$e.pid"
  if [ -f "$pidf" ] && kill -0 "$(cat "$pidf")" 2>/dev/null; then echo "$e RUNNING $(cat "$pidf")"; else echo "$e STOPPED"; fi
done
echo "procs trino=$(pgrep -fc 'io.trino.server.TrinoServer' 2>/dev/null || true) presto=$(pgrep -fc 'com.facebook.presto.server.PrestoServer' 2>/dev/null || true)"
for d in trino-476 presto-0.300 jdk25 jdk17; do
  if [ -d "$root/$d" ]; then echo "dir $d $(du -sh "$root/$d" 2>/dev/null | cut -f1)"; else echo "dir $d -"; fi
done
""";
    var (code, output) = RunWsl(distro, script, 120);
    if (code != 0) throw new InvalidOperationException("Failed to query status: " + output);
    Console.WriteLine(output.Trim());

    var own = output.Split('\n')
        .Select(e => e.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .Where(e => e.Length >= 2 && e[0] is "trino" or "presto")
        .ToDictionary(e => e[0], e => e[1] == "RUNNING");

    foreach (var (engine, port) in new[] { ("trino", 8080), ("presto", 8081) })
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var text = client.GetStringAsync($"http://127.0.0.1:{port}/v1/info").GetAwaiter().GetResult();
            var note = own.TryGetValue(engine, out var isOwn) && !isOwn ? "（外部实例）" : String.Empty;
            Console.WriteLine($"http {engine} :{port} => 已就绪{note}  {text}");
        }
        catch
        {
            Console.WriteLine($"http {engine} :{port} => 未监听");
        }
    }
}
static void Clean(String distro, Boolean purge)
{
    var script = purge
        ? $"rm -rf {Root}/_cache {Root}/logs/* {Root}/trino-476 {Root}/presto-0.300 && echo PURGED\n"
        : $"rm -rf {Root}/_cache {Root}/logs/* && echo CLEANED\n";
    var (code, output) = RunWsl(distro, script, 120);
    if (code != 0) throw new InvalidOperationException("Cleanup failed: " + output);
    Console.WriteLine(output.Trim());
}

static String FreeText()
{
    var drive = DriveInfo.GetDrives()
        .Where(e => e.DriveType == DriveType.Fixed && e.IsReady)
        .OrderByDescending(e => e.AvailableFreeSpace)
        .FirstOrDefault();

    return drive == null ? "未知" : $"{drive.Name} {drive.AvailableFreeSpace / 1024d / 1024 / 1024:N0} GB";
}

// ---------------- 保活：WSL 空闲会自动停机，保持一个会话让数据库常驻 ----------------

static String KeepAliveFile(String distro) =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewLife.DbCtl", $"keepalive-{distro}.pid");

static void EnsureKeepAlive(String distro)
{
    var file = KeepAliveFile(distro);
    if (File.Exists(file) && Int32.TryParse(File.ReadAllText(file).Trim(), out var pid))
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited && process.ProcessName.Equals("wsl", StringComparison.OrdinalIgnoreCase)) return;
        }
        catch { }
    }

    var psi = new ProcessStartInfo("wsl.exe")
    {
        UseShellExecute = true,
        WindowStyle = ProcessWindowStyle.Hidden,
    };
    foreach (var item in new[] { "-d", distro, "-u", "root", "--", "bash", "-c", "exec sleep 2147483647" }) psi.ArgumentList.Add(item);

    var process2 = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start WSL keep-alive session");
    Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    File.WriteAllText(file, process2.Id.ToString(CultureInfo.InvariantCulture));
    Console.WriteLine($"已启动 WSL 保活会话（PID {process2.Id}）");
}

static void StopKeepAliveIfIdle(String distro)
{
    // 只有 Trino/Presto 全部退出后才释放保活，避免影响其它会话正在使用的实例
    var (_, output) = RunWsl(distro, """
count=$(pgrep -fc 'io.trino.server.TrinoServer|com.facebook.presto.server.PrestoServer' 2>/dev/null || true)
if [ -z "$count" ]; then count=0; fi
echo $count
""", 60);
    if (output.Trim() != "0") return;

    var file = KeepAliveFile(distro);
    if (!File.Exists(file)) return;

    if (Int32.TryParse(File.ReadAllText(file).Trim(), out var pid))
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited && process.ProcessName.Equals("wsl", StringComparison.OrdinalIgnoreCase)) process.Kill(true);
        }
        catch { }
    }

    File.Delete(file);
    Console.WriteLine("WSL keep-alive session released");
}

// ---------------- 参数与帮助 ----------------

static Options ParseOptions(String[] arguments)
{
    var options = new Options();
    for (var i = 0; i < arguments.Length; i++)
    {
        var item = arguments[i];
        if (item.StartsWith("--", StringComparison.Ordinal))
        {
            var name = item[2..];
            if (name is "purge" or "keepalive" or "force")
            {
                options.Flags.Add(name);
                continue;
            }

            var value = i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal) ? arguments[++i] : String.Empty;
            options.Values[name] = value;
        }
        else
        {
            options.Positional.Add(item);
        }
    }

    return options;
}

static void PrintHelp()
{
    Console.WriteLine("""
NewLife.Presto 本地数据库控制（WSL 后端）

  dotnet run tools/DbCtl.cs -- status
  dotnet run tools/DbCtl.cs -- start trino [--port 8080] [--distro Ubuntu-24.04]
  dotnet run tools/DbCtl.cs -- start presto [--port 8081]
  dotnet run tools/DbCtl.cs -- stop trino|presto
  dotnet run tools/DbCtl.cs -- install trino|presto
  dotnet run tools/DbCtl.cs -- clean [--purge]

说明：
  - Trino/PrestoDB 发行包硬性检查操作系统，Windows 原生无法运行，本脚本通过 WSL 运行。
  - 安装目录为发行版内 /opt/dbservers；发行版虚拟磁盘位于空余空间最多的固定磁盘。
  - 首次启动会自动下载 JDK 与发行包（约 1.2 GB），再次启动直接复用。
  - 启动后保持一个 WSL 会话（保活），避免 WSL 空闲停机导致数据库退出。
""");
}

internal sealed class Options
{
    public List<String> Positional { get; } = [];
    public Dictionary<String, String> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<String> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public String? Get(String name) => Values.TryGetValue(name, out var value) && value.Length > 0 ? value : null;
    public Boolean Has(String name) => Flags.Contains(name) || Values.ContainsKey(name);
    public Int32 Port => Get("port") is { } value && Int32.TryParse(value, out var port) ? port : (Positional.FirstOrDefault()?.Equals("presto", StringComparison.OrdinalIgnoreCase) == true ? 8081 : 8080);
}
