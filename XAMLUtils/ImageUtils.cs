using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SylverInk.XAMLUtils;

public static partial class ImageUtils
{
	public static Image DecodeEmbed(string data)
	{
		using MemoryStream stream = new(Convert.FromBase64String(data));
		var img = new Image();

		PngBitmapDecoder decoder = new(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
		BitmapSource source = decoder.Frames[0];

		// Always Bgr24?
		byte[] pixels = new byte[source.PixelWidth * 4 * source.PixelHeight];
		source.CopyPixels(pixels, source.PixelWidth * 4, 0);

		img.BeginInit();
		img.Source = BitmapSource.Create(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, source.Format, source.Palette, pixels, source.PixelWidth * 4);
		img.Stretch = Stretch.None;
		img.Margin = new Thickness(5, 0, 5, 0);
		img.EndInit();

		return img;
	}

	public static byte[] EncodeEmbed(Image img)
	{
		using MemoryStream stream = new();

		PngBitmapEncoder encoder = new()
		{
			Interlace = PngInterlaceOption.On
		};

		encoder.Frames.Add(BitmapFrame.Create(img.Source as BitmapSource));
		encoder.Save(stream);
		return stream.ToArray();
	}
}
