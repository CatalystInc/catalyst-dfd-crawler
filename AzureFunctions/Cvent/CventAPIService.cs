using Azure.Core;
using AzureFunctions.Cvent.Models;
using AzureFunctions.Indexer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;

namespace AzureFunctions.Cvent
{
    public class CventAPIService
    {
        internal readonly HttpClient _httpClient;
        internal readonly ILogger<CventAPIService> _logger;

        private readonly string _cventServiceEndpoint;
        private readonly string _cventClientId;
        private readonly string _cventClientSecret;

        public CventAPIService(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _logger = loggerFactory.CreateLogger<CventAPIService>();

            _cventServiceEndpoint = configuration["CventServiceEndpoint"] ?? throw new ArgumentNullException(nameof(configuration), "CventServiceEndpoint is missing");
            _cventClientId = configuration["CventClientId"] ?? throw new ArgumentNullException(nameof(configuration), "CventClientId is missing");
            _cventClientSecret = configuration["CventClientSecret"] ?? throw new ArgumentNullException(nameof(configuration), "CventClientSecret is missing");

            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(_cventServiceEndpoint);
            var userAgent = configuration["UserAgent"] ?? "CatalystCventBot/1.0";
            _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            _httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true
            };

        }

        public async Task<AccessTokenResponseModel> GetAccessToken()
        {
            _logger.LogInformation($"[CVENT API Service] Starting GetAccessToken call.");

            var url = "/ea/oauth2/token";

            _logger.LogInformation($"[CVENT API Service] cientId: {_cventClientId}, client secret: {_cventClientSecret}");

            var request = new FormUrlEncodedContent(new Dictionary<string, string>
            {
               { "grant_type", "client_credentials" },
               { "client_id", _cventClientId },
               { "client_secret", _cventClientSecret }
            });

            var httpRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(url, UriKind.Relative),
                Content = request
            };

            _logger.LogInformation($"[CVENT API Service] Getting base64 authorization.");
            var authorization = GetBase64Authorization();
            _logger.LogInformation($"[CVENT API Service] base64 authorization: {authorization}");

            httpRequest.Headers.Add("authorization", $"Basic {authorization}"); 
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            _logger.LogInformation($"[CVENT API Service] Sending http request to get access token. base url: {_httpClient.BaseAddress}, relative url: {httpRequest.RequestUri.ToString()}");

            var response = await _httpClient.SendAsync(httpRequest);

            _logger.LogInformation($"[CVENT API Service] Got response from http request to get access token");

            var stringResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"[CVENT API Service] Access Token response: {response.StatusCode}, reason: {response.ReasonPhrase}, content: {stringResponse}");
            }
            else
            {
                _logger.LogInformation($"[CVENT API Service] Success status code: {response.StatusCode}, {response.ReasonPhrase}");
                _logger.LogInformation($"[CVENT API Service] Access Token response: {stringResponse}");
            }
            _logger.LogInformation($"[CVENT API Service] Deserializaing response.");
            var accessTokenModel = JsonConvert.DeserializeObject<AccessTokenResponseModel>(stringResponse);
            _logger.LogInformation($"[CVENT API Service] Finishing GetAccessToken call");
            return accessTokenModel;
        }

        public async Task<EventsResponseModel> GetEvents(string accessToken)
        {
            _logger.LogInformation($"[CVENT API Service] Starting GetEvents call.");
            var url = "/ea/events";

            var httpRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url, UriKind.Relative)
            };

            _logger.LogInformation($"[CVENT API Service] access token: {accessToken}");

            httpRequest.Headers.Add("authorization", $"Bearer {accessToken}");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _logger.LogInformation($"[CVENT API Service] Sending http request to get events data. base url: {_httpClient.BaseAddress}, relative url: {httpRequest.RequestUri.ToString()}");

            var response = await _httpClient.SendAsync(httpRequest);
            var stringResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"[CVENT API Service] GetEvents response: {response.StatusCode}, {stringResponse}");
            }
            else
            {
                _logger.LogInformation($"[CVENT API Service] Success status code: {response.StatusCode}, {response.ReasonPhrase}");
                _logger.LogInformation($"[CVENT API Service] GetEvents response: {stringResponse}");
            }

            _logger.LogInformation($"[CVENT API Service] Deserializaing response.");
            var eventsResponse = JsonConvert.DeserializeObject<EventsResponseModel>(stringResponse);
            _logger.LogInformation($"[CVENT API Service] Finishing GetEvents call");
            return eventsResponse;
        }

        private string GetBase64Authorization()
        {
            var credentials = $"{_cventClientId}:{_cventClientSecret}";
            var authorization = EncodeToBase64(credentials);
            return authorization;
        }

        private string EncodeToBase64(string toEncode)
        {
            byte[] toEncodeAsBytes = System.Text.UTF8Encoding.Default.GetBytes(toEncode);
            string returnValue = System.Convert.ToBase64String(toEncodeAsBytes);
            return returnValue;
        }
    }
}
