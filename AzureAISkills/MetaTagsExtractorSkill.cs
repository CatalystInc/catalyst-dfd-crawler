using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

public class MetaTagsExtractorSkill
{
	private readonly ILogger _logger;

	public MetaTagsExtractorSkill(ILoggerFactory loggerFactory)
	{
		_logger = loggerFactory.CreateLogger<MetaTagsExtractorSkill>();
	}

	[Function("MetaTagsExtractor")]
	public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
	{
		_logger.LogInformation("C# HTTP trigger function processed a request.");

		string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
		var data = JsonSerializer.Deserialize<SkillRequest>(requestBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

		if (data?.Values == null)
		{
			return req.CreateResponse(HttpStatusCode.BadRequest);
		}

		var results = new List<OutputRecord>();

		foreach (var record in data.Values)
		{
			if (!record.Data.TryGetValue("metaTags", out object? metaTagsObj))
			{
				_logger.LogWarning("Record {RecordId} does not contain 'metaTags' field.", record.RecordId);
				continue;
			}

			if (metaTagsObj is not JsonElement metaTagsElement)
			{
				_logger.LogWarning("MetaTags in record {RecordId} is not in the expected format.", record.RecordId);
				continue;
			}

			var outputData = new Dictionary<string, object>();

			if (metaTagsElement.ValueKind == JsonValueKind.Object)
			{
				foreach (var property in metaTagsElement.EnumerateObject())
				{
					if (data.Fields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
					{
						outputData[property.Name] = property.Value.GetString() ?? string.Empty;
					}
				}
			}

			results.Add(new OutputRecord
			{
				RecordId = record.RecordId,
				Data = outputData
			});
		}

		var response = req.CreateResponse(HttpStatusCode.OK);
		await response.WriteAsJsonAsync(new SkillResponse { Values = results });
		return response;
	}
}

public record SkillRequest(List<InputRecord> Values, List<string> Fields);

public record InputRecord(string RecordId, Dictionary<string, object> Data);

public record OutputRecord
{
	public required string RecordId { get; init; }
	public required Dictionary<string, object> Data { get; init; }
}

public record SkillResponse
{
	public required List<OutputRecord> Values { get; init; }
}