using Microsoft.AspNetCore.Http;

namespace Inventory.Api.Infrastructure;

/// <summary>
/// مسیرهای فیزیکی خصوصی زیر wwwroot که هرگز نباید توسط StaticFiles سرو شوند.
/// بررسی segment-aware است تا مسیرهای عمومی مشابه (مثل innerletter-public) مسدود نشوند.
/// </summary>
public static class ProtectedStaticFilePaths
{
    private static readonly PathString[] BlockedRoots =
    {
        new("/SecureFiles"),
        new("/uploads/innerletter"),
        new("/فایل های صادره"),
        new("/%D9%81%D8%A7%DB%8C%D9%84%20%D9%87%D8%A7%DB%8C%20%D8%B5%D8%A7%D8%AF%D8%B1%D9%87"),
        new("/فایل های ایمیل"),
        new("/%D9%81%D8%A7%DB%8C%D9%84%20%D9%87%D8%A7%DB%8C%20%D8%A7%DB%8C%D9%85%DB%8C%D9%84")
    };

    public static bool IsBlocked(PathString path)
    {
        foreach (var root in BlockedRoots)
        {
            if (path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
