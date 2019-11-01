using Newtonsoft.Json;

namespace PactNetMessages.Models
{
    public class PactVerificationResult
    {
        [JsonProperty(PropertyName = "providerApplicationVersion")]
        public string ProviderApplicationVersion { get; set; }

        [JsonProperty(PropertyName = "success")]
        public bool Success { get; set; }
    }
}