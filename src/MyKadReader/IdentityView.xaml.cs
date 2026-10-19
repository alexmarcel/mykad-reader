using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MyKadReader.Core;

namespace MyKadReader;

public partial class IdentityView : UserControl
{
    public IdentityView() { InitializeComponent(); }
    public static BitmapSource DecodePhoto(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 400; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
        return bitmap;
    }
    public void ShowIdentity(Identity? identity)
    {
        FullName.Text = identity?.Name ?? "Unavailable";
        Number.Text = identity?.Number ?? "—";
        Dob.Text = identity?.BirthDate ?? "Unavailable";
        Gender.Text = identity?.Gender ?? "Unavailable";
        Address.Text = identity?.Address ?? "Unavailable";
        Availability.Text = identity?.Availability ?? "Read a card or select a saved record to view details.";
        Portrait.Source = null; PhotoStatus.Text = "Photo unavailable";
        if (identity?.Photo is { } photo)
        {
            try { Portrait.Source = DecodePhoto(photo); PhotoStatus.Text = "Chip portrait"; }
            catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
            { PhotoStatus.Text = "Photo could not be decoded"; }
        }
    }
}
