﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Web.Http;
using static System.Runtime.InteropServices.WindowsRuntime.AsyncInfo;

namespace EhWikiClient
{
    public class Client : IDisposable
    {
        public static IAsyncOperation<Client> CreateAsync()
        {
            return Run(async token =>
            {
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("EhWiki.json", CreationCollisionOption.OpenIfExists);
                var dic = JsonConvert.DeserializeObject<List<Record>>(await FileIO.ReadTextAsync(file));
                if(dic != null)
                    return new Client(dic.ToDictionary(rec => rec.Title, StringComparer.OrdinalIgnoreCase));
                return new Client(null);
            });
        }

        private Client(Dictionary<string, Record> dic)
        {
            saveTimer = new System.Threading.Timer(saveTimerCallBack, this, 30000, 30000);
            this.dic = dic ?? new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
        }

        private HttpClient http = new HttpClient();

        private System.Threading.Timer saveTimer;

        private static async void saveTimerCallBack(object state)
        {
            try
            {
                var client = (Client)state;
                var file = await ApplicationData.Current.LocalFolder.CreateFileAsync("EhWiki.json", CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, JsonConvert.SerializeObject(client.dic.Values.Where(r => r != null)));
            }
            catch
            {
            }
        }

        private Dictionary<string, Record> dic;

        public Record Get(string title)
        {
            var r = (Record)null;
            if(dic.TryGetValue(title, out r))
                return r;
            FetchAsync(title).Completed = (s, e) =>
            {
                if(e == AsyncStatus.Error)
                    s.ErrorCode.ToString();
            };
            return null;
        }

        public IAsyncOperation<Record> GetAsync(string title)
        {
            var r = (Record)null;
            if(dic.TryGetValue(title, out r))
                return Task.Run(() => r).AsAsyncOperation();
            return FetchAsync(title);
        }

        private static readonly Uri apiUri = new Uri("https://ehwiki.org/api.php");

        public IAsyncOperation<Record> FetchAsync(string title)
        {
            return Run(async token =>
            {
                var res = await http.PostAsync(apiUri, new HttpFormUrlEncodedContent(getRequestParameters(title)));
                var resStr = await res.Content.ReadAsStringAsync();
                var record = Record.Load(resStr);
                dic[title] = record;
                return record;
            });
        }

        private IEnumerable<KeyValuePair<string, string>> getRequestParameters(string title)
        {
            //https://ehwiki.org/api.php?action=parse&page={pageName}&prop=text&format=jsonfm&utf8=
            yield return new KeyValuePair<string, string>("action", "parse");
            yield return new KeyValuePair<string, string>("page", title);
            yield return new KeyValuePair<string, string>("prop", "text");
            yield return new KeyValuePair<string, string>("format", "json");
            yield return new KeyValuePair<string, string>("utf8", "");
        }

        #region IDisposable Support
        private bool disposedValue = false; // 要检测冗余调用

        protected virtual void Dispose(bool disposing)
        {
            if(!disposedValue)
            {
                if(disposing)
                {
                    http.Dispose();
                    saveTimer.Dispose();
                }
                disposedValue = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
