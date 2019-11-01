﻿using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json;
using PactNetMessages.Extensions;
using PactNetMessages.Logging;
using PactNetMessages.Mocks.MockMq.Models;
using PactNetMessages.Mocks.MockMq.Validators;
using PactNetMessages.Models;
using PactNetMessages.Reporters;


namespace PactNetMessages
{
    public class PactVerifier : IPactVerifier, IDisposable
    {
        private readonly PactVerifierConfig _pactVerifierConfig;
        private readonly HttpClient _httpClient;

        public string ConsumerName { get; private set; }

        public string PactFileUri { get; private set; }
        public PactUriOptions PactUriOptions { get; private set; }

        public string ProviderName { get; private set; }

        public ProviderStates ProviderStates { get; }


        /// <summary>
        /// Define any set up and tear down state that is required when running the interaction verify.
        /// We strongly recommend that any set up state is cleared using the tear down. This includes any state and IoC container overrides you may be doing.
        /// </summary>
        /// <param name="setUp">A set up action that will be run before each interaction verify. If no action is required please use an empty lambda () => {}.</param>
        /// <param name="tearDown">A tear down action that will be run after each interaction verify. If no action is required please use an empty lambda () => {}.</param>
        /// <param name="config"></param>
        public PactVerifier(Action setUp, Action tearDown, PactVerifierConfig config = null)
        {
            _pactVerifierConfig = config ?? new PactVerifierConfig();
            ProviderStates = new ProviderStates(setUp, tearDown);
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Define a set up and/or tear down action for a specific state specified by the consumer.
        /// This is where you should set up test data, so that you can fulfil the contract outlined by a consumer.
        /// </summary>
        /// <param name="providerState">The name of the provider state as defined by the consumer interaction, which lives in the Pact file.</param>
        /// <param name="setUp">A set up function that will be run before the interaction verify.  The setup function must return a Message object for verification.</param>
        /// <param name="tearDown">A tear down action that will be run after the interaction verify, if the provider has specified it in the interaction. If no action is required please use an empty lambda () => {}.</param>
        public IPactVerifier ProviderState(string providerState, Func<Message> setUp, Action tearDown = null)
        {
            if (String.IsNullOrEmpty(providerState))
            {
                throw new ArgumentException("Please supply a non null or empty providerState");
            }

            var providerStateItem = new ProviderState(providerState, setUp, tearDown);
            ProviderStates.Add(providerStateItem);

            return this;
        }

        public IPactVerifier MessageProvider(string providerName)
        {
            if (String.IsNullOrEmpty(providerName))
            {
                throw new ArgumentException("Please supply a non null or empty providerName");
            }

            if (!String.IsNullOrEmpty(ProviderName))
            {
                throw new ArgumentException(
                    "ProviderName has already been supplied, please instantiate a new PactVerifier if you want to perform verification for a different provider");
            }


            ProviderName = providerName;
            return this;
        }

        public IPactVerifier HonoursPactWith(string consumerName)
        {
            if (String.IsNullOrEmpty(consumerName))
            {
                throw new ArgumentException("Please supply a non null or empty consumerName");
            }

            if (!String.IsNullOrEmpty(ConsumerName))
            {
                throw new ArgumentException(
                    "ConsumerName has already been supplied, please instantiate a new PactVerifier if you want to perform verification for a different consumer");
            }

            ConsumerName = consumerName;

            return this;
        }

        public IPactVerifier PactUri(string fileUri, PactUriOptions options = null)
        {
            if (string.IsNullOrEmpty(fileUri))
            {
                throw new ArgumentException("Please supply a non null or empty fileUri");
            }

            PactFileUri = fileUri;
            PactUriOptions = options;

            return this;
        }

        public void Verify(string description = null, string providerState = null)
        {
            if (String.IsNullOrEmpty(PactFileUri))
            {
                throw new InvalidOperationException(
                    "PactFileUri has not been set, please supply a uri using the PactUri method.");
            }

            MessagePactFile pactFile;
            try
            {
                string pactFileJson;

                if (IsWebUri(PactFileUri))
                {
                    pactFileJson = HttpGetPactFile();
                }
                else
                {
                    pactFileJson = File.ReadAllText(PactFileUri);
                }

                pactFile = JsonConvert.DeserializeObject<MessagePactFile>(pactFileJson);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    String.Format("Json Pact file could not be retrieved using uri \'{0}\'.", PactFileUri), ex);
            }

            //Filter interactions
            if (description != null)
            {
                pactFile.Messages = pactFile.Messages.Where(x => x.Description.Equals(description));
            }

            if (providerState != null)
            {
                pactFile.Messages = pactFile.Messages.Where(x => x.ProviderState.Equals(providerState));
            }

            if ((description != null || providerState != null) &&
                (pactFile.Messages == null || !pactFile.Messages.Any()))
            {
                throw new ArgumentException(
                    "The specified description and/or providerState filter yielded no interactions.");
            }

            var loggerName = LogProvider.CurrentLogProvider.AddLogger(_pactVerifierConfig.LogDir,
                ProviderName.ToLowerSnakeCase(), "{0}_verifier.log");
            _pactVerifierConfig.LoggerName = loggerName;

            var verificationResult = new PactVerificationResult
            {
                ProviderApplicationVersion = _pactVerifierConfig.ProviderVersion
            };

            try
            {
                var validator = new MessageProviderValidator(new Reporter(_pactVerifierConfig), _pactVerifierConfig);
                validator.Validate(pactFile, ProviderStates);

                verificationResult.Success = true;
            }
            finally
            {
                LogProvider.CurrentLogProvider.RemoveLogger(_pactVerifierConfig.LoggerName);
            }

            if (_pactVerifierConfig.PublishVerificationResults)
            {
                HttpPostPublishVerificationResults(
                    pactFile.Links.PublishVerificationResults.Href, verificationResult);
            }
        }

        private void HttpPostPublishVerificationResults(string uri, PactVerificationResult verificationResult)
        {
            ApplyAuthorizationHeadersIfRequired();
            _httpClient.PostAsync(uri, new StringContent(JsonConvert.SerializeObject(verificationResult))).GetAwaiter().GetResult();
        }

        private string HttpGetPactFile()
        {
            ApplyAuthorizationHeadersIfRequired();
            return _httpClient.GetStringAsync(PactFileUri).GetAwaiter().GetResult();
        }

        private void ApplyAuthorizationHeadersIfRequired()
        {
            if (PactUriOptions != null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                    PactUriOptions.AuthorizationScheme, PactUriOptions.AuthorizationValue);
            }
        }

        private static bool IsWebUri(string uri)
        {
            return uri.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase) ||
                uri.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}