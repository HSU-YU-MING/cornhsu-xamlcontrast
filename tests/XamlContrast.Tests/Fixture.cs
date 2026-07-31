using XamlContrast.Core;

namespace XamlContrast.Tests;

/// <summary>暫存專案目錄：擺幾個 XAML/C# 檔，跑偵測＋稽核，用完即丟。</summary>
internal sealed class Fixture : IDisposable
{
    public string Root { get; }

    public Fixture(string? dirName = null)
    {
        Root = Path.Combine(Path.GetTempPath(), dirName ?? ("xc-test-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Root);
    }

    public Fixture File(string relPath, string content)
    {
        var full = Path.Combine(Root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return this;
    }

    public AuditResult Run() => Auditor.Run(Root, PaletteDetector.Detect(Root));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* 暫存目錄，清不掉就算了 */ }
    }
}
