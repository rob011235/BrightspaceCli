namespace OllamaExp
{
    using System.Text.Json.Serialization;

    public sealed class TagsResponse
    {
        [JsonPropertyName("models")]
        public List<ModelInfo> Models { get; set; } = new();
    }
}