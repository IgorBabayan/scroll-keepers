using System.Windows.Media;

namespace ScrollKeeper.Services.Images;

/// <summary>Downloads web images once, keeps them on disk and in memory.</summary>
public interface IImageCache
{
    /// <summary>
    /// Returns the image decoded so its shorter side is <paramref name="decodeSize"/> pixels (0 = original size;
    /// smaller images are never upscaled), or <c>null</c> if it cannot be downloaded or decoded. Never throws.
    /// </summary>
    Task<ImageSource?> GetAsync(string url, int decodeSize);
}
