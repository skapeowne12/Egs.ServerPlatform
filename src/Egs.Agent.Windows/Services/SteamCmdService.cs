using System.Diagnostics;
using System.IO.Compression;

namespace Egs.Agent.Windows.Services;

public sealed class SteamCmdService
{
    private const string SteamCmdDownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SteamCmdService> _logger;

    public SteamCmdService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<SteamCmdService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public string ResolveInstallDirectory(string installPath)
    {
        var rootPath = _configuration["Agent:ServerRootPath"];
        var serverRootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(AppContext.BaseDirectory, "runtime", "servers")
            : rootPath;

        Directory.CreateDirectory(serverRootPath);

        return Path.IsPathRooted(installPath)
            ? Path.GetFullPath(installPath)
            : Path.GetFullPath(Path.Combine(serverRootPath, installPath));
    }

    public async Task EnsureSteamCmdInstalledAsync(Func<string, Task> writeLineAsync, CancellationToken ct)
    {
        var executablePath = GetSteamCmdExecutablePath();
        if (File.Exists(executablePath))
        {
            return;
        }

        var steamCmdDirectory = Path.GetDirectoryName(executablePath)!;
        Directory.CreateDirectory(steamCmdDirectory);

        var zipPath = Path.Combine(steamCmdDirectory, "steamcmd.zip");
        await writeLineAsync($"[SteamCMD] Downloading SteamCMD to '{zipPath}'...");

        using (var response = await _httpClient.GetAsync(SteamCmdDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var downloadStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(zipPath);
            await downloadStream.CopyToAsync(fileStream, ct);
        }

        await writeLineAsync("[SteamCMD] Extracting SteamCMD package...");
        ZipFile.ExtractToDirectory(zipPath, steamCmdDirectory, overwriteFiles: true);
        File.Delete(zipPath);

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("SteamCMD download completed, but steamcmd.exe was not found.", executablePath);
        }
    }

    public async Task InstallAppAsync(
        string appId,
        string installDirectory,
        bool validate,
        Func<string, Task> writeLineAsync,
        CancellationToken ct)
    {
        await EnsureSteamCmdInstalledAsync(writeLineAsync, ct);

        Directory.CreateDirectory(installDirectory);

        var steamCmdExe = GetSteamCmdExecutablePath();
        var steamCmdWorkingDirectory = Path.GetDirectoryName(steamCmdExe)!;
        var installCommand = $"+force_install_dir {Quote(installDirectory)} {BuildLoginCommand(anonymous: true)} +app_update {appId}";
        if (validate)
        {
            installCommand += " validate";
        }

        installCommand += " +quit";

        await writeLineAsync($"[SteamCMD] Installing app {appId} into '{installDirectory}'...");
        var exitCode = await RunProcessAsync(steamCmdExe, installCommand, steamCmdWorkingDirectory, writeLineAsync, ct);
        if (exitCode == 0)
        {
            return;
        }

        var steamUser = _configuration["Agent:SteamUser"];
        var steamPassword = _configuration["Agent:SteamPassword"];
        if (string.IsNullOrWhiteSpace(steamUser) || string.IsNullOrWhiteSpace(steamPassword))
        {
            throw new InvalidOperationException(
                $"SteamCMD exited with code {exitCode} while installing app {appId}. Configure Agent:SteamUser and Agent:SteamPassword if this server requires authenticated SteamCMD access.");
        }

        await writeLineAsync("[SteamCMD] Anonymous install failed; retrying with configured Steam credentials...");

        var credentialedCommand = $"+force_install_dir {Quote(installDirectory)} {BuildLoginCommand(anonymous: false)} +app_update {appId}";
        if (validate)
        {
            credentialedCommand += " validate";
        }

        credentialedCommand += " +quit";

        exitCode = await RunProcessAsync(steamCmdExe, credentialedCommand, steamCmdWorkingDirectory, writeLineAsync, ct);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"SteamCMD exited with code {exitCode} while installing app {appId}, even after retrying with configured credentials.");
        }
    }

    private async Task<int> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Func<string, Task> writeLineAsync,
        CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _ = writeLineAsync($"[SteamCMD] {args.Data}");
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                _ = writeLineAsync($"[SteamCMD] {args.Data}");
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        }

        _logger.LogInformation("Started SteamCMD process {ProcessId}: {FileName} {Arguments}", process.Id, fileName, arguments);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);

        return process.ExitCode;
    }

    private string BuildLoginCommand(bool anonymous)
    {
        if (anonymous)
        {
            return "+login anonymous";
        }

        var steamUser = _configuration["Agent:SteamUser"] ?? string.Empty;
        var steamPassword = _configuration["Agent:SteamPassword"] ?? string.Empty;
        return $"+login {Quote(steamUser)} {Quote(steamPassword)}";
    }

    private string GetSteamCmdExecutablePath()
    {
        var rootPath = _configuration["Agent:SteamCmdRootPath"];
        var steamCmdRootPath = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(AppContext.BaseDirectory, "runtime", "steamcmd")
            : rootPath;

        return Path.Combine(steamCmdRootPath, "steamcmd.exe");
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
