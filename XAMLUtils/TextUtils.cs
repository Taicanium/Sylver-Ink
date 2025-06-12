using System.Windows.Documents;
using System.Windows.Markup;

namespace SylverInk.XAMLUtils;

/// <summary>
/// Static functions serving conversion needs across FlowDocument objects, Xaml markup data, and unformatted plaintext.
/// </summary>
public static class TextUtils
{
	public static string FlowDocumentToPlaintext(FlowDocument? document)
	{
		try
		{
			if (document is null)
				return string.Empty;

			if (!document.IsInitialized)
				return string.Empty;

			var begun = false;
			var content = string.Empty;
			var pointer = document.ContentStart;

			while (pointer is not null && pointer.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.None)
			{
				switch (pointer.GetPointerContext(LogicalDirection.Forward))
				{
					case TextPointerContext.Text:
						var runText = pointer.GetTextInRun(LogicalDirection.Forward);

						// Xaml escape sequences aren't handled by XamlReader.Parse, which is very frustrating.
						runText = runText.Replace("{}{", "{");

						content += runText;
						pointer = pointer.GetPositionAtOffset(pointer.GetTextRunLength(LogicalDirection.Forward));
						break;
					case TextPointerContext.ElementStart:
						if (pointer.GetAdjacentElement(LogicalDirection.Forward) is Paragraph)
						{
							if (begun)
								content += "\r\n\r\n";
							begun = true;
						}

						if (pointer.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
							content += "\r\n";

						pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
						break;
					default:
						pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
						break;
				}
			}

			return content.Trim();
		}
		catch
		{
			return string.Empty;
		}
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

	public static FlowDocument XamlToFlowDocument(string xaml)
	{
		if (string.IsNullOrWhiteSpace(xaml))
			return new();

		xaml = xaml.Replace("{}{", "{");

		return (FlowDocument)XamlReader.Parse(xaml);
	}

	public static string XamlToPlaintext(string xaml) => FlowDocumentToPlaintext(XamlToFlowDocument(xaml));
}
