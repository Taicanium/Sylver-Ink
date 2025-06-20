using System.Text;
using System.Windows.Documents;
using System.Windows.Markup;

namespace SylverInk.XAMLUtils;

/// <summary>
/// Static functions serving conversion needs across FlowDocument objects, Xaml markup data, and unformatted plaintext.
/// </summary>
public static class TextUtils
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

		xaml = xaml.Replace("{}{", "{");

		return (FlowDocument)XamlReader.Parse(xaml);
	}

	public static string XamlToPlaintext(string xaml) => FlowDocumentToPlaintext(XamlToFlowDocument(xaml));
}
