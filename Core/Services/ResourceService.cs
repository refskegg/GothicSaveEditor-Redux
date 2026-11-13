using System;
using System.Windows;

namespace GothicSaveEditor.Core.Services
{
    public static class ResourceServices
    {
        public static string GetString(string resourceName)
        {
            try
            {
                var res = Application.Current?.TryFindResource(resourceName);
                return res?.ToString() ?? resourceName;
            }
            catch
            {
                return "";
            }
        }
    }
}
