using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.Enums;

namespace TaskManager.UITests;

/// <summary>
/// Una sesion de Appium para toda la tanda: arranca el servidor si no esta ya escuchando, se
/// conecta al dispositivo y deja la app recien borrada (sin datos: nunca la cuenta de nadie).
/// Al acabar devuelve la letra a 1.0 y cierra lo que abrio.
/// </summary>
/// <remarks>
/// Variables (todas opcionales):
/// <list type="bullet">
/// <item><c>TM_UDID</c>: serie adb del dispositivo (por defecto el MuMu, <c>127.0.0.1:16416</c>).</item>
/// <item><c>TM_APPIUM_URL</c>: servidor ya arrancado (por defecto <c>http://127.0.0.1:4723/</c>).</item>
/// <item><c>TM_APPIUM</c>: ejecutable de Appium para arrancarlo si no responde
///   (por defecto <c>D:\dev\appium\node_modules\.bin\appium.cmd</c>).</item>
/// <item><c>APPIUM_HOME</c>: donde estan los drivers (por defecto <c>D:\dev\appium-home</c>).</item>
/// <item><c>ANDROID_HOME</c>: SDK con adb (por defecto <c>D:\dev\android-sdk</c>).</item>
/// <item><c>TM_APK</c>: APK a instalar antes de empezar; sin ella se usa la que haya instalada.</item>
/// </list>
/// </remarks>
public sealed class UiSession : IDisposable
{
    public const string Package = "com.socratic.taskmanager";

    public AndroidDriver Driver { get; }

    public string Udid { get; } = Env("TM_UDID", "127.0.0.1:16416");

    public string ArtifactsDir { get; }

    private readonly Process? _server;
    private readonly string _adb;

    public UiSession()
    {
        var androidHome = Env("ANDROID_HOME", @"D:\dev\android-sdk");
        _adb = Path.Combine(androidHome, "platform-tools", "adb.exe");

        ArtifactsDir = Path.Combine(ProjectDir(), "artifacts", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(ArtifactsDir);

        var url = new Uri(Env("TM_APPIUM_URL", "http://127.0.0.1:4723/"));
        if (!ServerAlive(url))
        {
            _server = StartServer(url, androidHome);
        }

        // Por si una tanda anterior se corto a medias con la letra grande puesta.
        SetFontScale(1.0);

        var options = new AppiumOptions
        {
            PlatformName = "Android",
            AutomationName = AutomationName.AndroidUIAutomator2,
        };
        options.AddAdditionalAppiumOption("udid", Udid);
        options.AddAdditionalAppiumOption("appPackage", Package);
        options.AddAdditionalAppiumOption("appWaitActivity", "*");
        // noReset=false: Appium hace «pm clear» antes de abrir. La app entra siempre sin cuenta y
        // sin datos; nunca con la sesion de una persona real.
        options.AddAdditionalAppiumOption("noReset", false);
        options.AddAdditionalAppiumOption("fullReset", false);
        options.AddAdditionalAppiumOption("newCommandTimeout", 300);
        options.AddAdditionalAppiumOption("autoGrantPermissions", true);
        options.AddAdditionalAppiumOption("disableWindowAnimation", true);
        if (Environment.GetEnvironmentVariable("TM_APK") is { Length: > 0 } apk)
        {
            options.AddAdditionalAppiumOption("app", apk);
        }

        Driver = new AndroidDriver(url, options, TimeSpan.FromMinutes(3));
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
    }

    // ---------------------------------------------------------------- textos de la app

    private static readonly Dictionary<string, string> English = Texts("English");
    private static readonly Dictionary<string, string> Spanish = Texts("Spanish");

    /// <summary>El texto que la app enseña para una clave, en el idioma pedido.</summary>
    public static string T(string key, string language) =>
        (language == "es" ? Spanish : English)[key];

    private static Dictionary<string, string> Texts(string field) =>
        (Dictionary<string, string>)typeof(TaskManager.Core.Services.LocalizationService)
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    // ---------------------------------------------------------------- utilidades

    /// <summary>Espera a que aparezca un elemento; null si no llega a tiempo.</summary>
    public AppiumElement? WaitFor(By by, double seconds = 10)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        do
        {
            var found = Driver.FindElements(by);
            if (found.Count > 0)
            {
                return found[0];
            }

            Thread.Sleep(250);
        }
        while (DateTime.UtcNow < until);

        return null;
    }

