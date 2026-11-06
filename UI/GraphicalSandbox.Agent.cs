using System.Drawing;
using System.Drawing.Imaging;

namespace Rimtera.UI;

public static partial class GraphicalSandbox
{
    private sealed partial class GameForm
    {
        /// <summary>The play bridge's screenshot: the game canvas as a PNG (docs/AGENT_PLAY.md). Called on the window's own thread.</summary>
        private byte[]? CaptureCanvasPng()
        {
            if (_canvas.Width <= 0 || _canvas.Height <= 0)
                return null;
            using var bitmap = new Bitmap(_canvas.Width, _canvas.Height, PixelFormat.Format32bppArgb);
            try
            {
                _canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, _canvas.Size));
            }
            catch (Exception)
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(_canvas.PointToScreen(Point.Empty), Point.Empty, _canvas.Size);
            }
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }
}
