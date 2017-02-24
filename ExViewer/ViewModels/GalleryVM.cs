﻿using ExClient;
using ExViewer.Settings;
using ExViewer.Views;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using GalaSoft.MvvmLight.Threading;
using Newtonsoft.Json;
using System;
using static System.Runtime.InteropServices.WindowsRuntime.AsyncInfo;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Foundation;
using Windows.System;
using System.Collections.ObjectModel;
using Windows.Storage.Streams;
using Windows.Storage;
using Windows.Graphics.Imaging;
using ExClient.Api;

namespace ExViewer.ViewModels
{
    public enum OperationState
    {
        NotStarted,
        Started,
        Failed,
        Completed
    }

    public class TagList : List<Tag>
    {
        public TagList(IEnumerable<Tag> items) : base(items) { }

        public Namespace Namespace => this.FirstOrDefault()?.Namespace ?? Namespace.Misc;
    }

    public class GalleryVM : ViewModelBase
    {
        private static CacheStorage<GalleryInfo, GalleryVM> Cache
        {
            get;
        } = new CacheStorage<GalleryInfo, GalleryVM>(gi => Run(async token => new GalleryVM((await Gallery.FetchGalleriesAsync(new[] { gi })).Single())), 25, new GalleryInfoComparer());

        private class GalleryInfoComparer : IEqualityComparer<GalleryInfo>
        {
            public bool Equals(GalleryInfo x, GalleryInfo y)
            {
                return x.Id == y.Id;
            }

            public int GetHashCode(GalleryInfo obj)
            {
                return obj.Id.GetHashCode();
            }
        }

        public static GalleryVM GetVM(Gallery gallery)
        {
            GalleryVM vm;
            var gi = new GalleryInfo(gallery.Id, gallery.Token);
            if(Cache.TryGet(gi, out vm))
            {
                vm.Gallery = gallery;
                if(gallery.Count <= vm.currentIndex)
                    vm.currentIndex = -1;
            }
            else
            {
                vm = new GalleryVM(gallery);
                Cache.Add(gi, vm);
            }
            return vm;
        }

        public static IAsyncOperation<GalleryVM> GetVMAsync(long parameter)
        {
            return Cache.GetAsync(new GalleryInfo(parameter, null));
        }

