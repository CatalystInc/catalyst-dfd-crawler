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
            var url = "/ea/oauth2/token";

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

            var authorization = GetBase64Authorization();

            httpRequest.Headers.Add("authorization", $"Basic {authorization}");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

            var response = await _httpClient.SendAsync(httpRequest);

            var stringResponse = await response.Content.ReadAsStringAsync();
            
            //_logger.LogInformation($"[CVENT API Service] Access Token response: {stringResponse}");
            var accessTokenModel = JsonConvert.DeserializeObject<AccessTokenResponseModel>(stringResponse);
            return accessTokenModel;
        }

        public async Task<EventsResponseModel> GetEvents(string accessToken)
        {
            var url = "/ea/events";

            var httpRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url, UriKind.Relative)
            };

            httpRequest.Headers.Add("authorization", $"Bearer {accessToken}");
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(httpRequest);
            var stringResponse = await response.Content.ReadAsStringAsync();

            //_logger.LogInformation($"[CVENT API Service] Events response -> status code: {response.StatusCode}, content: {stringResponse}");
            var eventsResponse = JsonConvert.DeserializeObject<EventsResponseModel>(stringResponse);
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
