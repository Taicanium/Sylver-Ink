using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using static SylverInk.XAMLUtils.ImageUtils;

namespace SylverInk.XAMLUtils;

/// <summary>
/// Static functions serving conversion needs across FlowDocument objects, Xaml markup data, and unformatted plaintext.
/// </summary>
public static partial class TextUtils
{
	public static string FlowDocumentPreview(FlowDocument? document)
	{
		if (document is null)
			return string.Empty;

		if (!document.IsInitialized)
			return string.Empty;

		StringBuilder content = new();
		var pointer = document.ContentStart;

		while (pointer is not null && document.ContentStart.GetOffsetToPosition(pointer) < 250)
			pointer = TranslatePointer(pointer, ref content);

		return content.ToString().Trim();
	}

	public static string FlowDocumentToPlaintext(FlowDocument? document)
	{
		if (document is null)
			return string.Empty;

		if (!document.IsInitialized)
			return string.Empty;

		StringBuilder content = new();
		var pointer = document.ContentStart;

		while (pointer is not null)
			pointer = TranslatePointer(pointer, ref content);

		return content.ToString().Trim();
	}

	public static string FlowDocumentToXaml(FlowDocument document)
	{
		var content = XamlWriter.Save(document);
		content = content.Replace("{}{", "{");

		var pointer = document.ContentStart;

		while (pointer is not null)
		{
			if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart
				&& pointer.GetAdjacentElement(LogicalDirection.Forward) is BlockUIContainer container
				&& container.Child is Image img)
			{
				var embed = EncodeEmbed(img);
				var match = ContainerRegex().Match(content);

				content = $"{content[..match.Index]}<Paragraph Tag=\"base64\">{Convert.ToBase64String(embed)}</Paragraph>{content[(match.Index + match.Length)..]}";
			}

			pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
		}

		return content;
	}

	public static FlowDocument PlaintextToFlowDocument(FlowDocument document, string content)
	{
		document.Blocks.Clear();
		TextPointer pointer = document.ContentStart;
		var lineSplit = content.Replace("\r", string.Empty).Split('\n') ?? [];
		for (int i = 0; i < lineSplit.Length; i++)
		{
			var line = lineSplit[i];
			if (string.IsNullOrEmpty(line))
				continue;

			pointer.InsertTextInRun(line);
			while (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.ElementEnd)
				pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);

			if (i >= lineSplit.Length - 1)
				continue;

			if (string.IsNullOrEmpty(lineSplit[i + 1]))
				pointer = pointer.InsertParagraphBreak();
			else
				pointer = pointer.InsertLineBreak();
		}
		return document;
	}

	public static string PlaintextToXaml(string content) => FlowDocumentToXaml(PlaintextToFlowDocument(new(), content));

	private static TextPointer? TranslatePointer(TextPointer pointer, ref StringBuilder content)
	{
		switch (pointer.GetPointerContext(LogicalDirection.Forward))
		{
			case TextPointerContext.None:
				return null;
			case TextPointerContext.Text:
				var runText = pointer.GetTextInRun(LogicalDirection.Forward);

				// Xaml escape sequences aren't handled by XamlReader.Parse, which is very frustrating.
				content.Append(runText.Replace("{}{", "{"));

				return pointer.GetPositionAtOffset(pointer.GetTextRunLength(LogicalDirection.Forward));
			case TextPointerContext.ElementStart:
				var element = pointer.GetAdjacentElement(LogicalDirection.Forward);

				if (element is Paragraph && content.Length > 0)
				{
					content.AppendLine();
					content.AppendLine();
				}
				else if (element is LineBreak)
					content.AppendLine();

				return pointer.GetNextContextPosition(LogicalDirection.Forward);
			default:
				return pointer.GetNextContextPosition(LogicalDirection.Forward);
		}
	}

	public static FlowDocument XamlToFlowDocument(string xaml)
	{
		if (string.IsNullOrWhiteSpace(xaml))
			return new();

		var document = (FlowDocument)XamlReader.Parse(xaml.Replace("{}{", "{"));
		var pointer = document.ContentStart;

		while (pointer is not null)
		{
			if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart
				&& pointer.GetAdjacentElement(LogicalDirection.Forward) is Paragraph paragraph
				&& paragraph.Tag is "base64")
			{
				while (string.IsNullOrEmpty(pointer.GetTextInRun(LogicalDirection.Forward)))
					pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);

				var img = DecodeEmbed(pointer.GetTextInRun(LogicalDirection.Forward));

				while (pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.ElementStart)
					pointer = pointer.GetNextContextPosition(LogicalDirection.Backward);

				pointer = pointer.GetNextContextPosition(LogicalDirection.Backward) ?? document.ContentStart;

				BlockUIContainer container = new(img);
				document.Blocks.InsertBefore(paragraph, container);
				document.Blocks.Remove(paragraph);
			}

			pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
		}

		return document;
	}

	public static string XamlToPlaintext(string xaml) => FlowDocumentToPlaintext(XamlToFlowDocument(xaml));

	[GeneratedRegex(@"<BlockUIContainer.*?</BlockUIContainer>")]
	private static partial Regex ContainerRegex();
}
