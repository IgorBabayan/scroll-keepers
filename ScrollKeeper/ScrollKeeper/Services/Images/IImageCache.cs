using System.Windows.Media;

namespace ScrollKeeper.Services.Images;

/// <summary>Downloads web images once, keeps them on disk and in memory.</summary>
public interface IImageCache
{
    /// <summary>
    /// Returns the image decoded to <paramref name="decodeWidth"/> pixels wide (0 = original size),
    /// or <c>null</c> if it cannot be downloaded or decoded. Never throws.
    /// </summary>
    Task<ImageSource?> GetAsync(string url, int decodeWidth);
}
