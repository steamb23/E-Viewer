﻿using Opportunity.MvvmUniverse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ExClient.Api;
using Windows.Foundation;
using System.Runtime.InteropServices.WindowsRuntime;
using ExClient.Internal;
using HtmlAgilityPack;
using Opportunity.MvvmUniverse.Collections;

namespace ExClient.Galleries.Renaming
{
    public sealed class RenameInfo : ObservableObject
    {
        public static IAsyncOperation<RenameInfo> FetchAsync(GalleryInfo galleryInfo)
        {
            return AsyncInfo.Run(async token =>
            {
                var r = new RenameInfo(galleryInfo);
                var u = r.UpdateAsync();
                token.Register(u.Cancel);
                await u;
                return r;
            });
        }

        private RenameInfo(GalleryInfo galleryInfo) => this.GalleryInfo = galleryInfo;

        public GalleryInfo GalleryInfo { get; }

        public ObservableListView<RenameRecord> RomanRecords => this.rmnRecords.AsReadOnly();
        public ObservableListView<RenameRecord> JapaneseRecords => this.jpnRecords.AsReadOnly();

        private readonly ObservableList<RenameRecord> rmnRecords = new ObservableList<RenameRecord>();
        private readonly ObservableList<RenameRecord> jpnRecords = new ObservableList<RenameRecord>();

        private string originalRmn;
        public string OriginalRomanTitle
        {
            get => this.originalRmn;
            private set => Set(ref this.originalRmn, value);
        }

        private string originalJpn;
        public string OriginalJapaneseTitle
        {
            get => this.originalJpn;
            private set => Set(ref this.originalJpn, value);
        }

        private int currentRmn = -1;
        public int CurrentRomanID
        {
            get => this.currentRmn;
            private set => Set(ref this.currentRmn, value);
        }

        private int currentJpn = -1;
        public int CurrentJapaneseID
        {
            get => this.currentJpn;
            private set => Set(ref this.currentJpn, value);
        }

        public IAsyncAction UpdateAsync()
        {
            return AsyncInfo.Run(async token =>
            {
                var uri = new Uri($"https://e-hentai.org/gallerypopups.php?gid={this.GalleryInfo.ID}&t={this.GalleryInfo.Token.ToTokenString()}&act=rename");
                var get = Client.Current.HttpClient.GetDocumentAsync(uri);
                token.Register(get.Cancel);
                var doc = await get;
                analyze(doc);
            });
        }

        private void analyze(HtmlDocument doc)
        {
            if (doc.DocumentNode.ChildNodes.Count == 1 && doc.DocumentNode.FirstChild.NodeType == HtmlNodeType.Text)
                throw new InvalidOperationException(doc.DocumentNode.FirstChild.InnerText);
            var tables = doc.DocumentNode.Descendants("table").ToList();
            IReadOnlyList<RenameRecord> romanRec, japaneseRec;
            (this.CurrentRomanID, romanRec, this.OriginalRomanTitle) = analyzeTable(tables[0]);
            (this.CurrentJapaneseID, japaneseRec, this.OriginalJapaneseTitle) = analyzeTable(tables[1]);
            this.rmnRecords.Update(romanRec);
            this.jpnRecords.Update(japaneseRec);

            (int currentID, IReadOnlyList<RenameRecord> records, string original) analyzeTable(HtmlNode tableNode)
            {
                var original = default(string);
                var text = tableNode.Element("tr").LastChild.FirstChild;
                if (text.NodeType == HtmlNodeType.Text)
                    original = text.InnerText.DeEntitize();

                var trecords = tableNode.Elements("tr").Skip(1).ToList();
                var records = new List<RenameRecord>();
                var currentID = -1;
                foreach (var rec in trecords)
                {
                    var input = rec.Descendants("input").First();
                    // 0 for new; -1 for blank vote
                    var recId = input.GetAttributeValue("value", -1);
                    var check = !string.IsNullOrWhiteSpace(input.GetAttributeValue("checked", null));
                    if (check)
                        currentID = recId;
                    if (recId > 0)
                    {
                        var powStr = rec.ChildNodes[1].InnerText;
                        var power = int.Parse(powStr.Substring(0, powStr.Length - 1));
                        var title = rec.ChildNodes[2].InnerText.DeEntitize();
                        records.Add(new RenameRecord(recId, title, power));
                    }
                }
                return (currentID, records, original);
            }
        }
    }
}
