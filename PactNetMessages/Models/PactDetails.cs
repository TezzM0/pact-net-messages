using System;
using Newtonsoft.Json;
using PactNetMessages.Extensions;


namespace PactNetMessages.Models
{
    public class PactDetails
    {
        [JsonProperty(Order = -2, PropertyName = "consumer")]
        public Pacticipant Consumer { get; set; }

        [JsonProperty(Order = -3, PropertyName = "provider")]
        public Pacticipant Provider { get; set; }

        public string GeneratePactFileName()
        {
            return String.Format("{0}-{1}.json",
                    Consumer != null ? Consumer.Name : String.Empty,
                    Provider != null ? Provider.Name : String.Empty)
                .ToLowerSnakeCase();
        }

        [JsonProperty(PropertyName = "_links")]
        public Links Links { get; set; }
    }

    public class Links
    {
        [JsonProperty(PropertyName = "pb:publish-verification-results")]
        public Link PublishVerificationResults { get; set; }
    }

    public class Link
    {
        [JsonProperty(PropertyName = "title")]
        public string Title { get; set; }

        [JsonProperty(PropertyName = "href")]
        public string Href { get; set; }
    }
}