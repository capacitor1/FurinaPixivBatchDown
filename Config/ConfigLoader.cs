// ConfigLoader.cs
using System;
using System.IO;
using System.Text.Json;

public static class ConfigLoader
{
    private const string ConfigFileName = "Config.json";

    /// <summary>
    /// 加载配置文件，如果不存在则创建默认配置并保存。
    /// </summary>
    public static AppConfig Load()
    {
        Console.WriteLine("[CONF I] Loading config...");

        try
        {
            if (!File.Exists(ConfigFileName))
            {
                Console.WriteLine($"[CONF W] Config file '{ConfigFileName}' not found. Creating default config...");
                var defaultConfig = CreateDefaultConfig();
                Save(defaultConfig);
                return defaultConfig;
            }

            string jsonContent = File.ReadAllText(ConfigFileName);
            if (string.IsNullOrWhiteSpace(jsonContent))
            {
                Console.WriteLine("[CONF W] Config file is empty. Creating default config...");
                var defaultConfig = CreateDefaultConfig();
                Save(defaultConfig);
                return defaultConfig;
            }

            var config = JsonSerializer.Deserialize<AppConfig>(jsonContent);
            if (config == null)
            {
                Console.WriteLine("[CONF W] Failed to deserialize config. Using default config...");
                return CreateDefaultConfig();
            }

            Console.WriteLine("[CONF I] Config loaded successfully.");
            return config;
        }
        catch (IOException ioEx)
        {
            Console.WriteLine($"[CONF E] I/O error accessing config file: {ioEx.Message}");
            return CreateDefaultConfig();
        }
        catch (JsonException jsonEx)
        {
            Console.WriteLine($"[CONF E] JSON parsing error: {jsonEx.Message}");
            return CreateDefaultConfig();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CONF E] Unexpected error loading config: {ex.Message}");
            return CreateDefaultConfig();
        }
    }

    private static AppConfig CreateDefaultConfig()
    {
        return new AppConfig
        {
            Cookie = string.Empty,
            SaveBasePath = string.Empty,
            NeedAI = true,
            AutoLoadUsersList = string.Empty,
            ApiRequestDelay = 1000,
            Init429Delay = 30000,
            NeedUpdateNovels = false
        };
    }

    private static void Save(AppConfig config)
    {
        try
        {
            string jsonContent = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFileName, jsonContent);
            Console.WriteLine($"[CONF I] Default config saved to '{ConfigFileName}'.");
        }
        catch (IOException ioEx)
        {
            Console.WriteLine($"[CONF E] Failed to save default config: {ioEx.Message}");
        }
    }
}