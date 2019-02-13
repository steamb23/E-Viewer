﻿using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml.Media.Imaging;

namespace ExClient
{
    internal static class StorageHelper
    {
        static StorageHelper()
        {
            var ignore = SetImageFolderAsync(null);
        }

        public static StorageFolder ImageFolder { get; private set; }

        public static async Task SetImageFolderAsync(StorageFolder folder)
        {
            if (folder != null)
            {
                ImageFolder = folder;
                return;
            }

            var old = ImageFolder;
            var temp = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync("Images", CreationCollisionOption.OpenIfExists);
            if (old == ImageFolder)
                ImageFolder = temp;
        }
    }

}
