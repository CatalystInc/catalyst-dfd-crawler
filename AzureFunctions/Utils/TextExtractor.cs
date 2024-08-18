using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AzureSearchCrawler
{
	/// <summary>
	/// Extracts text content from a web page. The default implementation removes all script, style,
	/// svg, and path tags, and then returns the InnerText of the page body, with cleaned up whitespace.
	/// </summary>
	/// <remarks>
	/// You can implement your own custom text extraction by overriding the ExtractPageContent method.
	/// The protected helper methods in this class might be useful. GetCleanedUpTextForXpath is the easiest way to get started.
	/// </remarks>
	public partial class TextExtractor : ITextExtractor
	{
		private readonly ILogger<TextExtractor> _logger;

		/// <summary>
		/// Initializes a new instance of the <see cref="TextExtractor"/> class.
		/// </summary>
		/// <param name="logger">The logger.</param>
		public TextExtractor(ILoggerFactory loggerFactory)
		{
			_logger = loggerFactory.CreateLogger<TextExtractor>();
		}

		/// <summary>
		/// Extracts the content from the provided HTML document.
		/// </summary>
		/// <param name="doc">The HTML document to extract content from.</param>
		/// <returns>The extracted page content.</returns>
		public virtual PageContent ExtractPageContent(HtmlDocument doc)
		{
			ArgumentNullException.ThrowIfNull(doc);

			return new PageContent
			{
				Title = ExtractTitle(doc),
				TextContent = GetCleanedUpTextForXpath(doc, "//body"),
				HtmlContent = ExtractHtmlContent(doc)
			};
		}

		/// <summary>
		/// Gets cleaned up text for the specified XPath.
		/// </summary>
		/// <param name="doc">The HTML document.</param>
		/// <param name="xpath">The XPath to extract text from.</param>
		/// <returns>The cleaned up text.</returns>
		public string GetCleanedUpTextForXpath(HtmlDocument doc, string xpath)
		{
			ArgumentNullException.ThrowIfNull(doc);
			ArgumentException.ThrowIfNullOrEmpty(xpath);

			try
			{
				RemoveNodesOfType(doc, "script", "style", "svg", "path");
				string content = ExtractTextFromFirstMatchingElement(doc, xpath);
				return NormalizeWhitespace(content);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error extracting text for XPath: {XPath}", xpath);
				return string.Empty;
			}
		}

		/// <summary>
		/// Normalizes whitespace in the given content.
		/// </summary>
		/// <param name="content">The content to normalize.</param>
		/// <returns>The normalized content.</returns>
		protected string NormalizeWhitespace(string? content)
		{
			if (string.IsNullOrEmpty(content))
			{
				return string.Empty;
			}

			content = NewlinesRegex().Replace(content, "\n");
			return SpacesRegex().Replace(content, " ").Trim();
		}

		/// <summary>
		/// Removes nodes of specified types from the document.
		/// </summary>
		/// <param name="doc">The HTML document.</param>
		/// <param name="types">The types of nodes to remove.</param>
		protected void RemoveNodesOfType(HtmlDocument doc, params string[] types)
		{
			ArgumentNullException.ThrowIfNull(doc);
			ArgumentNullException.ThrowIfNull(types);

			string xpath = string.Join(" | ", types.Select(t => $"//{t}"));
			RemoveNodes(doc, xpath);
		}

		/// <summary>
		/// Removes nodes matching the specified XPath from the document.
		/// </summary>
		/// <param name="doc">The HTML document.</param>
		/// <param name="xpath">The XPath of nodes to remove.</param>
		protected void RemoveNodes(HtmlDocument doc, string xpath)
		{
			var nodes = SafeSelectNodes(doc, xpath).ToList();
			_logger.LogDebug("Removing {Count} nodes matching {XPath}", nodes.Count, xpath);

			foreach (var node in nodes)
			{
				node.Remove();
			}
		}

		/// <summary>
		/// Extracts text from the first element matching the XPath expression.
		/// </summary>
		/// <param name="doc">The HTML document.</param>
		/// <param name="xpath">The XPath expression.</param>
		/// <returns>The inner text of the first matching element, or null if no elements match.</returns>
		protected string? ExtractTextFromFirstMatchingElement(HtmlDocument doc, string xpath)
		{
			return SafeSelectNodes(doc, xpath).FirstOrDefault()?.InnerText;
		}

		/// <summary>
		/// Safely selects nodes from the document using the specified XPath.
		/// </summary>
		/// <param name="doc">The HTML document.</param>
		/// <param name="xpath">The XPath expression.</param>
		/// <returns>An enumerable of matching HTML nodes.</returns>
		protected IEnumerable<HtmlNode> SafeSelectNodes(HtmlDocument doc, string xpath)
		{
			return doc.DocumentNode.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>();
		}

		private string ExtractTitle(HtmlDocument doc)
		{
			return doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;
		}

		private string ExtractHtmlContent(HtmlDocument doc)
		{
			return doc.DocumentNode.SelectSingleNode("//body")?.InnerHtml ?? string.Empty;
		}

		[GeneratedRegex(@"(\r\n|\n)+")]
		private static partial Regex NewlinesRegex();

		[GeneratedRegex(@"[ \t]+")]
		private static partial Regex SpacesRegex();
	}

	/// <summary>
	/// Represents the interface for a text extractor.
	/// </summary>
	public interface ITextExtractor
	{
		/// <summary>
		/// Extracts the content from the provided HTML document.
		/// </summary>
		/// <param name="doc">The HTML document to extract content from.</param>
		/// <returns>The extracted page content.</returns>
		PageContent ExtractPageContent(HtmlDocument doc);
	}
}