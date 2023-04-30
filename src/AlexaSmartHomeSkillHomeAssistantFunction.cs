using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Net.Http;
using System;

namespace azure_function_alexasmarthomeskill_homeassistant
{
    public static class AlexaSmartHomeSkillHomeAssistantFunction
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        [FunctionName("AlexaSmartHomeSkillHomeAssistantFunction")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic eventData = JsonConvert.DeserializeObject(requestBody);

            string base_url = Environment.GetEnvironmentVariable("BASE_URL");
            if (string.IsNullOrEmpty(base_url))
            {
                return new BadRequestObjectResult("Please set BASE_URL environment variable");
            }
            base_url = base_url.TrimEnd('/');

            dynamic directive = eventData.directive;
            if (directive == null)
            {
                return new BadRequestObjectResult("Malformatted request - missing directive");
            }

            if (directive.header.payloadVersion != "3")
            {
                return new BadRequestObjectResult("Only support payloadVersion == 3");
            }

            dynamic scope = directive.endpoint.scope ?? directive.payload.grantee ?? directive.payload.scope;
            if (scope == null)
            {
                return new BadRequestObjectResult("Malformatted request - missing endpoint.scope");
            }

            if (scope.type != "BearerToken")
            {
                return new BadRequestObjectResult("Only support BearerToken");
            }

            string token = scope.token ?? Environment.GetEnvironmentVariable("LONG_LIVED_ACCESS_TOKEN");
            bool verify_ssl = !bool.Parse(Environment.GetEnvironmentVariable("NOT_VERIFY_SSL") ?? "false");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var content = new StringContent(JsonConvert.SerializeObject(eventData), System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{base_url}/api/alexa/smart_home", content);

            if ((int)response.StatusCode >= 400)
            {
                var errorType = response.StatusCode == System.Net.HttpStatusCode.Unauthorized || response.StatusCode == System.Net.HttpStatusCode.Forbidden
                    ? "INVALID_AUTHORIZATION_CREDENTIAL"
                    : "INTERNAL_ERROR";
                var errorMessage = await response.Content.ReadAsStringAsync();

                return new OkObjectResult(new
                {
                    eventObj = new
                    {
                        header = new
                        {
                            @namespace = "Alexa",
                            name = "ErrorResponse",
                            messageId = Guid.NewGuid().ToString(),
                            payloadVersion = "3"
                        },
                        payload = new
                        {
                            type = errorType,
                            message = errorMessage
                        }
                    }

                });
            }

            string responseContent = await response.Content.ReadAsStringAsync();
            dynamic responseObj = JsonConvert.DeserializeObject(responseContent);

            return new OkObjectResult(responseObj);
        }
    }
}