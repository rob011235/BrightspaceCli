namespace OllamaExp
{
    using System.Text.Json.Serialization;
    
    public sealed class ModelInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
    }
}