using System.Text;

namespace GothicSaveEditor.Core.Utils;

public static class EncodingHelper
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (!_registered)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _registered = true;
        }
    }

    public static Encoding GetEncoding(int codePage = 1252)
    {
        EnsureRegistered();
        return Encoding.GetEncoding(codePage);
    }
}
