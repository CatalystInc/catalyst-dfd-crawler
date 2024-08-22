namespace AzureFunctions.Indexer
{
	public class JsonLdConfig
	{
		public string SourceElementPath { get; set; }
		public string TargetField { get; set; }
		public string TargetType { get; set; }
	}
}