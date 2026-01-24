// AppConfig.cs
using System.Text.Json.Serialization;

public class AppConfig
{
    [JsonPropertyName("cookie")]
    public string? Cookie { get; set; }

    [JsonPropertyName("savebasepath")]
    public string? SaveBasePath { get; set; }

    [JsonPropertyName("needAI")]
    public bool NeedAI { get; set; }

    [JsonPropertyName("autoloaduserslist")]
    public string? AutoLoadUsersList { get; set; }

    [JsonPropertyName("init_429delay")]
    public int? Init429Delay { get; set; }

    [JsonPropertyName("apirequestdelay")]
    public int? ApiRequestDelay { get; set; }

    [JsonPropertyName("needupdatenovels")]
    public bool NeedUpdateNovels { get; set; }
}