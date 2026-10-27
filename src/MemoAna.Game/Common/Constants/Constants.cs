using System;
using System.Collections.Generic;
using System.Text;

namespace MemoAna.Common.Constants;

public static class Constants
{
    public static class Directories
    {
        public static string BasePath => 
            DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS 
            ? FileSystem.AppDataDirectory 
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".memoana");

        public static string Logs => $"{BasePath}/logs";
        public static string Database => $"{BasePath}/database";
    }
}