    public AppiumElement Require(By by, string what, double seconds = 10) =>
        WaitFor(by, seconds) ?? throw new Xunit.Sdk.XunitException($"No aparece {what} ({by}).");

    public static By Id(string automationId) => MobileBy.AccessibilityId(automationId);

    public static By Text(string text) =>
        By.XPath($"//*[@text={XPathLiteral(text)}]");

    public static By TextContains(string text) =>
        By.XPath($"//*[contains(@text,{XPathLiteral(text)})]");

    private static string XPathLiteral(string s) =>
        s.Contains('\'') ? $"concat('{s.Replace("'", "',\"'\",'")}')" : $"'{s}'";

    /// <summary>Guarda la pantalla en la carpeta de artefactos de la tanda.</summary>
    public void Shot(string name)
    {
        var file = Path.Combine(ArtifactsDir, $"{DateTime.Now:HHmmss}-{name}.png");
        Driver.GetScreenshot().SaveAsFile(file);
    }

    public void Back() => Driver.Navigate().Back();

    public AppState State() => Driver.GetAppState(Package);

    /// <summary>Cierra la app y la vuelve a abrir (conserva los datos).</summary>
    public void Restart()
    {
        Driver.TerminateApp(Package);
        Driver.ActivateApp(Package);
    }

    public void SetFontScale(double scale) =>
        Adb($"shell settings put system font_scale {scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    public string Adb(string args)
    {
        var psi = new ProcessStartInfo(_adb, $"-s {Udid} {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(30_000);
        return output;
    }

    public void Dispose()
    {
        try
        {
            SetFontScale(1.0);
        }
        catch
        {
            // Si adb no responde no hay mas que hacer; el README avisa de comprobarlo.
        }

        try
        {
            Driver.Quit();
        }
        catch
        {
        }

        if (_server is { HasExited: false })
        {
            _server.Kill(entireProcessTree: true);
        }
    }

    // ---------------------------------------------------------------- servidor

    private static bool ServerAlive(Uri url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            return http.GetAsync(new Uri(url, "status")).Result.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static Process StartServer(Uri url, string androidHome)
    {
        var exe = Env("TM_APPIUM", @"D:\dev\appium\node_modules\.bin\appium.cmd");
        var psi = new ProcessStartInfo(exe, $"--address {url.Host} --port {url.Port} --log-level warn")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["APPIUM_HOME"] = Env("APPIUM_HOME", @"D:\dev\appium-home");
        psi.Environment["ANDROID_HOME"] = androidHome;
        psi.Environment["ANDROID_SDK_ROOT"] = androidHome;

        var server = Process.Start(psi)!;
        server.OutputDataReceived += (_, _) => { };
        server.ErrorDataReceived += (_, _) => { };
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();

        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until)
        {
            if (ServerAlive(url))
            {
                return server;
            }

            Thread.Sleep(500);
        }

        server.Kill(entireProcessTree: true);
        throw new InvalidOperationException($"Appium no arranca en {url} con {exe}.");
    }

    private static string Env(string name, string fallback) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

    private static string ProjectDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TaskManager.UITests.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}

[CollectionDefinition(Name)]
public sealed class UiCollection : ICollectionFixture<UiSession>
{
    public const string Name = "Appium";
}

/// <summary>Las pruebas de una clase se ejecutan por orden de nombre (T01, T02...).</summary>
public sealed class ByNameOrderer : Xunit.Sdk.ITestCaseOrderer
{
    public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases)
        where TTestCase : Xunit.Abstractions.ITestCase =>
        testCases.OrderBy(t => t.TestMethod.Method.Name, StringComparer.Ordinal);
}
