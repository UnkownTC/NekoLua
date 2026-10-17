using LabApi.Features.Console;
using System.Text;

namespace NekoLua.Loader;

internal static class LuaEmbeddedExtractor
{
    private const string ResourcePrefix = "NekoLua.EmbeddedLua.";
    public static (int Extracted, int Skipped) ExtractAll()
    {
        var cfg = MainPlugin.CacheConfig;

        if (!cfg.ExtractEmbeddedLua)
        {
            return (0, 0);
        }

        var asm = MainPlugin.PluginAssembly;
        if (asm is null)
        {
            return (0, 0);
        }

        var targetRoot = LuaPaths.GlobalDependencies;
        if (!targetRoot.Exists)
            targetRoot.Create();

        var overwrite = cfg.OverwriteEmbeddedLua;
        var extracted = 0;
        var skipped = 0;

        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                continue;
            if (!resourceName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                continue;

            var rel = resourceName.Substring(ResourcePrefix.Length);

            if (rel.Length == 0 ||
                rel.Contains("..") ||
                rel.Contains('/') ||
                rel.Contains('\\'))
            {
                continue;
            }

            var targetPath = Path.Combine(targetRoot.FullName, rel);

            if (File.Exists(targetPath) && !overwrite)
            {
                skipped++;
                continue;
            }

            try
            {
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var content = reader.ReadToEnd();

                File.WriteAllText(targetPath, content, new UTF8Encoding(false));
                extracted++;
            }
            catch (Exception ex)
            {
                Logger.Error($"解压 {rel} 失败: {ex.Message}");
            }
        }

        Logger.Info($"内嵌库: 解压 {extracted} 个, 跳过 {skipped} 个 → {targetRoot.FullName}");
        return (extracted, skipped);
    }
    public static string[] ListResources()
    {
        var asm = MainPlugin.PluginAssembly;
        return asm is null
            ? []
            : [.. asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                     && n.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))];
    }
}
