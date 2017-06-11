﻿using Opportunity.MvvmUniverse.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml.Data;

namespace ExClient.Galleries
{
    internal abstract class GalleryList<TGallery, TModel> : ObservableCollection<Gallery>, IItemsRangeInfo
         where TGallery : Gallery
    {
        protected static Gallery DefaultGallery
        {
            get;
        } = new Gallery(-1, null, "0", LocalizedStrings.Resources.DefaultTitle, "", "", "ms-appx:///", LocalizedStrings.Resources.DefaultUploader, "0", "0", 0, false, "2.5", "0", new string[0]);

        public bool IsEmpty => this.Count == 0;

        private int loadedCount;

        internal GalleryList(List<TModel> models)
            : base(Enumerable.Repeat(DefaultGallery, models.Count))
        {
            this.models = models;
        }

        private List<TModel> models;

        protected override void ClearItems()
        {
            this.models.Clear();
            this.loadedCount = 0;
            base.ClearItems();
            RaisePropertyChanged(nameof(IsEmpty));
        }

        protected override void RemoveItem(int index)
        {
            this.models.RemoveAt(index);
            if (this[index] != DefaultGallery)
                this.loadedCount--;
            base.RemoveItem(index);
            RaisePropertyChanged(nameof(IsEmpty));
        }

        protected override void RemoveItems(int index, int count)
        {
            this.models.RemoveRange(index, count);
            for (var i = 0; i < count; i++)
            {
                if (this[index + i] != DefaultGallery)
                    this.loadedCount--;
            }
            base.RemoveItems(index, count);
            RaisePropertyChanged(nameof(IsEmpty));
        }

        void IItemsRangeInfo.RangesChanged(ItemIndexRange visibleRange, IReadOnlyList<ItemIndexRange> trackedItems)
        {
            if (this.loadedCount == this.models.Count)
            {
                return;
            }
            var ranges = trackedItems.Concat(Enumerable.Repeat(visibleRange, 1)).ToList();
            var start = ranges.Min(r => r.FirstIndex);
            var end = ranges.Max(r => r.LastIndex) + 1;
            if (start < 0)
                start = 0;
            if (end > this.Count)
                end = this.Count;
            for (var i = start; i < end; i++)
            {
                if (this[i] != DefaultGallery)
                    continue;
                this[i] = Load(this.models[i]);
                this.models[i] = default(TModel);
                this.loadedCount++;
            }
        }

        protected abstract TGallery Load(TModel model);

        void IDisposable.Dispose() { this.Clear(); }
    }
}
