using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Forms;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using LeiKaiFeng.Http;

namespace yande.re
{

    sealed class DeleteRepeatFile
    {
        static string GetStreamHashCode(Stream stream)
        {
            HashAlgorithm hashAlgorithm = SHA256.Create();

            byte[] hashCode = hashAlgorithm.ComputeHash(stream);

            return BitConverter.ToString(hashCode).Replace("-", "");

        }

        static Stream Open(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        }

        static string GetFileHashCode(string path)
        {
            using (var stream = Open(path))
            {
                return GetStreamHashCode(stream);
            }            
        }

        public static void Statr(string folderPath)
        {
            var paths = Directory.EnumerateFiles(folderPath);

            var dic = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in paths)
            {
                string hash;
                try
                {
                    hash = GetFileHashCode(path);
                }
                catch (Exception e)
                {
                    
                    continue;
                }

                if (dic.TryGetValue(hash, out var list))
                {
                    list.Add(path);
                }
                else
                {
                    list = new List<string>();

                    list.Add(path);

                    dic[hash] = list;
                }
            }


            foreach (var list in dic.Values)
            {

                for (int i = 1; i < list.Count; i++)
                {
                    File.Delete(list[i]);
                }
            }
        }
    }

    sealed class Http
    {
        const string Konachan_Host = "https://konachan.com";

        const string Yandere_Host = "https://yande.re";


        readonly Regex m_regex = new Regex(@"<a class=""directlink largeimg"" href=""([^""]+)""");

        readonly MHttpClient m_request;

        readonly Uri m_host;

        readonly Func<Task<DateTime>> m_nextPagesFunc;

        public Http(string host, Func<Task<DateTime>> nextPagesFunc, TimeSpan timeOut)
        {
            m_request = GetHttpClient(host);

            m_request.TimeOut = timeOut;

            m_host = GetHost(host);


            m_nextPagesFunc = nextPagesFunc;

        }

        public static string[] GetSource()
        {
            return new string[]
            {
                Yandere_Host,
                Konachan_Host
            };
        }

        static async Task<KeyValuePair<Socket, Stream>> CreateYandereConnect(Uri uri)
        {
            Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            await socket.ConnectAsync(new Uri(Konachan_Host).Host, 443).ConfigureAwait(false);

            SslStream sslStream = new SslStream(new NetworkStream(socket, true), false);

            await sslStream.AuthenticateAsClientAsync(uri.Host).ConfigureAwait(false);

            return new KeyValuePair<Socket, Stream>(socket, sslStream);
        }

        static MHttpClient GetHttpClient(string host)
        {
            if(host == Konachan_Host)
            {
                return new MHttpClient();
            }
            else
            {
                return new MHttpClient(new MHttpClientHandler
                {
                    ConnectCallback = CreateYandereConnect,

                    MaxResponseSize = 1024 * 1024 * 5
                });
            }
        }

        static Uri GetHost(string host)
        {
            if(host == null)
            {
                return new Uri(Yandere_Host);
            }
            else
            {
                string v = GetSource().ToList().Find((s) => s == host);

                if(v == null)
                {
                    return new Uri(Yandere_Host);
                }
                else
                {
                    return new Uri(v);
                }
            }
             
        }


        Uri GetUriPath(DateTime dateTime)
        {
            string s = $"/post/popular_by_day?day={dateTime.Day}&month={dateTime.Month}&year={dateTime.Year}";

            return new Uri(m_host, s);
        }

        List<Uri> ParseUris(string html)
        {
            var match = m_regex.Match(html);

            var list = new List<Uri>();

            while (match.Success)
            {
                try
                {
                    list.Add(new Uri(match.Groups[1].Value));
                }
                catch
                {

                }

                match = match.NextMatch();
            }

            return list;
        }


        public Task<byte[]> GetImageBytesAsync(Uri uri)
        {
            return m_request.GetByteArrayAsync(uri);
        }

        public async Task<List<Uri>> GetUrisAsync()
        {
            DateTime dateTime = await m_nextPagesFunc().ConfigureAwait(false);

            Uri uri = GetUriPath(dateTime);

            string html = await m_request.GetStringAsync(uri).ConfigureAwait(false);

            return ParseUris(html);
        }

    }

    sealed class CreateColl
    {

        static async void GetUris(Http get_content, BlockingCollection<Uri> uris)
        {
            while (true)
            {
                try
                {

                    var list = await get_content.GetUrisAsync().ConfigureAwait(false);

                    foreach (var item in list)
                    {
                        uris.Add(item);
                    }
                }
                catch(Exception e)
                {

                }

            }
        }
     
        static async void GetImage(Http get_content, BlockingCollection<Uri> uris, BlockingCollection<byte[]> imgs)
        {
            while (true)
            {

                
                try
                {
                    Uri uri = uris.Take();

                    byte[] buffer = await get_content.GetImageBytesAsync(uri).ConfigureAwait(false);


                    imgs.Add(buffer);
                }
                catch(Exception e)
                {

                }
            }
        }

        static void GetImage(Http get_content, BlockingCollection<Uri> uris, BlockingCollection<byte[]> imgs, int imgCount)
        {
            foreach (var item in Enumerable.Range(0, imgCount))
            {
                Task.Run(() => GetImage(get_content, uris, imgs));
            }
        }

        public static BlockingCollection<byte[]> Create(Http get_content, int uriCount, int imgCount)
        {
            var uris = new BlockingCollection<Uri>(uriCount);

            var imgs = new BlockingCollection<byte[]>(imgCount);


            Task.Run(() => GetUris(get_content, uris));


            Task.Run(() => GetImage(get_content, uris, imgs, imgCount));

            return imgs;
        }


    }

    sealed class Data
    {
        public Data(byte[] buffer)
        {
            Buffer = buffer;

            ImageSource = ImageSource.FromStream(() => new MemoryStream(Buffer));
        }

        public ImageSource ImageSource { get; }

        public byte[] Buffer { get; }


    }

    public partial class MainPage : ContentPage
    {

       

        const int VIEW_ITEM_SIZE = 6;

        const int ONE_IMAGE_WAIT_TIME = 2;

        const string DATE_SAVE_KEY = "m_fdsfsdewtrehfdfb";

        const string HOST_SAVE_KEY = "m_fsfsfderwre3443534";
        
        const string ROOT_PATH = "/storage/emulated/0/konachan_image";

        readonly TimeSpan m_timeOut = new TimeSpan(0, 1, 0);

        readonly int m_uriload = 32;

        readonly int m_imageload = 16;

        

        readonly ObservableCollection<Data> m_imageSources = new ObservableCollection<Data>();

        DateTime m_dateTime = GetDateTime();



        public MainPage()
        {
            InitializeComponent();

            DeviceDisplay.KeepScreenOn = true;

            SetViewHost();

            SetViewDate();

            SetViewImageSource();

        }

        

        DateTime GetNextDateTime()
        {
            DateTime dateTime = m_dateTime;

            m_pagesText.Text = dateTime.ToString();

            SetDateTime(dateTime);

            m_dateTime = dateTime.Add(new TimeSpan(-1, 0, 0, 0));

            return dateTime;
        }

        void SetViewImageSource()
        {
            foreach (var item in Enumerable.Range(0, VIEW_ITEM_SIZE))
            {
                m_imageSources.Add(new Data(Array.Empty<byte>()));
            }

            m_view.ItemsSource = m_imageSources;

        }

        void SetViewDate()
        {
            m_date.Date = m_dateTime;
        }

        void SetViewHost()
        {
            var vs = Http.GetSource();

            string host = GetHost();

            int index = vs.ToList().FindIndex((s) => s == host);

            index = index == -1 ? 0 : index;

            m_sele.ItemsSource = vs;

            m_sele.SelectedIndex = index;
        }

        static DateTime GetDateTime()
        {

            return Preferences.Get(DATE_SAVE_KEY, DateTime.Today);
        }

        static void SetDateTime(DateTime dateTime)
        {
            Preferences.Set(DATE_SAVE_KEY, dateTime);
        }

        static string GetHost()
        {
            return Preferences.Get(HOST_SAVE_KEY, "");
        }

        static void SetHost(string host)
        {
            Preferences.Set(HOST_SAVE_KEY, host);
        }

        async Task FlushView()
        {
            await Task.Yield();
        }

        Task SetImage(byte[] buffer)
        {
            var date = new Data(buffer);

            m_imageSources.RemoveAt(0);

            m_imageSources.Add(date);


            m_view.ScrollTo(m_imageSources.Count - 1, position: ScrollToPosition.End, animate: false);

            return FlushView();
        }

        static async Task SaveImage(byte[] buffer)
        {
            try
            {

                string name = Path.Combine(ROOT_PATH, Path.GetRandomFileName() + ".png");

                using (var file = new FileStream(name, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                {

                    await file.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                }
            }
            catch(Exception e)
            {
                
            }
        }


        void OnCollectionViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(m_view.SelectedItem != null)
            {
                
                Task t = SaveImage(((Data)m_view.SelectedItem).Buffer);

                m_view.SelectedItem = null;
            }
        }

        async void Start(string host)
        {
            var http = new Http(
                host,
                () => MainThread.InvokeOnMainThreadAsync(GetNextDateTime),
                m_timeOut);

            var imgs = CreateColl.Create(http, m_uriload, m_imageload);



            while (true)
            {
                if (imgs.TryTake(out byte[] buffer))
                {
                    await SetImage(buffer);
                }

                await Task.Delay(ONE_IMAGE_WAIT_TIME * 1000);
            }
        }

        async void OnDeleteFile(object sender, EventArgs e)
        {
            Button button = (Button)sender;

            button.IsEnabled = false;

            try
            {
                await Task.Run(() => DeleteRepeatFile.Statr(ROOT_PATH));


                Task t = DisplayAlert("消息", "Delete 完成", "确定");
            }
            catch
            {
                Task t = DisplayAlert("错误", "Delete error", "确定");
            }
            finally
            {

                button.IsEnabled = true;

            }
        }

        async void OnStart(object sender, EventArgs e)
        {
            m_cons.IsVisible = false;

            m_dateTime = m_date.Date;

            string host = m_sele.SelectedItem.ToString();

            SetHost(host);

            try
            {
                var p = await Permissions.RequestAsync<Permissions.StorageWrite>();

                if (p == PermissionStatus.Granted)
                {

                    Directory.CreateDirectory(ROOT_PATH);

                    Start(host);
                }

            }
            catch
            {
                Task t = DisplayAlert("错误", "需要存储权限", "确定");
            }
        }
    }
}