        public GalleryImage GetCurrent()
        {
            try
            {
                return Gallery[currentIndex];
            }
            catch(ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        private GalleryVM()
        {
            Share = new RelayCommand<GalleryImage>(async image =>
            {
                if(Helpers.ShareHandler.IsShareSupported)
                {
                    Helpers.ShareHandler.Share(async (s, e) =>
                    {
                        var deferral = e.Request.GetDeferral();
                        try
                        {
                            e.Request.Data.Properties.Title = gallery.GetDisplayTitle();
                            e.Request.Data.Properties.Description = gallery.GetSecondaryTitle();
                            if(image == null)
                            {
                                var ms = new InMemoryRandomAccessStream();
                                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
                                encoder.SetSoftwareBitmap(gallery.Thumb);
                                await encoder.FlushAsync();
                                e.Request.Data.Properties.Thumbnail = RandomAccessStreamReference.CreateFromStream(ms);
                                var firstImage = gallery.FirstOrDefault()?.ImageFile;
                                if(firstImage != null)
                                    e.Request.Data.SetBitmap(RandomAccessStreamReference.CreateFromFile(firstImage));
                                e.Request.Data.Properties.ContentSourceWebLink = gallery.GalleryUri;
                                e.Request.Data.SetWebLink(gallery.GalleryUri);
                            }
                            else
                            {
                                if(image.ImageFile != null)
                                {
                                    e.Request.Data.SetBitmap(RandomAccessStreamReference.CreateFromFile(image.ImageFile));
                                    e.Request.Data.Properties.Thumbnail = RandomAccessStreamReference.CreateFromStream(await image.ImageFile.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem));
                                }
                                e.Request.Data.Properties.ContentSourceWebLink = image.PageUri;
                                e.Request.Data.SetWebLink(image.PageUri);
                            }
                        }
                        finally
                        {
                            deferral.Complete();
                        }
                    });
                }
                else
                {
                    if(image == null)
                        await Launcher.LaunchUriAsync(gallery.GalleryUri);
                    else
                        await Launcher.LaunchUriAsync(image.PageUri);
                }
            }, image => gallery != null);
            OpenInExplorer = new RelayCommand(async () => await Launcher.LaunchFolderAsync(gallery.GalleryFolder), () => gallery != null);
            Save = new RelayCommand(() =>
            {
                var task = gallery.SaveGalleryAsync(SettingCollection.Current.GetStrategy());
                SaveStatus = OperationState.Started;
                task.Progress = (sender, e) =>
                {
                    SaveProgress = e.ImageLoaded / (double)e.ImageCount;
                };
                task.Completed = (sender, e) =>
                {
                    switch(e)
                    {
                    case AsyncStatus.Canceled:
                    case AsyncStatus.Error:
                        SaveStatus = OperationState.Failed;
                        RootControl.RootController.SendToast(sender.ErrorCode, null);
                        break;
                    case AsyncStatus.Completed:
                        SaveStatus = OperationState.Completed;
                        break;
                    case AsyncStatus.Started:
                        SaveStatus = OperationState.Started;
                        break;
                    }
                    SaveProgress = 1;
                };
            }, () => SaveStatus != OperationState.Started && !(Gallery is SavedGallery));
            OpenImage = new RelayCommand<GalleryImage>(image =>
            {
                CurrentIndex = image.PageId - 1;
                RootControl.RootController.Frame.Navigate(typeof(ImagePage), gallery.Id);
            });
            LoadOriginal = new RelayCommand<GalleryImage>(async image =>
            {
                image.PropertyChanged += Image_PropertyChanged;
                await image.LoadImageAsync(true, ConnectionStrategy.AllFull, false);
                image.PropertyChanged -= Image_PropertyChanged;
            }, image => image != null && !image.OriginalLoaded);
            ReloadImage = new RelayCommand<GalleryImage>(async image =>
            {
                image.PropertyChanged += Image_PropertyChanged;
                if(image.OriginalLoaded)
                    await image.LoadImageAsync(true, ConnectionStrategy.AllFull, false);
                else
                    await image.LoadImageAsync(true, SettingCollection.Current.GetStrategy(), false);
                image.PropertyChanged -= Image_PropertyChanged;
            }, image => image != null);
            TorrentDownload = new RelayCommand<TorrentInfo>(async torrent =>
            {
                RootControl.RootController.SendToast(LocalizedStrings.Resources.Views.GalleryPage.TorrentDownloading, null);
                try
                {
                    var file = await torrent.LoadTorrentAsync();
                    await Launcher.LaunchFileAsync(file);
                }
                catch(Exception ex)
                {
                    RootControl.RootController.SendToast(ex, typeof(GalleryPage));
                }
            }, torrent => torrent != null && torrent.TorrentUri != null);
            GoToDefinition = new RelayCommand<Tag>(async tag =>
            {
                await Launcher.LaunchUriAsync(tag.TagDefinitionUri);
            }, tag => tag != null);
            SearchTag = new RelayCommand<Tag>(tag =>
            {
                var vm = SearchVM.GetVM(tag.Search());
                RootControl.RootController.Frame.Navigate(typeof(SearchPage), vm.SearchQuery);
            }, tag => tag != null);
        }

        private void Image_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(GalleryImage.OriginalLoaded))
                LoadOriginal.RaiseCanExecuteChanged();
        }

        private GalleryVM(Gallery gallery)
            : this()
        {
            this.Gallery = gallery;
        }

        public RelayCommand<GalleryImage> Share
        {
            get;
        }

        public RelayCommand OpenInExplorer
        {
            get;
        }

        public RelayCommand Save
        {
            get;
        }

        public RelayCommand<GalleryImage> OpenImage
        {
            get;
        }

        public RelayCommand<GalleryImage> LoadOriginal
        {
            get;
        }

        public RelayCommand<GalleryImage> ReloadImage
        {
            get;
        }

        public RelayCommand<TorrentInfo> TorrentDownload
        {
            get;
        }

