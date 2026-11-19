using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ZephyrRemote;

public class ScreenCapture
{
    private static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    public int Count => Screen.AllScreens.Length;

    public byte[] Capture(int index, int maxWidth, long quality)
    {
        Screen[] screens = Screen.AllScreens;
        if (index < 0 || index >= screens.Length)
            return Array.Empty<byte>();

        Rectangle bounds = screens[index].Bounds;
        using Bitmap full = new(bounds.Width, bounds.Height);
        using (Graphics g = Graphics.FromImage(full))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

        int width = Math.Min(maxWidth, bounds.Width);
        int height = bounds.Height * width / bounds.Width;
        using Bitmap small = new(width, height);
        using (Graphics g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(full, 0, 0, width, height);
        }

        EncoderParameters parameters = new(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        using MemoryStream stream = new();
        small.Save(stream, Jpeg, parameters);
        return stream.ToArray();
    }
}