        public RelayCommand<Tag> GoToDefinition
        {
            get;
        }

        public RelayCommand<Tag> SearchTag
        {
            get;
        }

        private Gallery gallery;

        public Gallery Gallery
        {
            get
            {
                return gallery;
            }
            private set
            {
                if(gallery != null)
                    gallery.LoadMoreItemsException -= Gallery_LoadMoreItemsException;
                Set(ref gallery, value);
                if(gallery != null)
                    gallery.LoadMoreItemsException += Gallery_LoadMoreItemsException;
                Save.RaiseCanExecuteChanged();
                Share.RaiseCanExecuteChanged();
                OpenInExplorer.RaiseCanExecuteChanged();
                Torrents = null;
            }
        }

        private void Gallery_LoadMoreItemsException(IncrementalLoadingCollection<GalleryImage> sender, LoadMoreItemsExceptionEventArgs args)
        {
            RootControl.RootController.SendToast(args.Exception, typeof(GalleryPage));
            args.Handled = true;
        }

        private int currentIndex = -1;

        public int CurrentIndex
        {
            get
            {
                return currentIndex;
            }
            set
            {
                Set(ref currentIndex, value);
            }
        }

        private string currentInfo;

        public string CurrentInfo
        {
            get
            {
                return currentInfo;
            }
            private set
            {
                DispatcherHelper.CheckBeginInvokeOnUI(() => Set(ref currentInfo, value));
            }
        }

        public IAsyncAction RefreshInfoAsync()
        {
            return Run(async token =>
            {
                var current = GetCurrent();
                if(current?.ImageFile == null)
                {
                    CurrentInfo = LocalizedStrings.Resources.Views.ImagePage.ImageFileInfoDefault;
                    return;
                }
                var prop = await current.ImageFile.GetBasicPropertiesAsync();
                var imageProp = await current.ImageFile.Properties.GetImagePropertiesAsync();
                CurrentInfo = string.Format(LocalizedStrings.Resources.Views.ImagePage.ImageFileInfo, current.ImageFile.Name,
                    Converters.ByteSizeToStringConverter.ByteSizeToString(prop.Size, Converters.UnitPrefix.Binary),
                    imageProp.Width.ToString(), imageProp.Height.ToString());
            });
        }

        private OperationState saveStatus;

        public OperationState SaveStatus
        {
            get
            {
                return saveStatus;
            }
            set
            {
                DispatcherHelper.CheckBeginInvokeOnUI(() =>
                {
                    Set(ref saveStatus, value);
                    Save.RaiseCanExecuteChanged();
                });
            }
        }

        private double saveProgress;

        public double SaveProgress
        {
            get
            {
                return saveProgress;
            }
            set
            {
                DispatcherHelper.CheckBeginInvokeOnUI(() => Set(ref saveProgress, value));
            }
        }

        #region Comments

        public IAsyncAction LoadComments()
        {
            return Run(async token =>
            {
                try
                {
                    await gallery.LoadCommentsAsync();
                }
                catch(Exception ex)
                {
                    RootControl.RootController.SendToast(ex, typeof(GalleryPage));
                }
            });
        }

        #endregion Comments

        #region Torrents

        public IAsyncAction LoadTorrents()
        {
            return Run(async token =>
            {
                try
                {
                    Torrents = await gallery.LoadTorrnetsAsync();
                }
                catch(Exception ex)
                {
                    RootControl.RootController.SendToast(ex, typeof(GalleryPage));
                }
            });
        }

        private ReadOnlyCollection<TorrentInfo> torrents;

        public ReadOnlyCollection<TorrentInfo> Torrents
        {
            get
            {
                return torrents;
            }
            private set
            {
                torrents = value;
                DispatcherHelper.CheckBeginInvokeOnUI(() =>
                {
                    RaisePropertyChanged(nameof(Torrents));
                    RaisePropertyChanged(nameof(TorrentCount));
                });
            }
        }

        public int? TorrentCount => torrents?.Count ?? (gallery is SavedGallery ? null : gallery?.TorrentCount);

        #endregion Torrents
    }
}
